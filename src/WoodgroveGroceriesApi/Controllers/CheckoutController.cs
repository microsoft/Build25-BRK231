// Copyright (c) Microsoft Corporation. 
// Licensed under the MIT license.

using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web.Resource;
using WoodgroveGroceriesApi.Data;
using WoodgroveGroceriesApi.Middleware;
using WoodgroveGroceriesApi.Models;
using WoodgroveGroceriesApi.Services;

namespace WoodgroveGroceriesApi.Controllers
{
    /// <summary>
    /// Controller for managing checkout and payment operations in the Woodgrove Groceries API.
    /// </summary>
    [TypeFilter(typeof(AllowAnonymousInDevelopmentAttribute))]
    [ApiController]
    [Route("api/[controller]")]
    public class CheckoutController : ControllerBase
    {
        private readonly WoodgroveGroceriesContext _context;
        private readonly ILogger<CheckoutController> _logger;
        private readonly IMfaService _mfaService;

        /// <summary>
        /// Initializes a new instance of the <see cref="CheckoutController"/> class.
        /// </summary>
        /// <param name="context">The database context.</param>
        /// <param name="logger">The logger instance.</param>
        /// <param name="mfaService">The multi-factor authentication service.</param>
        public CheckoutController(
            WoodgroveGroceriesContext context, 
            ILogger<CheckoutController> logger,
            IMfaService mfaService)
        {
            _context = context;
            _logger = logger;
            _mfaService = mfaService;
        }

        /// <summary>
        /// Processes a checkout request.
        /// </summary>
        /// <param name="request">The checkout request containing cart information.</param>
        /// <returns>A checkout response with order details.</returns>
        /// <response code="200">Returns the checkout response with order details.</response>
        /// <response code="400">If the checkout request is invalid or the cart is empty.</response>
        /// <response code="401">If the user is not authenticated.</response>
        /// <response code="403">If the user does not have the required permissions or age verification fails for alcoholic products.</response>
        /// <response code="404">If the cart with the specified ID is not found.</response>
        // POST: api/Checkout
        [HttpPost]
        [RequiredScopeOrAppPermission(
            AcceptedScope = new[] { "Checkout.Process" })]
        public async Task<ActionResult<CheckoutResponse>> Checkout(CheckoutRequest request)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            // Find the cart
            var cart = await _context.Carts
                .Include(c => c.Items)
                .ThenInclude(i => i.Product)
                .FirstOrDefaultAsync(c => c.Id == request.CartId);

            if (cart == null)
            {
                return NotFound("Cart not found");
            }

            if (!cart.Items.Any())
            {
                return BadRequest("Cart is empty");
            }

            // Check for alcoholic products in the cart
            bool hasAlcoholicProducts = cart.Items.Any(i => i.Product.IsAlcoholic);
            
            // Verify age for alcoholic products
            if (hasAlcoholicProducts)
            {
                // Try to get user's date of birth from claims
                var dateOfBirthClaim = HttpContext.User?.FindFirst("DateOfBirth");
                if (dateOfBirthClaim == null || string.IsNullOrEmpty(dateOfBirthClaim.Value))
                {
                    _logger.LogWarning("Cart {CartId} contains alcoholic products but user has no DateOfBirth claim", request.CartId);
                    return StatusCode(403, "Age verification required for purchasing alcoholic products");
                }

                // Parse date of birth and calculate age
                if (!DateTime.TryParse(dateOfBirthClaim.Value, out DateTime birthDate))
                {
                    _logger.LogWarning("Cart {CartId} contains alcoholic products but user's DateOfBirth claim is invalid", request.CartId);
                    return StatusCode(403, "Age verification failed - invalid date format");
                }

                int age = DateTime.Today.Year - birthDate.Year;
                // Adjust age if birthday hasn't occurred yet this year
                if (birthDate.Date > DateTime.Today.AddYears(-age))
                {
                    age--;
                }

                // 🔍 DEMO POINT: AGE GATE
                // Check if user is at least 18 years old
                if (age < 18)
                {
                    _logger.LogWarning("Cart {CartId} contains alcoholic products but user is underage ({Age} years old)", 
                        request.CartId, age);
                    return StatusCode(403, "You must be at least 18 years old to purchase alcoholic products");
                }
                
                _logger.LogInformation("Age verification successful for cart {CartId} - user is {Age} years old", 
                    request.CartId, age);
            }

            // Calculate final total
            decimal totalAmount = cart.Items.Sum(i => i.Price);

            // Check if the transaction amount requires MFA
            bool requiresMfa = await _mfaService.RequiresMfaAsync(totalAmount);
            
            // Check if user already has completed MFA
            bool mfaCompleted = false;
            if (requiresMfa)
            {
                mfaCompleted = _mfaService.IsMfaCompleted(HttpContext);
                if (mfaCompleted)
                {
                    _logger.LogInformation("MFA was required but user already completed it for cart {CartId}", request.CartId);
                    // If MFA is already completed, we don't need to require it again
                    requiresMfa = false;
                }
            }

            // Create order
            var order = new Order
            {
                Id = Guid.NewGuid().ToString(),
                CartId = cart.Id,   
                TotalAmount = totalAmount,
                Currency = "USD",
                Status = requiresMfa ? "MFA_REQUIRED" : "PENDING_PAYMENT",
                Address = request.Address,
                CreatedAt = DateTime.UtcNow,
                ContainsAlcohol = hasAlcoholicProducts
            };

            _context.Orders.Add(order);
            await _context.SaveChangesAsync();

            // Return checkout response
            var response = new CheckoutResponse
            {
                OrderId = order.Id,
                CartId = order.CartId,
                TotalAmount = order.TotalAmount,
                Currency = order.Currency,
                Status = order.Status,
                RequiresMfa = requiresMfa
            };

