// Copyright (c) Microsoft Corporation. 
// Licensed under the MIT license.

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Query;
using Microsoft.AspNetCore.OData.Routing.Controllers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web.Resource;
using WoodgroveGroceriesApi.Data;
using WoodgroveGroceriesApi.Middleware;
using WoodgroveGroceriesApi.Models;

namespace WoodgroveGroceriesApi.Controllers
{
    /// <summary>
    /// Controller for managing shopping carts in the Woodgrove Groceries API.
    /// </summary>
    [TypeFilter(typeof(AllowAnonymousInDevelopmentAttribute))]
    [ApiController]
    [Route("api/[controller]")]
    public class CartsController : ODataController
    {
        private readonly WoodgroveGroceriesContext _context;
        private readonly ILogger<CartsController> _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="CartsController"/> class.
        /// </summary>
        /// <param name="context">The database context.</param>
        /// <param name="logger">The logger instance.</param>
        public CartsController(WoodgroveGroceriesContext context, ILogger<CartsController> logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// Gets all shopping carts.
        /// </summary>
        /// <returns>A collection of shopping carts.</returns>
        /// <response code="200">Returns the list of shopping carts.</response>
        /// <response code="401">If the user is not authenticated.</response>
        /// <response code="403">If the user does not have the required permissions.</response>
        // GET: api/Carts
        [HttpGet]
        [EnableQuery]
        [RequiredScopeOrAppPermission(
            AcceptedScope = new[] { "Carts.Read" })]
        public IActionResult GetCarts()
        {
            return Ok(_context.Carts);
        }

        /// <summary>
        /// Gets a specific shopping cart by ID.
        /// </summary>
        /// <param name="cartId">The ID of the shopping cart to retrieve.</param>
        /// <returns>The requested shopping cart.</returns>
        /// <response code="200">Returns the requested shopping cart.</response>
        /// <response code="401">If the user is not authenticated.</response>
        /// <response code="403">If the user does not have the required permissions.</response>
        /// <response code="404">If the shopping cart with the specified ID is not found.</response>
        // GET: api/Carts/{id}
        [HttpGet("{cartId}")]
        [EnableQuery]
        [RequiredScopeOrAppPermission(
            AcceptedScope = new[] { "Carts.Read" })]
        public async Task<IActionResult> GetCart(string cartId)
        {
            var cart = await _context.Carts
                .Include(c => c.Items)
                .ThenInclude(i => i.Product)
                .FirstOrDefaultAsync(c => c.Id == cartId);

            if (cart == null)
            {
                return NotFound();
            }

            // Calculate total price based on items in cart
            CalculateCartTotal(cart);
            await _context.SaveChangesAsync();

            return Ok(cart);
        }

        /// <summary>
        /// Creates a new shopping cart.
        /// </summary>
        /// <param name="cartCreate">The details of the cart to create.</param>
        /// <returns>The created cart.</returns>
        /// <response code="201">Returns the newly created cart.</response>
        /// <response code="400">If the cart creation request is invalid.</response>
        /// <response code="401">If the user is not authenticated.</response>
        /// <response code="403">If the user does not have the required permissions.</response>
        // POST: api/Carts
        [HttpPost]
        [RequiredScopeOrAppPermission(
            AcceptedScope = new[] { "Carts.Write" })]
        public async Task<ActionResult<Cart>> CreateCart(CartCreate cartCreate)
        {
            var cart = new Cart
            {
                Id = Guid.NewGuid().ToString(),
                UserId = cartCreate.UserId,
                TotalPrice = 0
            };

            _context.Carts.Add(cart);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetCart), new { cartId = cart.Id }, cart);
        }

