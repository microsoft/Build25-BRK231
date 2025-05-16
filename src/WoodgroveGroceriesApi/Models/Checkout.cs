// Copyright (c) Microsoft Corporation. 
// Licensed under the MIT license.

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace WoodgroveGroceriesApi.Models
{
    /// <summary>
    /// Represents a request to check out a shopping cart.
    /// </summary>
    public class CheckoutRequest
    {
        /// <summary>
        /// Gets or sets the identifier of the cart to check out.
        /// </summary>
        [Required]
        public string CartId { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets the delivery address for the order.
        /// </summary>
        public string? Address { get; set; }
    }
    
    /// <summary>
    /// Represents a response to a checkout request.
    /// </summary>
    public class CheckoutResponse
    {
        /// <summary>
        /// Gets or sets the identifier of the created order.
        /// </summary>
        public string OrderId { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets the identifier of the cart that was checked out.
        /// </summary>
        public string CartId { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets the total amount of the order.
        /// </summary>
        [Column(TypeName = "decimal(18,2)")]
        public decimal TotalAmount { get; set; }
        
        /// <summary>
        /// Gets or sets the currency of the total amount. Defaults to USD.
        /// </summary>
        public string Currency { get; set; } = "USD";
        
        /// <summary>
        /// Gets or sets the status of the checkout process.
        /// </summary>
        public string Status { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets a value indicating whether multi-factor authentication is required.
        /// Note: This property is maintained for backward compatibility but is always false.
        /// </summary>
        // Mantenemos esta propiedad pero siempre será false
        public bool RequiresMfa { get; set; } = false;
    }
    
    /// <summary>
    /// Represents an order in the system.
    /// </summary>
    public class Order
    {
        /// <summary>
        /// Gets or sets the unique identifier for the order.
        /// </summary>
        public string Id { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets the identifier of the cart associated with this order.
        /// </summary>
        public string CartId { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets the total amount of the order.
        /// </summary>
        [Column(TypeName = "decimal(18,2)")]
        public decimal TotalAmount { get; set; }
        
        /// <summary>
        /// Gets or sets the currency of the total amount. Defaults to USD.
        /// </summary>
        public string Currency { get; set; } = "USD";
        
        /// <summary>
        /// Gets or sets the status of the order.
        /// </summary>
        public string Status { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets the delivery address for the order.
        /// </summary>
        public string? Address { get; set; }
        
        /// <summary>
        /// Gets or sets the date and time when the order was created. Defaults to the current UTC time.
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        /// <summary>
        /// Gets or sets the identifier of the payment transaction associated with this order.
        /// </summary>
        public string? PaymentTransactionId { get; set; }
        
        /// <summary>
        /// Gets or sets a value indicating whether the order contains alcoholic products.
        /// </summary>
        public bool ContainsAlcohol { get; set; } = false;
        
        /// <summary>
        /// Gets or sets the cart associated with this order.
        /// </summary>
        // Navigation property
        [ForeignKey("CartId")]
        public virtual Cart? Cart { get; set; }
    }
    
    /// <summary>
    /// Represents a request to process a payment.
    /// </summary>
    public class PaymentRequest
    {
        /// <summary>
        /// Gets or sets the payment method to use (e.g., "CreditCard", "PayPal").
        /// </summary>
        [Required]
        public string PaymentMethod { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets the credit card number for card payments.
        /// </summary>
        public string? CardNumber { get; set; }
        
        /// <summary>
        /// Gets or sets the expiration date for card payments in MM/YY format.
        /// </summary>
        public string? ExpirationDate { get; set; }
        
        /// <summary>
        /// Gets or sets the CVV security code for card payments.
        /// </summary>
        public string? Cvv { get; set; }
    }
    
    /// <summary>
    /// Represents a response to a payment processing request.
    /// </summary>
    public class PaymentResponse
    {
        /// <summary>
        /// Gets or sets the identifier of the order associated with this payment.
        /// </summary>
        public string OrderId { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets the status of the payment (e.g., "Successful", "Failed", "Pending").
        /// </summary>
        public string PaymentStatus { get; set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets the identifier of the payment transaction.
        /// </summary>
        public string? TransactionId { get; set; }
        
        /// <summary>
        /// Gets or sets a value indicating whether multi-factor authentication is required.
        /// Note: This property is maintained for backward compatibility but is always false.
        /// </summary>
        // Mantenemos esta propiedad pero siempre será false
        public bool RequiresMfa { get; set; } = false;
    }
}