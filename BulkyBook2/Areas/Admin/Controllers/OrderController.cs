using Bulky.DataAccess.Repository.IRepository;
using Bulky.Models;
using Bulky.Models.ViewModels;
using Bulky.Utility;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Stripe;
using Stripe.Checkout;
using System.Collections;
using System.Data;
using System.Security.Claims;

namespace BulkyBook2.Areas.Admin.Controllers
{
    [Area("admin")]
    [Authorize]
    public class OrderController : Controller {

        private readonly IUnitOfWork _unitOfWork;
        [BindProperty]
        public OrderVM OrderVM { get; set; }
        public OrderController(IUnitOfWork unitOfWork) {
          _unitOfWork = unitOfWork;
        }

        public IActionResult Index()
        {
            return View();
        }

        public IActionResult Details(int orderId)
        {

            OrderVM = new()
            {
                OrderOfHeader = _unitOfWork.OrderOfHeader.Get(u => u.Id == orderId, includeProperties: "ApplicationUser"),
                orderDetails = _unitOfWork.OrderDetail.GetAll(u => u.OrderHeaderId == orderId, includeProperties: "Product")
            };
            return View(OrderVM);
        }


        [HttpPost]
        [Authorize(Roles = Ts.Role_Admin + "," + Ts.Role_Employee)]
        public IActionResult UpdateOrderDetail()
        {
            var orderHeaderFromDb = _unitOfWork.OrderOfHeader.Get(u => u.Id == OrderVM.OrderOfHeader.Id);
            orderHeaderFromDb.Name = OrderVM.OrderOfHeader.Name;
            orderHeaderFromDb.PhoneNumber = OrderVM.OrderOfHeader.PhoneNumber;
            orderHeaderFromDb.StreetAddress = OrderVM.OrderOfHeader.StreetAddress;
            orderHeaderFromDb.City = OrderVM.OrderOfHeader.City;
            orderHeaderFromDb.State = OrderVM.OrderOfHeader.State;
            orderHeaderFromDb.PostalCode = OrderVM.OrderOfHeader.PostalCode;
            if (!string.IsNullOrEmpty(OrderVM.OrderOfHeader.Carrier))
            {
                orderHeaderFromDb.Carrier = OrderVM.OrderOfHeader.Carrier;
            }
            if (!string.IsNullOrEmpty(OrderVM.OrderOfHeader.TrackingNumber))
            {
                orderHeaderFromDb.Carrier = OrderVM.OrderOfHeader.TrackingNumber;
            }
            _unitOfWork.OrderOfHeader.Update(orderHeaderFromDb);
            _unitOfWork.Save();

            TempData["Success"] = "Order Details Updated Successfully.";


            return RedirectToAction(nameof(Details), new { orderId = orderHeaderFromDb.Id });
        }


        [HttpPost]
        [Authorize(Roles = Ts.Role_Admin + "," + Ts.Role_Employee)]
        public IActionResult StartProcessing()
        {
            _unitOfWork.OrderOfHeader.UpdateStatus(OrderVM.OrderOfHeader.Id, Ts.StatusInProcess);
            _unitOfWork.Save();
            TempData["Success"] = "Order Details Updated Successfully.";
            return RedirectToAction(nameof(Details), new { orderId = OrderVM.OrderOfHeader.Id });
        }

        [HttpPost]
        [Authorize(Roles = Ts.Role_Admin + "," + Ts.Role_Employee)]
        public IActionResult ShipOrder()
        {

            var orderHeader = _unitOfWork.OrderOfHeader.Get(u => u.Id == OrderVM.OrderOfHeader.Id);
            orderHeader.TrackingNumber = OrderVM.OrderOfHeader.TrackingNumber;
            orderHeader.Carrier = OrderVM.OrderOfHeader.Carrier;
            orderHeader.OrderStatus = Ts.StatusShipped;
            orderHeader.ShippingDate = DateTime.Now;
            if (orderHeader.PaymentStatus == Ts.PaymentStatusDelayedPayment)
            {
                orderHeader.PaymentDueDate = DateTime.Now.AddDays(30);
            }

            _unitOfWork.OrderOfHeader.Update(orderHeader);
            _unitOfWork.Save();
            TempData["Success"] = "Order Shipped Successfully.";
            return RedirectToAction(nameof(Details), new { orderId = OrderVM.OrderOfHeader.Id });
        }


