using Bulky.DataAccess.Repository.IRepository;
using Bulky.Models;
using Bulky.Models.ViewModels;
using Bulky.Utility;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using System.Collections;
using System.Security.Claims;

namespace BulkyBook2.Areas.Admin.Controllers
{
    [Area("admin")]
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
