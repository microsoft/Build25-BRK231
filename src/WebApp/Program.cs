// Copyright (c) Microsoft Corporation. 
// Licensed under the MIT license.

using Microsoft.Identity.Web;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Identity.Web.UI;
using System.Globalization;

// Create and configure the web application builder
var builder = WebApplication.CreateBuilder(args);

// Configure authentication with Microsoft Identity Platform
builder.Services.AddAuthentication(OpenIdConnectDefaults.AuthenticationScheme)
    .AddMicrosoftIdentityWebApp(options => {
        // Bind configuration from the EntraID section
        builder.Configuration.GetSection("EntraID").Bind(options);
        
        // Configure event to handle redirection and add StepUp parameters
        options.Events.OnRedirectToIdentityProvider = async context =>
        {
            // Check if this is a step-up authentication request
            bool needsStepUp = false;
            if (context.Properties.Items.TryGetValue("StepUp", out var stepUp) && !string.IsNullOrEmpty(stepUp))
            {
                needsStepUp = true;
            }
            
            // Also check if the request path contains /stepup
            string? path = context.Request.Path.Value?.ToLower();
            if (path != null && path.Contains("/stepup"))
            {
                needsStepUp = true;
                context.ProtocolMessage.Prompt = "login";
            }
            
            // If step-up is needed, add the claims parameter for MFA (c1)
            if (needsStepUp)
            {
                context.ProtocolMessage.Parameters.Add("claims", 
                    "%7B%22access_token%22%3A%7B%22acrs%22%3A%7B%22essential%22%3Atrue%2C%22value%22%3A%22c1%22%7D%7D%7D");
            }

            // Add login_hint if available
            if (context.Properties.Items.TryGetValue("login_hint", out var loginHint) && !string.IsNullOrEmpty(loginHint))
            {
                context.ProtocolMessage.LoginHint = loginHint;
            }

            // Add scope if available in properties
            if (context.Properties.Items.TryGetValue("scope", out var scope) && !string.IsNullOrEmpty(scope))
            {
                context.ProtocolMessage.Scope = scope;
            }            

            await Task.CompletedTask;
        };
        
        // Save tokens to make them available after authentication
        options.SaveTokens = true;
    })
    .EnableTokenAcquisitionToCallDownstreamApi() // Enables token acquisition
    .AddDownstreamApi("ChatApi", builder.Configuration.GetSection("ChatApi")) // Configures downstream API
    .AddInMemoryTokenCaches(); // Configures token caching in memory

// Add controllers with Microsoft Identity UI
builder.Services.AddControllersWithViews()
    .AddMicrosoftIdentityUI();

// Add Razor Pages support
builder.Services.AddRazorPages();

// Configure localization settings
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    var supportedCultures = new[] { new CultureInfo("en-US") };
    options.DefaultRequestCulture = new Microsoft.AspNetCore.Localization.RequestCulture("en-US");
    options.SupportedCultures = supportedCultures;
    options.SupportedUICultures = supportedCultures;
    // Disable culture detection from browser
    options.RequestCultureProviders.Clear();
});

// Build the application
var app = builder.Build();

// Configure the HTTP request pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

// Use HTTPS redirection, static files, and routing
app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

// Enable authentication and authorization
app.UseAuthentication();
app.UseAuthorization();

// Map endpoints
app.MapRazorPages();
app.MapControllers();

// Run the application
app.Run();
