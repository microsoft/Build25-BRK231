// Copyright (c) Microsoft Corporation. 
// Licensed under the MIT license.

using System.ComponentModel.DataAnnotations.Schema;

namespace WoodgroveGroceriesApi.Models
{
    /// <summary>
    /// Represents a shopping cart in the Woodgrove Groceries system.
    /// </summary>
    public class Cart
    {
        /// <summary>
        /// Gets or sets the unique identifier for the cart.
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the identifier of the user who owns this cart.
        /// </summary>
        public string? UserId { get; set; }

        /// <summary>
        /// Gets or sets the collection of items in the cart.
        /// </summary>
        public virtual ICollection<CartItem> Items { get; set; } = new List<CartItem>();

        /// <summary>
        /// Gets or sets the total price of all items in the cart.
        /// </summary>
        [Column(TypeName = "decimal(18,2)")]
        public decimal TotalPrice { get; set; }
    }

    /// <summary>
    /// Data transfer object for creating a new cart.
    /// </summary>
    public class CartCreate
    {
        /// <summary>
        /// Gets or sets the identifier of the user who owns this cart.
        /// </summary>
        public string? UserId { get; set; }
    }

    /// <summary>
    /// Data transfer object for updating an existing cart.
    /// </summary>
    public class CartUpdate
    {
        /// <summary>
        /// Gets or sets the updated identifier of the user who owns this cart.
        /// </summary>
        public string? UserId { get; set; }
    }
}