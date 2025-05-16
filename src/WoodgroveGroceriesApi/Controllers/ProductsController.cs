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
    /// Controller for managing products in the Woodgrove Groceries API.
    /// </summary>
    [TypeFilter(typeof(AllowAnonymousInDevelopmentAttribute))]
    [ApiController]
    [Route("api/[controller]")]
    public class ProductsController : ODataController
    {
        private readonly WoodgroveGroceriesContext _context;
        private readonly ILogger<ProductsController> _logger;
        private readonly IConfiguration _configuration;

        /// <summary>
        /// Initializes a new instance of the <see cref="ProductsController"/> class.
        /// </summary>
        /// <param name="context">The database context.</param>
        /// <param name="logger">The logger instance.</param>
        /// <param name="configuration">The configuration instance.</param>
        public ProductsController(WoodgroveGroceriesContext context, ILogger<ProductsController> logger, IConfiguration configuration)
        {
            _context = context;
            _logger = logger;
            _configuration = configuration;
        }

        /// <summary>
        /// Gets all products.
        /// </summary>
        /// <returns>A collection of products.</returns>
        /// <response code="200">Returns the list of products.</response>
        /// <response code="401">If the user is not authenticated.</response>
        /// <response code="403">If the user does not have the required permissions.</response>
        // GET: api/Products
        [HttpGet]
        [EnableQuery]
        [RequiredScopeOrAppPermission(
            AcceptedScope = new[] { "Products.Read" })]
        public IActionResult GetProducts()
        {
            return Ok(_context.Products);
        }

        /// <summary>
        /// Gets a specific product by ID.
        /// </summary>
        /// <param name="productId">The ID of the product to retrieve.</param>
        /// <returns>The requested product.</returns>
        /// <response code="200">Returns the requested product.</response>
        /// <response code="401">If the user is not authenticated.</response>
        /// <response code="403">If the user does not have the required permissions.</response>
        /// <response code="404">If the product with the specified ID is not found.</response>
        // GET: api/Products/{id}
        [HttpGet("{productId}")]
        [EnableQuery]
        [RequiredScopeOrAppPermission(
            AcceptedScope = new[] { "Products.Read" })]
        public async Task<IActionResult> GetProduct(string productId)
        {
            var product = await _context.Products.FindAsync(productId);

            if (product == null)
            {
                return NotFound();
            }

            return Ok(product);
        }

        /// <summary>
        /// Searches for products based on a query.
        /// </summary>
        /// <param name="query">The search query.</param>
        /// <returns>A collection of products that match the search query.</returns>
        /// <response code="200">Returns the list of matching products.</response>
        /// <response code="401">If the user is not authenticated.</response>
        /// <response code="403">If the user does not have the required permissions.</response>
        // GET: api/Products/search
        [HttpGet("search")]
        [RequiredScopeOrAppPermission(
            AcceptedScope = new[] { "Products.Read" })]
        public IActionResult SearchProducts([FromQuery] string query)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return Ok(_context.Products);
            }

            // Split the query into terms for multiple word searches
            var terms = query.Split(',', ' ')
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim().ToLowerInvariant())
                .ToList();

            // Use AsEnumerable() to move the filtering to client-side
            // to avoid issues with EF Core's translation limitations
            var results = _context.Products
                .AsEnumerable() // Switch to LINQ to Objects
                .Where(p => 
                    terms.Any(term =>
                        (p.Name != null && p.Name.ToLowerInvariant().Contains(term)) ||
                        (p.Description != null && p.Description.ToLowerInvariant().Contains(term)) ||
                        (p.Category != null && p.Category.ToLowerInvariant().Contains(term))
                    ))
                .ToList();
            
            _logger.LogInformation("Product search performed with query: {Query}, found {Count} results", query, results.Count);
            
            return Ok(results);
        }

        /// <summary>
        /// Creates a new product.
        /// </summary>
        /// <param name="productCreate">The product to create.</param>
        /// <returns>The created product.</returns>
        /// <response code="201">Returns the newly created product.</response>
        /// <response code="400">If the product is invalid.</response>
        /// <response code="401">If the user is not authenticated.</response>
        /// <response code="403">If the user does not have the required permissions.</response>
        // POST: api/Products
        [HttpPost]
        [RequiredScopeOrAppPermission(
            AcceptedScope = new[] { "Products.Write" })]
        [RequireAppRole] // Requires the ProductManager role from appsettings.json
        public async Task<ActionResult<Product>> CreateProduct(ProductCreate productCreate)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var product = new Product
            {
                Id = Guid.NewGuid().ToString(),
                Name = productCreate.Name,
                Description = productCreate.Description,
                Price = productCreate.Price,
                DiscountPercentage = productCreate.DiscountPercentage,
                IsVegan = productCreate.IsVegan,
                IsGlutenFree = productCreate.IsGlutenFree,
                IsAlcoholic = productCreate.IsAlcoholic,
                Category = productCreate.Category
            };

            _context.Products.Add(product);
            await _context.SaveChangesAsync();

            _logger.LogInformation("Product created: {ProductId}, {ProductName}", product.Id, product.Name);

            return CreatedAtAction(nameof(GetProduct), new { productId = product.Id }, product);
        }

        /// <summary>
        /// Replaces an existing product.
        /// </summary>
        /// <param name="productId">The ID of the product to replace.</param>
        /// <param name="productUpdate">The updated product information.</param>
        /// <returns>The updated product.</returns>
        /// <response code="200">Returns the updated product.</response>
        /// <response code="400">If the product is invalid.</response>
        /// <response code="401">If the user is not authenticated.</response>
        /// <response code="403">If the user does not have the required permissions.</response>
        /// <response code="404">If the product with the specified ID is not found.</response>
        // PUT: api/Products/{id}
        [HttpPut("{productId}")]
        [RequiredScopeOrAppPermission(
            AcceptedScope = new[] { "Products.Write" })]
        [RequireAppRole] // Requires the ProductManager role from appsettings.json
        public async Task<IActionResult> ReplaceProduct(string productId, ProductUpdate productUpdate)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var product = await _context.Products.FindAsync(productId);

            if (product == null)
            {
                return NotFound();
            }

            // Update all properties
            if (productUpdate.Name != null)
                product.Name = productUpdate.Name;
            if (productUpdate.Description != null)
                product.Description = productUpdate.Description;
            if (productUpdate.Price.HasValue)
                product.Price = productUpdate.Price.Value;
            if (productUpdate.DiscountPercentage.HasValue)
                product.DiscountPercentage = productUpdate.DiscountPercentage.Value;
            if (productUpdate.IsVegan.HasValue)
                product.IsVegan = productUpdate.IsVegan.Value;
            if (productUpdate.IsGlutenFree.HasValue)
                product.IsGlutenFree = productUpdate.IsGlutenFree.Value;
            if (productUpdate.IsAlcoholic.HasValue)
                product.IsAlcoholic = productUpdate.IsAlcoholic.Value;
            if (productUpdate.Category != null)
                product.Category = productUpdate.Category;

            try
            {
                await _context.SaveChangesAsync();
                _logger.LogInformation("Product updated: {ProductId}, {ProductName}", product.Id, product.Name);
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!ProductExists(productId))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return Ok(product);
        }
        
        /// <summary>
        /// Updates an existing product.
        /// </summary>
        /// <param name="productId">The ID of the product to update.</param>
        /// <param name="productUpdate">The updated product information.</param>
        /// <returns>The updated product.</returns>
        /// <response code="200">Returns the updated product.</response>
        /// <response code="400">If the product is invalid.</response>
        /// <response code="401">If the user is not authenticated.</response>
        /// <response code="403">If the user does not have the required permissions.</response>
        /// <response code="404">If the product with the specified ID is not found.</response>
        // PATCH: api/Products/{id}
        [HttpPatch("{productId}")]
        [RequiredScopeOrAppPermission(
            AcceptedScope = new[] { "Products.Write" })]
        [RequireAppRole] // Requires the ProductManager role from appsettings.json
        public async Task<IActionResult> UpdateProduct(string productId, ProductUpdate productUpdate)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var product = await _context.Products.FindAsync(productId);

            if (product == null)
            {
                return NotFound();
            }

            // Only update provided properties (same as PUT for our simplified model)
            if (productUpdate.Name != null)
                product.Name = productUpdate.Name;
            if (productUpdate.Description != null)
                product.Description = productUpdate.Description;
            if (productUpdate.Price.HasValue)
                product.Price = productUpdate.Price.Value;
            if (productUpdate.DiscountPercentage.HasValue)
                product.DiscountPercentage = productUpdate.DiscountPercentage.Value;
            if (productUpdate.IsVegan.HasValue)
                product.IsVegan = productUpdate.IsVegan.Value;
            if (productUpdate.IsGlutenFree.HasValue)
                product.IsGlutenFree = productUpdate.IsGlutenFree.Value;
            if (productUpdate.IsAlcoholic.HasValue)
                product.IsAlcoholic = productUpdate.IsAlcoholic.Value;
            if (productUpdate.Category != null)
                product.Category = productUpdate.Category;

            try
            {
                await _context.SaveChangesAsync();
                _logger.LogInformation("Product patch updated: {ProductId}, {ProductName}", product.Id, product.Name);
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!ProductExists(productId))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return Ok(product);
        }

        /// <summary>
        /// Deletes a specific product by ID.
        /// </summary>
        /// <param name="productId">The ID of the product to delete.</param>
        /// <returns>No content.</returns>
        /// <response code="204">If the product was successfully deleted.</response>
        /// <response code="401">If the user is not authenticated.</response>
        /// <response code="403">If the user does not have the required permissions.</response>
        /// <response code="404">If the product with the specified ID is not found.</response>
        // DELETE: api/Products/{id}
        [HttpDelete("{productId}")]
        [RequiredScopeOrAppPermission(
            AcceptedScope = new[] { "Products.Write" })]
        [RequireAppRole] // Requires the ProductManager role from appsettings.json
        public async Task<IActionResult> DeleteProduct(string productId)
        {
            var product = await _context.Products.FindAsync(productId);
            if (product == null)
            {
                return NotFound();
            }

            _context.Products.Remove(product);
            await _context.SaveChangesAsync();
            
            _logger.LogInformation("Product deleted: {ProductId}, {ProductName}", productId, product.Name);

            return NoContent();
        }

        private bool ProductExists(string id)
        {
            return _context.Products.Any(p => p.Id == id);
        }
    }
}