        [HttpPost]
        [Authorize(Roles = Ts.Role_Admin + "," + Ts.Role_Employee)]
        public IActionResult CancelOrder()
        {

            var orderHeader = _unitOfWork.OrderOfHeader.Get(u => u.Id == OrderVM.OrderOfHeader.Id);

            if (orderHeader.PaymentStatus == Ts.PaymentStatusApproved)
            {
                var options = new RefundCreateOptions
                {
                    Reason = RefundReasons.RequestedByCustomer,
                    PaymentIntent = orderHeader.PaymentIntentId
                };

                var service = new RefundService();
                Refund refund = service.Create(options);

                _unitOfWork.OrderOfHeader.UpdateStatus(orderHeader.Id, Ts.StatusCancelled, Ts.StatusRefunded);
            }
            else
            {
                _unitOfWork.OrderOfHeader.UpdateStatus(orderHeader.Id, Ts.StatusCancelled, Ts.StatusCancelled);
            }
            _unitOfWork.Save();
            TempData["Success"] = "Order Cancelled Successfully.";
            return RedirectToAction(nameof(Details), new { orderId = OrderVM.OrderOfHeader.Id });

        }

        [ActionName("Details")]
        [HttpPost]
        public IActionResult Details_PAY_NOW()
        {
            OrderVM.OrderOfHeader = _unitOfWork.OrderOfHeader
                .Get(u => u.Id == OrderVM.OrderOfHeader.Id, includeProperties: "ApplicationUser");
            OrderVM.orderDetails = _unitOfWork.OrderDetail
                .GetAll(u => u.OrderHeaderId == OrderVM.OrderOfHeader.Id, includeProperties: "Product");

            //stripe logic
            var domain = Request.Scheme + "://" + Request.Host.Value + "/";
            var options = new SessionCreateOptions
            {
                SuccessUrl = domain + $"admin/order/PaymentConfirmation?orderHeaderId={OrderVM.OrderOfHeader.Id}",
                CancelUrl = domain + $"admin/order/details?orderId={OrderVM.OrderOfHeader.Id}",
                LineItems = new List<SessionLineItemOptions>(),
                Mode = "payment",
            };

            foreach (var item in OrderVM.orderDetails)
            {
                var sessionLineItem = new SessionLineItemOptions
                {
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        UnitAmount = (long)(item.Price * 100), // $20.50 => 2050
                        Currency = "usd",
                        ProductData = new SessionLineItemPriceDataProductDataOptions
                        {
                            Name = item.Product.Title
                        }
                    },
                    Quantity = item.Count
                };
                options.LineItems.Add(sessionLineItem);
            }


            var service = new SessionService();
            Session session = service.Create(options);
            _unitOfWork.OrderOfHeader.UpdateStripePaymentID(OrderVM.OrderOfHeader.Id, session.Id, session.PaymentIntentId);
            _unitOfWork.Save();
            Response.Headers.Add("Location", session.Url);
            return new StatusCodeResult(303);
        }

        public IActionResult PaymentConfirmation(int orderHeaderId)
        {

            OrderOfHeader orderHeader = _unitOfWork.OrderOfHeader.Get(u => u.Id == orderHeaderId);
            if (orderHeader.PaymentStatus == Ts.PaymentStatusDelayedPayment)
            {
                //this is an order by company

                var service = new SessionService();
                Session session = service.Get(orderHeader.SessionId);

                if (session.PaymentStatus.ToLower() == "paid")
                {
                    _unitOfWork.OrderOfHeader.UpdateStripePaymentID(orderHeaderId, session.Id, session.PaymentIntentId);
                    _unitOfWork.OrderOfHeader.UpdateStatus(orderHeaderId, orderHeader.OrderStatus, Ts.PaymentStatusApproved);
                    _unitOfWork.Save();
                }


            }


            return View(orderHeaderId);
        }



        #region API CALLS
        [HttpGet]
        public IActionResult GetAll(string status)
        {
            IEnumerable<OrderOfHeader> objOrderHeaders =_unitOfWork.OrderOfHeader.GetAll(includeProperties:"ApplicationUser").ToList();

            if (User.IsInRole(Ts.Role_Admin) || User.IsInRole(Ts.Role_Employee))
            {
                objOrderHeaders = _unitOfWork.OrderOfHeader.GetAll(includeProperties: "ApplicationUser").ToList();
            }
            else
            {

                var claimsIdentity = (ClaimsIdentity)User.Identity;
                var userId = claimsIdentity.FindFirst(ClaimTypes.NameIdentifier).Value;

                objOrderHeaders = _unitOfWork.OrderOfHeader
                    .GetAll(u => u.ApplicationUserId == userId, includeProperties: "ApplicationUser");
            }

            switch (status)
            {
                case "pending":
                    objOrderHeaders = objOrderHeaders.Where(u => u.PaymentStatus == Ts.PaymentStatusDelayedPayment);
                    break;
                case "inprocess":
                    objOrderHeaders = objOrderHeaders.Where(u => u.OrderStatus == Ts.StatusInProcess);
                    break;
                case "completed":
                    objOrderHeaders = objOrderHeaders.Where(u => u.OrderStatus == Ts.StatusShipped);
                    break;
                case "approved":
                    objOrderHeaders = objOrderHeaders.Where(u => u.OrderStatus == Ts.StatusApproved);
                    break;
                default:
                    break;

            }



            return Json(new { data=objOrderHeaders });
        }



        #endregion


    }
}