            if (requiresMfa)
            {
                _logger.LogInformation("Checkout requires MFA verification for cart {CartId} with total {TotalAmount}",
                    request.CartId, totalAmount);
            }
            else
            {
                _logger.LogInformation("Checkout completed for cart {CartId}, order {OrderId} created", 
                    request.CartId, order.Id);
            }

            return Ok(response);
        }

        /// <summary>
        /// Processes a payment for a specific order.
        /// </summary>
        /// <param name="orderId">The ID of the order to be paid.</param>
        /// <param name="paymentRequest">The payment request containing payment details.</param>
        /// <returns>A payment response with transaction details.</returns>
        /// <response code="200">Returns the payment response with transaction details.</response>
        /// <response code="400">If the payment request is invalid.</response>
        /// <response code="401">If the user is not authenticated.</response>
        /// <response code="403">If the user does not have the required permissions or MFA is required but not completed.</response>
        /// <response code="404">If the order with the specified ID is not found.</response>
        /// <response code="402">If the payment fails.</response>
        // POST: api/Checkout/{orderId}/pay
        [HttpPost("{orderId}/pay")]
        [RequiredScopeOrAppPermission(
            AcceptedScope = new[] { "Checkout.Process" })]
        public async Task<ActionResult<PaymentResponse>> ProcessPayment(string orderId, PaymentRequest paymentRequest)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            // Find the order
            var order = await _context.Orders
                .Include(o => o.Cart)
                .ThenInclude(c => c.Items)
                .ThenInclude(i => i.Product)
                .FirstOrDefaultAsync(o => o.Id == orderId);
                
            if (order == null)
            {
                return NotFound("Order not found");
            }
            
            // Check if order contains alcoholic products and verify age if needed
            if (order.ContainsAlcohol)
            {
                // Try to get user's date of birth from claims
                var dateOfBirthClaim = HttpContext.User?.FindFirst("DateOfBirth");
                if (dateOfBirthClaim == null || string.IsNullOrEmpty(dateOfBirthClaim.Value))
                {
                    _logger.LogWarning("Order {OrderId} contains alcoholic products but user has no DateOfBirth claim", orderId);
                    return StatusCode(403, "Age verification required for purchasing alcoholic products");
                }

                // Parse date of birth and calculate age
                if (!DateTime.TryParse(dateOfBirthClaim.Value, out DateTime birthDate))
                {
                    _logger.LogWarning("Order {OrderId} contains alcoholic products but user's DateOfBirth claim is invalid", orderId);
                    return StatusCode(403, "Age verification failed - invalid date format");
                }

                int age = DateTime.Today.Year - birthDate.Year;
                // Adjust age if birthday hasn't occurred yet this year
                if (birthDate.Date > DateTime.Today.AddYears(-age))
                {
                    age--;
                }

                // Check if user is at least 18 years old
                if (age < 18)
                {
                    _logger.LogWarning("Order {OrderId} contains alcoholic products but user is underage ({Age} years old)", 
                        orderId, age);
                    return StatusCode(403, "You must be at least 18 years old to purchase alcoholic products");
                }
                
                _logger.LogInformation("Age verification successful for order {OrderId} - user is {Age} years old", 
                    orderId, age);
            }

            // Check if MFA was required for this order
            if (order.Status == "MFA_REQUIRED")
            {
                // Use our MfaService to check if MFA is completed
                bool mfaCompleted = _mfaService.IsMfaCompleted(HttpContext);
                
                if (!mfaCompleted)
                {
                    _logger.LogWarning("Payment attempted without required MFA for order {OrderId}", orderId);
                    return StatusCode(403, new PaymentResponse
                    {
                        OrderId = order.Id,
                        PaymentStatus = "MFA_REQUIRED",
                        TransactionId = null,
                        RequiresMfa = true
                    });
                }
                
                _logger.LogInformation("MFA verification successful for order {OrderId}", orderId);
            }

            // Process payment (in a real app, this would integrate with a payment gateway)
            bool paymentSuccess = ProcessPaymentWithGateway(order, paymentRequest);
            var transactionId = Guid.NewGuid().ToString();

            if (paymentSuccess)
            {
                // Update order status
                order.Status = "PAID";
                order.PaymentTransactionId = transactionId;
                await _context.SaveChangesAsync();

                _logger.LogInformation("Payment successful for order {OrderId}, transaction {TransactionId}", 
                    orderId, transactionId);

                return Ok(new PaymentResponse
                {
                    OrderId = order.Id,
                    PaymentStatus = "PAID",
                    TransactionId = transactionId,
                    RequiresMfa = false
                });
            }
            else
            {
                order.Status = "PAYMENT_FAILED";
                await _context.SaveChangesAsync();

                _logger.LogWarning("Payment failed for order {OrderId}", orderId);

                return StatusCode(402, new PaymentResponse
                {
                    OrderId = order.Id,
                    PaymentStatus = "FAILED",
                    TransactionId = null,
                    RequiresMfa = false
                });
            }
        }

        // Simulated payment processing
        private bool ProcessPaymentWithGateway(Order order, PaymentRequest paymentRequest)
        {
            // In a real application, this would integrate with a payment gateway
            // For demo purposes, we'll simulate payment success/failure based on certain criteria

            // Simulate payment failure for specific scenarios
            if (string.Equals(paymentRequest.CardNumber, "4111111111111111", StringComparison.OrdinalIgnoreCase))
            {
                return false; // Test card for failure simulation
            }

            // For demo purposes, mark as successful
            return true;
        }
    }
}