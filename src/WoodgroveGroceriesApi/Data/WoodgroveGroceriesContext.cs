// Copyright (c) Microsoft Corporation. 
// Licensed under the MIT license.

using Microsoft.EntityFrameworkCore;
using Microsoft.OData.ModelBuilder;
using Microsoft.OData.Edm;
using WoodgroveGroceriesApi.Models;

namespace WoodgroveGroceriesApi.Data
{
    public class WoodgroveGroceriesContext : DbContext
    {
        public WoodgroveGroceriesContext(DbContextOptions<WoodgroveGroceriesContext> options)
            : base(options)
        {
        }

        public DbSet<Product> Products { get; set; }
        public DbSet<Cart> Carts { get; set; }
        public DbSet<CartItem> CartItems { get; set; }
        public DbSet<Order> Orders { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure relationships
            modelBuilder.Entity<CartItem>()
                .HasOne(ci => ci.Cart)
                .WithMany(c => c.Items)
                .HasForeignKey(ci => ci.CartId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<CartItem>()
                .HasOne(ci => ci.Product)
                .WithMany(p => p.CartItems)
                .HasForeignKey(ci => ci.ProductId);

            modelBuilder.Entity<Order>()
                .HasOne(o => o.Cart)
                .WithMany()
                .HasForeignKey(o => o.CartId);

            // Seed data
            SeedData(modelBuilder);
        }

        private void SeedData(ModelBuilder modelBuilder)
        {
            // Create a list of grocery products
            var products = new List<Product>();
            
            // Categories
            string[] categories = new[] { 
                "Fruits & Vegetables", 
                "Bakery", 
                "Dairy & Eggs", 
                "Meat & Seafood"
            };
            
            // Fruits & Vegetables
            products.Add(new Product { Id = "p001", Name = "Organic Bananas", Description = "Bunch of organic bananas", Price = 1.99M, DiscountPercentage = 0, IsVegan = true, IsGlutenFree = true, IsAlcoholic = false, Category = "Fruits & Vegetables" });
            products.Add(new Product { Id = "p002", Name = "Organic Apples", Description = "Bag of organic apples", Price = 4.99M, DiscountPercentage = 10, IsVegan = true, IsGlutenFree = true, IsAlcoholic = false, Category = "Fruits & Vegetables" });
            products.Add(new Product { Id = "p003", Name = "Organic Avocados", Description = "Ripe organic avocados", Price = 2.50M, DiscountPercentage = 0, IsVegan = true, IsGlutenFree = true, IsAlcoholic = false, Category = "Fruits & Vegetables" });

            //Comment to the user:
            //Complete list of categories and products were removed 
            //Please add more items for the demo to work correctly

            // Seed the products
            modelBuilder.Entity<Product>().HasData(products);

            // Seed a sample cart
            modelBuilder.Entity<Cart>().HasData(
                new Cart { Id = "c1", UserId = "user1", TotalPrice = 0 }
            );
        }

        // OData model configuration for enabling advanced querying
        public static IEdmModel GetEdmModel()
        {
            var builder = new ODataConventionModelBuilder();
            
            // Configure entity sets for OData
            builder.EntitySet<Product>("Products");
            builder.EntitySet<Cart>("Carts");
            builder.EntitySet<CartItem>("CartItems");
            
            return builder.GetEdmModel();
        }
    }
}