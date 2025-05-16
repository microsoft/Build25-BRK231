// Copyright (c) Microsoft Corporation. 
// Licensed under the MIT license.

using Microsoft.AspNetCore.Authentication.JwtBearer;
using MyOpenAIWebApi.Options;
using MyOpenAIWebApi.Services;
using MyOpenAIWebApi.Helpers;
using MyOpenAIWebApi.Hubs;
using Microsoft.IdentityModel.Tokens;

/// <summary>
/// Application entry point and configuration for the OpenAI Web API.
/// This file sets up the ASP.NET Core application, configures services,
/// and establishes the middleware pipeline.
/// </summary>

// Create the web application builder
var builder = WebApplication.CreateBuilder(args);

// Configure application settings from appsettings.json files
builder.Services.Configure<OpenAIOptions>(builder.Configuration.GetSection("AzureOpenAI"));
builder.Services.Configure<CustomAPIOptions>(builder.Configuration.GetSection("CustomAPI"));
builder.Services.Configure<RAGOptions>(builder.Configuration.GetSection("RAG"));

// Add controllers for handling API endpoints
builder.Services.AddControllers();


/// <summary>
/// Configure JWT Authentication using Microsoft Entra ID (formerly Azure Active Directory)
/// This sets up the JWT Bearer authentication scheme for the API.
/// </summary>
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options => 
    {
        // Configure authority for token validation and key discovery
        options.Authority = builder.Configuration["Entra:ValidationAuthority"];
        
        // Configure token validation parameters
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Entra:ValidIssuer"],
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Entra:ValidAudience"],
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            // Security keys are downloaded automatically from the authority
        };
        
        options.SaveToken = true;          // Save the token for later retrieval
        options.RequireHttpsMetadata = true; // Require HTTPS for metadata endpoints
        
        // Configure event handlers for token processing
        options.Events = new JwtBearerEvents
        {
            // Event handler for extracting the token from the request
            OnMessageReceived = context =>
            {
                // First try to get token from Authorization header (for HTTP and WebSocket negotiation)
                string? token = context.Request.Headers["Authorization"].FirstOrDefault()?.Split(" ").Last();
                  // If token not found in header, try query string (as fallback for WebSockets)
                if (string.IsNullOrEmpty(token))
                {
                    token = context.Request.Query["access_token"];
                }
                
                // Check if the request is for the SignalR hub and set the token
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(token) && path.StartsWithSegments("/assistantHub"))
                {
                    // Store token in context for SignalR authentication
                    context.Token = token;
                }
                
                return Task.CompletedTask;
            }
        };    
    });

/// <summary>
/// Configure SignalR for real-time communication between clients and server.
/// SignalR enables bi-directional communication for streaming assistant responses.
/// </summary>
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = true;               // Enable detailed errors for debugging
    options.MaximumReceiveMessageSize = 102400;        // Set maximum message size to 100 KB
});

/// <summary>
/// Configure Cross-Origin Resource Sharing (CORS) to allow browser-based clients
/// from different origins to interact with this API.
/// </summary>
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {        // Get allowed origins from configuration or use default localhost value
        var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() 
                            ?? new[] { "http://localhost:5118" }; // Fallback default
        
        // Configure CORS policy with required options
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()            // Allow all HTTP headers
              .AllowAnyMethod()            // Allow all HTTP methods (GET, POST, etc.)
              .AllowCredentials()          // Allow credentials (required for SignalR)
              .SetIsOriginAllowed(_ => true) // For development - set more restrictive in production
              .WithExposedHeaders("Content-Disposition"); // Expose specific headers to the client
    });
});

/// <summary>
/// Register services required for the application in the dependency injection container.
/// </summary>

// Register TokenHelpers for authenticating with Microsoft Entra ID
builder.Services.AddSingleton<TokenHelpers>(sp =>
{
    // Get Microsoft Entra ID configuration from app settings
    var config = sp.GetRequiredService<IConfiguration>();
    var tenantId = config["Entra:TenantId"] ?? "";
    var clientId = config["Entra:ClientId"] ?? "";
    var clientSecret = config["Entra:ClientSecret"] ?? "";
    var authority = config["Entra:Authority"] ?? "";
    
    return new TokenHelpers(tenantId, clientId, clientSecret, authority);
});

// Register application services in the dependency injection container
builder.Services.AddSingleton<CustomAPIHelper>();      // Helper for custom API operations
builder.Services.AddMemoryCache();                     // In-memory cache for application data
builder.Services.AddSingleton<IAssistantManager, InMemoryAssistantManager>(); // Assistant management service

/// <summary>
/// Configure Swagger for API documentation and exploration
/// </summary>
builder.Services.AddEndpointsApiExplorer();  // API explorer for endpoint discovery
builder.Services.AddSwaggerGen();            // Swagger generator for API documentation

// Build the web application
var app = builder.Build();

/// <summary>
/// Configure the HTTP request pipeline with middleware components
/// </summary>

/// <summary>
/// Configure the HTTP request pipeline with middleware components
/// </summary>

// Enable Swagger UI only in development environment
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();          // Enable Swagger endpoint for OpenAPI specification
    app.UseSwaggerUI();        // Enable Swagger UI for interactive API documentation
}

// Configure the HTTP request processing pipeline in the correct order
app.UseCors();                // Apply CORS policies first
app.UseRouting();             // Set up routing for endpoint matching
app.UseAuthentication();      // Authenticate users based on credentials
app.UseAuthorization();       // Authorize users based on claims/roles
app.UseHttpsRedirection();    // Redirect HTTP requests to HTTPS

// Map endpoints to controllers and SignalR hubs
app.MapControllers();                         // Map controller actions to routes
app.MapHub<AssistantHub>("/assistantHub");    // Map SignalR hub to its endpoint

// Start the application
app.Run();
