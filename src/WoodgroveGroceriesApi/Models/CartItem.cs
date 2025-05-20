// Copyright (c) Microsoft Corporation. 
// Licensed under the MIT license.

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WoodgroveGroceriesApi.Models
{
    /// <summary>
    /// Represents an item in a shopping cart.
    /// </summary>
    public class CartItem
    {
        /// <summary>
        /// Gets or sets the unique identifier for the cart item.
        /// </summary>
        [Key]
        public string ItemId { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets the identifier of the product associated with this cart item.
        /// </summary>
        public string ProductId { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets the quantity of the product in the cart.
        /// </summary>
        [Required]
        public int Quantity { get; set; }
        
        /// <summary>
        /// Gets or sets the price of the cart item (unit price × quantity).
        /// </summary>
        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal Price { get; set; }
        
        /// <summary>
        /// Gets or sets the identifier of the cart this item belongs to.
        /// </summary>
        public string CartId { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets the cart this item belongs to.
        /// </summary>
        // Navigation properties
        [ForeignKey("CartId")]
        public virtual Cart? Cart { get; set; }
        
        /// <summary>
        /// Gets or sets the product associated with this cart item.
        /// </summary>
        [ForeignKey("ProductId")]
        public virtual Product? Product { get; set; }
    }

    /// <summary>
    /// Data transfer object for creating a new cart item.
    /// </summary>
    public class CartItemCreate
    {
        /// <summary>
        /// Gets or sets the identifier of the product to add to the cart.
        /// </summary>
        [Required]
        public string ProductId { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets the quantity of the product to add to the cart.
        /// </summary>
        [Required]
        public int Quantity { get; set; }
    }

    /// <summary>
    /// Data transfer object for updating an existing cart item.
    /// </summary>
    public class CartItemUpdate
    {
        /// <summary>
        /// Gets or sets the updated quantity of the product in the cart.
        /// </summary>
        public int? Quantity { get; set; }
    }
}