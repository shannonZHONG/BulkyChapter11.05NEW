using Bulky.DataAccess.Repository.IRepository;
using Bulky.Models;
using Bulky.Models.ViewModels;
using Bulky.Utility;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stripe.Checkout;
using System.Security.Claims;

namespace BulkyBook2.Areas.Customer.Controllers
{
    [Area("Customer")]
    [Authorize]
    public class CartController : Controller
    {


        private readonly IUnitOfWork? _unitOfWork;
        [BindProperty]
        public ShoppingCartVM? ShoppingCartVM { get; set; }

        public CartController(IUnitOfWork unitOfWork) { 
         _unitOfWork = unitOfWork;
        
        }

        public IActionResult Index()
        {
            var claimsIdentity = (ClaimsIdentity)User.Identity;
            var userId = claimsIdentity.FindFirst(ClaimTypes.NameIdentifier).Value;

            ShoppingCartVM = new()
            {
                ShoppingCartList = _unitOfWork.ShoppingCart.GetAll(u => u.ApplicationUserId == userId,
                includeProperties: "product"),
                OrderOfHeader =new()

            };

            foreach (var cart in ShoppingCartVM.ShoppingCartList) { 
            cart. Price =GetPriceBasedOnQuantity(cart);
                // could be called orderTotal it make sence.
                ShoppingCartVM.OrderOfHeader.OrderTotal += (cart.Price * cart.Count);
            }

            return View(ShoppingCartVM);
        }

        public IActionResult Summary() {
            var claimsIdentity = (ClaimsIdentity)User.Identity;
            var userId = claimsIdentity.FindFirst(ClaimTypes.NameIdentifier).Value;

            ShoppingCartVM = new()
            {
                ShoppingCartList = _unitOfWork.ShoppingCart.GetAll(u => u.ApplicationUserId == userId,
                includeProperties: "product"),
                OrderOfHeader = new()

            };

            ShoppingCartVM.OrderOfHeader.ApplicationUser = _unitOfWork.ApplicationUser.Get(u => u.Id == userId);

            ShoppingCartVM.OrderOfHeader.Name = ShoppingCartVM.OrderOfHeader.ApplicationUser.Name;
            ShoppingCartVM.OrderOfHeader.PhoneNumber = ShoppingCartVM.OrderOfHeader.ApplicationUser.PhoneNumber;
            ShoppingCartVM.OrderOfHeader.StreetAddress = ShoppingCartVM.OrderOfHeader.ApplicationUser.StreetAddress;
            ShoppingCartVM.OrderOfHeader.City = ShoppingCartVM.OrderOfHeader.ApplicationUser.City;
            ShoppingCartVM.OrderOfHeader.State = ShoppingCartVM.OrderOfHeader.ApplicationUser.State;
            ShoppingCartVM.OrderOfHeader.PostalCode = ShoppingCartVM.OrderOfHeader.ApplicationUser.PostalCode;

            foreach (var cart in ShoppingCartVM.ShoppingCartList)
            {
                cart.Price = GetPriceBasedOnQuantity(cart);
                // could be called orderTotal it make sence.
                ShoppingCartVM.OrderOfHeader.OrderTotal += (cart.Price * cart.Count);
            }

            return View(ShoppingCartVM);
        }