        /// <summary>
        /// Replaces an existing shopping cart with new details.
        /// </summary>
        /// <param name="cartId">The ID of the cart to replace.</param>
        /// <param name="cartUpdate">The new details of the cart.</param>
        /// <returns>The updated cart.</returns>
        /// <response code="200">Returns the updated cart.</response>
        /// <response code="400">If the cart update request is invalid.</response>
        /// <response code="401">If the user is not authenticated.</response>
        /// <response code="403">If the user does not have the required permissions.</response>
        /// <response code="404">If the cart with the specified ID is not found.</response>
        // PUT: api/Carts/{id}
        [HttpPut("{cartId}")]
        [RequiredScopeOrAppPermission(
            AcceptedScope = new[] { "Carts.Write" })]
        public async Task<IActionResult> ReplaceCart(string cartId, CartUpdate cartUpdate)
        {
            var cart = await _context.Carts.FindAsync(cartId);

            if (cart == null)
            {
                return NotFound();
            }

            if (cartUpdate.UserId != null)
            {
                cart.UserId = cartUpdate.UserId;
            }

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!CartExists(cartId))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return Ok(cart);
        }

        /// <summary>
        /// Updates specific details of an existing shopping cart.
        /// </summary>
        /// <param name="cartId">The ID of the cart to update.</param>
        /// <param name="cartUpdate">The new details of the cart.</param>
        /// <returns>The updated cart.</returns>
        /// <response code="200">Returns the updated cart.</response>
        /// <response code="400">If the cart update request is invalid.</response>
        /// <response code="401">If the user is not authenticated.</response>
        /// <response code="403">If the user does not have the required permissions.</response>
        /// <response code="404">If the cart with the specified ID is not found.</response>
        // PATCH: api/Carts/{id}
        [HttpPatch("{cartId}")]
        [RequiredScopeOrAppPermission(
            AcceptedScope = new[] { "Carts.Write" })]
        public async Task<IActionResult> UpdateCart(string cartId, CartUpdate cartUpdate)
        {
            var cart = await _context.Carts.FindAsync(cartId);

            if (cart == null)
            {
                return NotFound();
            }

            if (cartUpdate.UserId != null)
            {
                cart.UserId = cartUpdate.UserId;
            }

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!CartExists(cartId))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return Ok(cart);
        }

        /// <summary>
        /// Deletes a specific shopping cart by ID.
        /// </summary>
        /// <param name="cartId">The ID of the cart to delete.</param>
        /// <returns>No content.</returns>
        /// <response code="204">If the cart was successfully deleted.</response>
        /// <response code="401">If the user is not authenticated.</response>
        /// <response code="403">If the user does not have the required permissions.</response>
        /// <response code="404">If the cart with the specified ID is not found.</response>
        // DELETE: api/Carts/{id}
        [HttpDelete("{cartId}")]
        [RequiredScopeOrAppPermission(
            AcceptedScope = new[] { "Carts.Write" })]
        public async Task<IActionResult> DeleteCart(string cartId)
        {
            var cart = await _context.Carts
                .Include(c => c.Items)
                .FirstOrDefaultAsync(c => c.Id == cartId);

            if (cart == null)
            {
                return NotFound();
            }

            _context.CartItems.RemoveRange(cart.Items);
            _context.Carts.Remove(cart);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        /// <summary>
        /// Gets all items in a specific shopping cart.
        /// </summary>
        /// <param name="cartId">The ID of the cart to retrieve items from.</param>
        /// <returns>A collection of cart items.</returns>
        /// <response code="200">Returns the list of cart items.</response>
        /// <response code="401">If the user is not authenticated.</response>
        /// <response code="403">If the user does not have the required permissions.</response>
        /// <response code="404">If the cart with the specified ID is not found.</response>
        // GET: api/Carts/{cartId}/items
        [HttpGet("{cartId}/items")]
        [EnableQuery]
        [RequiredScopeOrAppPermission(
            AcceptedScope = new[] { "Carts.Read" })]
        public async Task<ActionResult<IEnumerable<CartItem>>> GetCartItems(string cartId)
        {
            if (!CartExists(cartId))
            {
                return NotFound();
            }

            var cartItems = await _context.CartItems
                .Include(i => i.Product)
                .Where(i => i.CartId == cartId)
                .ToListAsync();

            return Ok(cartItems);
        }

        /// <summary>
        /// Adds a new item to a specific shopping cart.
        /// </summary>
        /// <param name="cartId">The ID of the cart to add the item to.</param>
        /// <param name="itemCreate">The details of the item to add.</param>
        /// <returns>The created cart item.</returns>
        /// <response code="201">Returns the newly created cart item.</response>
        /// <response code="400">If the item creation request is invalid.</response>
        /// <response code="401">If the user is not authenticated.</response>
        /// <response code="403">If the user does not have the required permissions.</response>
        /// <response code="404">If the cart or product with the specified ID is not found.</response>
        // POST: api/Carts/{cartId}/items
        [HttpPost("{cartId}/items")]
        [RequiredScopeOrAppPermission(
            AcceptedScope = new[] { "Carts.Write" })]
        public async Task<ActionResult<CartItem>> AddItemToCart(string cartId, CartItemCreate itemCreate)
        {
            if (!CartExists(cartId))
            {
                return NotFound("Cart not found");
            }

            var product = await _context.Products.FindAsync(itemCreate.ProductId);
            if (product == null)
            {
                return NotFound("Product not found");
            }

            // Check if item already exists in cart
            var existingItem = await _context.CartItems
                .FirstOrDefaultAsync(i => i.CartId == cartId && i.ProductId == itemCreate.ProductId);

            if (existingItem != null)
            {
                // Update quantity if item already exists
                existingItem.Quantity += itemCreate.Quantity;
                existingItem.Price = CalculateItemPrice(product, existingItem.Quantity);
            }
            else
            {
                // Create new cart item
                var cartItem = new CartItem
                {
                    ItemId = Guid.NewGuid().ToString(),
                    CartId = cartId,
                    ProductId = itemCreate.ProductId,
                    Quantity = itemCreate.Quantity,
                    Price = CalculateItemPrice(product, itemCreate.Quantity)
                };

                _context.CartItems.Add(cartItem);
                existingItem = cartItem;
            }

            // Update cart total
            var cart = await _context.Carts
                .Include(c => c.Items)
                .ThenInclude(i => i.Product)
                .FirstOrDefaultAsync(c => c.Id == cartId);

            CalculateCartTotal(cart);

            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetCartItem), 
                new { cartId = cartId, itemId = existingItem.ItemId }, 
                existingItem);
        }

        /// <summary>
        /// Gets a specific item from a shopping cart by ID.
        /// </summary>
        /// <param name="cartId">The ID of the cart to retrieve the item from.</param>
        /// <param name="itemId">The ID of the item to retrieve.</param>
        /// <returns>The requested cart item.</returns>
        /// <response code="200">Returns the requested cart item.</response>
        /// <response code="401">If the user is not authenticated.</response>
        /// <response code="403">If the user does not have the required permissions.</response>
        /// <response code="404">If the cart item with the specified ID is not found.</response>
        // GET: api/Carts/{cartId}/items/{itemId}
        [HttpGet("{cartId}/items/{itemId}")]
        [EnableQuery]
        [RequiredScopeOrAppPermission(
            AcceptedScope = new[] { "Carts.Read" })]
        public async Task<ActionResult<CartItem>> GetCartItem(string cartId, string itemId)
        {
            var cartItem = await _context.CartItems
                .Include(i => i.Product)
                .FirstOrDefaultAsync(i => i.CartId == cartId && i.ItemId == itemId);

            if (cartItem == null)
            {
                return NotFound();
            }

            return Ok(cartItem);
        }

        /// <summary>
        /// Replaces an existing item in a shopping cart with new details.
        /// </summary>
        /// <param name="cartId">The ID of the cart to update the item in.</param>
        /// <param name="itemId">The ID of the item to replace.</param>
        /// <param name="itemUpdate">The new details of the item.</param>
        /// <returns>The updated cart item.</returns>
        /// <response code="200">Returns the updated cart item.</response>
        /// <response code="400">If the item update request is invalid.</response>
        /// <response code="401">If the user is not authenticated.</response>
        /// <response code="403">If the user does not have the required permissions.</response>
        /// <response code="404">If the cart item with the specified ID is not found.</response>
        // PUT: api/Carts/{cartId}/items/{itemId}
        [HttpPut("{cartId}/items/{itemId}")]
        [RequiredScopeOrAppPermission(
            AcceptedScope = new[] { "Carts.Write" })]
        public async Task<IActionResult> ReplaceCartItem(string cartId, string itemId, CartItemUpdate itemUpdate)
        {
            var cartItem = await _context.CartItems
                .Include(i => i.Product)
                .FirstOrDefaultAsync(i => i.CartId == cartId && i.ItemId == itemId);

            if (cartItem == null)
            {
                return NotFound();
            }

            if (itemUpdate.Quantity.HasValue && itemUpdate.Quantity.Value > 0)
            {
                cartItem.Quantity = itemUpdate.Quantity.Value;
                cartItem.Price = CalculateItemPrice(cartItem.Product, cartItem.Quantity);

                // Update cart total
                var cart = await _context.Carts
                    .Include(c => c.Items)
                    .ThenInclude(i => i.Product)
                    .FirstOrDefaultAsync(c => c.Id == cartId);

                CalculateCartTotal(cart);
                
                await _context.SaveChangesAsync();
            }

            return Ok(cartItem);
        }

        /// <summary>
        /// Updates specific details of an existing item in a shopping cart.
        /// </summary>
        /// <param name="cartId">The ID of the cart to update the item in.</param>
        /// <param name="itemId">The ID of the item to update.</param>
        /// <param name="itemUpdate">The new details of the item.</param>
        /// <returns>The updated cart item.</returns>
        /// <response code="200">Returns the updated cart item.</response>
        /// <response code="400">If the item update request is invalid.</response>
        /// <response code="401">If the user is not authenticated.</response>
        /// <response code="403">If the user does not have the required permissions.</response>
        /// <response code="404">If the cart item with the specified ID is not found.</response>
        // PATCH: api/Carts/{cartId}/items/{itemId}
        [HttpPatch("{cartId}/items/{itemId}")]
        [RequiredScopeOrAppPermission(
            AcceptedScope = new[] { "Carts.Write" })]
        public async Task<IActionResult> UpdateCartItem(string cartId, string itemId, CartItemUpdate itemUpdate)
        {
            var cartItem = await _context.CartItems
                .Include(i => i.Product)
                .FirstOrDefaultAsync(i => i.CartId == cartId && i.ItemId == itemId);

            if (cartItem == null)
            {
                return NotFound();
            }

            if (itemUpdate.Quantity.HasValue && itemUpdate.Quantity.Value > 0)
            {
                cartItem.Quantity = itemUpdate.Quantity.Value;
                cartItem.Price = CalculateItemPrice(cartItem.Product, cartItem.Quantity);

                // Update cart total
                var cart = await _context.Carts
                    .Include(c => c.Items)
                    .ThenInclude(i => i.Product)
                    .FirstOrDefaultAsync(c => c.Id == cartId);

                CalculateCartTotal(cart);
                
                await _context.SaveChangesAsync();
            }

            return Ok(cartItem);
        }

        /// <summary>
        /// Deletes a specific item from a shopping cart by ID.
        /// </summary>
        /// <param name="cartId">The ID of the cart to delete the item from.</param>
        /// <param name="itemId">The ID of the item to delete.</param>
        /// <returns>No content.</returns>
        /// <response code="204">If the item was successfully deleted.</response>
        /// <response code="401">If the user is not authenticated.</response>
        /// <response code="403">If the user does not have the required permissions.</response>
        /// <response code="404">If the cart item with the specified ID is not found.</response>
        // DELETE: api/Carts/{cartId}/items/{itemId}]
        [HttpDelete("{cartId}/items/{itemId}")]
        [RequiredScopeOrAppPermission(
            AcceptedScope = new[] { "Carts.Write" })]
        public async Task<IActionResult> DeleteCartItem(string cartId, string itemId)
        {
            var cartItem = await _context.CartItems
                .FirstOrDefaultAsync(i => i.CartId == cartId && i.ItemId == itemId);

            if (cartItem == null)
            {
                return NotFound();
            }

            _context.CartItems.Remove(cartItem);

            // Update cart total
            var cart = await _context.Carts
                .Include(c => c.Items)
                .ThenInclude(i => i.Product)
                .FirstOrDefaultAsync(c => c.Id == cartId);

            CalculateCartTotal(cart);

            await _context.SaveChangesAsync();

            return NoContent();
        }

        // Helper methods
        private bool CartExists(string id)
        {
            return _context.Carts.Any(c => c.Id == id);
        }

        private decimal CalculateItemPrice(Product product, int quantity)
        {
            if (product == null)
                return 0;

            decimal priceAfterDiscount = product.Price * (1 - product.DiscountPercentage / 100);
            return priceAfterDiscount * quantity;
        }

        private void CalculateCartTotal(Cart cart)
        {
            if (cart == null)
                return;

            cart.TotalPrice = cart.Items.Sum(i => i.Price);
        }
    }
}