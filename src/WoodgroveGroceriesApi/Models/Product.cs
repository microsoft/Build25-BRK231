// Copyright (c) Microsoft Corporation. 
// Licensed under the MIT license.

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WoodgroveGroceriesApi.Models
{
    /// <summary>
    /// Represents a product in the Woodgrove Groceries inventory.
    /// </summary>
    public class Product
    {
        /// <summary>
        /// Gets or sets the unique identifier for the product.
        /// </summary>
        [Key]
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the name of the product.
        /// </summary>
        [Required]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the description of the product.
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Gets or sets the price of the product.
        /// </summary>
        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal Price { get; set; }

        /// <summary>
        /// Gets or sets the discount percentage applied to the product.
        /// </summary>
        public int DiscountPercentage { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the product is vegan.
        /// </summary>
        public bool IsVegan { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the product is gluten-free.
        /// </summary>
        public bool IsGlutenFree { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the product contains alcohol.
        /// </summary>
        public bool IsAlcoholic { get; set; }

        /// <summary>
        /// Gets or sets the category of the product.
        /// </summary>
        public string? Category { get; set; }

        /// <summary>
        /// Gets or sets the collection of cart items that include this product.
        /// </summary>
        // Navigation property
        public virtual ICollection<CartItem>? CartItems { get; set; }
    }

    /// <summary>
    /// Data transfer object for creating a new product.
    /// </summary>
    public class ProductCreate
    {
        /// <summary>
        /// Gets or sets the name of the product.
        /// </summary>
        [Required]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the description of the product.
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Gets or sets the price of the product.
        /// </summary>
        [Required]
        public decimal Price { get; set; }

        /// <summary>
        /// Gets or sets the discount percentage applied to the product.
        /// </summary>
        public int DiscountPercentage { get; set; } = 0;

        /// <summary>
        /// Gets or sets a value indicating whether the product is vegan.
        /// </summary>
        public bool IsVegan { get; set; } = false;

        /// <summary>
        /// Gets or sets a value indicating whether the product is gluten-free.
        /// </summary>
        public bool IsGlutenFree { get; set; } = false;

        /// <summary>
        /// Gets or sets a value indicating whether the product contains alcohol.
        /// </summary>
        public bool IsAlcoholic { get; set; } = false;

        /// <summary>
        /// Gets or sets the category of the product.
        /// </summary>
        public string? Category { get; set; }
    }

    /// <summary>
    /// Data transfer object for updating an existing product.
    /// </summary>
    public class ProductUpdate
    {
        /// <summary>
        /// Gets or sets the name of the product.
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// Gets or sets the description of the product.
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Gets or sets the price of the product.
        /// </summary>
        public decimal? Price { get; set; }

        /// <summary>
        /// Gets or sets the discount percentage applied to the product.
        /// </summary>
        public int? DiscountPercentage { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the product is vegan.
        /// </summary>
        public bool? IsVegan { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the product is gluten-free.
        /// </summary>
        public bool? IsGlutenFree { get; set; }
        
        /// <summary>
        /// Gets or sets a value indicating whether the product contains alcohol.
        /// </summary>
        public bool? IsAlcoholic { get; set; }

        /// <summary>
        /// Gets or sets the category of the product.
        /// </summary>
        public string? Category { get; set; }
    }
}