        [HttpPost]
        [ActionName("Summary")]
		public IActionResult SummaryPOST()
		{
			var claimsIdentity = (ClaimsIdentity)User.Identity;
			var userId = claimsIdentity.FindFirst(ClaimTypes.NameIdentifier).Value;

            ShoppingCartVM.ShoppingCartList = _unitOfWork.ShoppingCart.GetAll(u => u.ApplicationUserId == userId,
                includeProperties: "product");


			ShoppingCartVM.OrderOfHeader.OrderDate = System.DateTime.Now;
			ShoppingCartVM.OrderOfHeader.ApplicationUserId = userId;

            ApplicationUser applicationUser = _unitOfWork.ApplicationUser.Get(u => u.Id == userId);

            foreach (var cart in ShoppingCartVM.ShoppingCartList)
			{
				cart.Price = GetPriceBasedOnQuantity(cart);
				// could be called orderTotal it make sence.
				ShoppingCartVM.OrderOfHeader.OrderTotal += (cart.Price * cart.Count);
			}
			if (applicationUser.CompanyId.GetValueOrDefault() == 0)
			{
				//it is a regular customer 
				ShoppingCartVM.OrderOfHeader.PaymentStatus = Ts.PaymentStatusPending;
				ShoppingCartVM.OrderOfHeader.OrderStatus = Ts.StatusPending;
			}
			else
			{
				//it is a company user
				ShoppingCartVM.OrderOfHeader.PaymentStatus = Ts.PaymentStatusDelayedPayment;
				ShoppingCartVM.OrderOfHeader.OrderStatus = Ts.StatusApproved;
			}

			_unitOfWork.OrderOfHeader.Add(ShoppingCartVM.OrderOfHeader);
			_unitOfWork.Save();

			foreach (var cart in ShoppingCartVM.ShoppingCartList)
			{
				OrderDetail orderDetail = new()
				{
					ProductId = cart.ProductId,
					OrderHeaderId = ShoppingCartVM.OrderOfHeader.Id,
					Price = cart.Price,
					Count = cart.Count
				};
				_unitOfWork.OrderDetail.Add(orderDetail);
				_unitOfWork.Save();
			}

			if (applicationUser.CompanyId.GetValueOrDefault() == 0)
			{
                //it is a regular customer account and we need to capture payment
                // stripe logic
                var domain = "https://localhost:7280/";
                var options = new SessionCreateOptions
                {
                    SuccessUrl = domain + $"Customer/Cart/OrderConfirmation?id={ShoppingCartVM.OrderOfHeader.Id}",
                    CancelUrl = domain + "Customer/Cart/Index",
                    LineItems = new List<SessionLineItemOptions>(),
                    
                    Mode = "payment",
                };

                foreach (var item in ShoppingCartVM.ShoppingCartList)
                {
                    var sessionLineItem = new SessionLineItemOptions
                    {
                        PriceData = new SessionLineItemPriceDataOptions
                        {
                            UnitAmount = (long)(item.Price * 100), // $20.50 => 2050
                            Currency = "usd",
                            ProductData = new SessionLineItemPriceDataProductDataOptions
                            {
                                Name = item.product.Title
                            }
                        },
                        Quantity = item.Count
                    };
                    options.LineItems.Add(sessionLineItem);
                }

                var service = new SessionService();
                Session session = service.Create(options);
                _unitOfWork.OrderOfHeader.UpdateStripePaymentID(ShoppingCartVM.OrderOfHeader.Id, session.Id, session.PaymentIntentId);
                _unitOfWork.Save();
                Response.Headers.Add("Location", session.Url);
                return new StatusCodeResult(303);


            }

			return RedirectToAction(nameof(OrderConfirmation),new { id=ShoppingCartVM.OrderOfHeader.Id});
		}


        public IActionResult OrderConfirmation(int id)
        {

            OrderOfHeader orderHeader = _unitOfWork.OrderOfHeader.Get(u => u.Id == id, includeProperties: "ApplicationUser");
            if (orderHeader.PaymentStatus != Ts.PaymentStatusDelayedPayment)
            {
                //this is an order by customer

                var service = new SessionService();
                Session session = service.Get(orderHeader.SessionId);

                if (session.PaymentStatus.ToLower() == "paid")
                {
                    _unitOfWork.OrderOfHeader.UpdateStripePaymentID(id, session.Id, session.PaymentIntentId);
                    _unitOfWork.OrderOfHeader.UpdateStatus(id, Ts.StatusApproved, Ts.PaymentStatusApproved);
                    _unitOfWork.Save();
                }


            }



            List<shoppingCart> shoppingCarts = _unitOfWork.ShoppingCart
                .GetAll(u => u.ApplicationUserId == orderHeader.ApplicationUserId).ToList();

            _unitOfWork.ShoppingCart.RemoveRange(shoppingCarts);
            _unitOfWork.Save();

            return View(id);
        }



        public IActionResult Plus(int cartId)
        {
              var cartFromDb =_unitOfWork.ShoppingCart.Get(u=>u.Id == cartId);
              cartFromDb.Count += 1;
              _unitOfWork.ShoppingCart.Update(cartFromDb);
              _unitOfWork.Save();
              return RedirectToAction(nameof(Index));
        }

        public IActionResult Minus(int cartId) {

            var cartFromDb = _unitOfWork.ShoppingCart.Get(u => u.Id == cartId,tracked:true);
            if (cartFromDb.Count <= 1)
            {
                HttpContext.Session.SetInt32(Ts.SessionCart,
                _unitOfWork.ShoppingCart.GetAll(u => u.ApplicationUserId == cartFromDb.ApplicationUserId).Count() - 1);
                _unitOfWork.ShoppingCart.Remove(cartFromDb);
            }
            else {
                cartFromDb.Count -= 1;
                _unitOfWork.ShoppingCart.Update(cartFromDb);
            }
           
            _unitOfWork.Save();
            return RedirectToAction(nameof(Index));

        }


        public IActionResult Remove(int cartId)
        {

            var cartFromDb = _unitOfWork.ShoppingCart.Get(u => u.Id == cartId,tracked:true);
            HttpContext.Session.SetInt32(Ts.SessionCart,
             _unitOfWork.ShoppingCart.GetAll(u => u.ApplicationUserId == cartFromDb.ApplicationUserId).Count()-1);
            _unitOfWork.ShoppingCart.Remove(cartFromDb);
            _unitOfWork.Save();
            return RedirectToAction(nameof(Index));

        }

        private double GetPriceBasedOnQuantity(shoppingCart shoppingCart)
        {
            if (shoppingCart.Count <= 50)
            {
                    return shoppingCart.product.Price;
            }
            else
            {
                if (shoppingCart.Count <= 100)
                {
                    return shoppingCart.product.Price50;
                }
                else
                {
                    return shoppingCart.product.Price100;
                }
            }
        }
    }
}
