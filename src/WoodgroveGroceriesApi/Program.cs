// Copyright (c) Microsoft Corporation. 
// Licensed under the MIT license.

using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.OData;
using Microsoft.EntityFrameworkCore;
using Microsoft.Identity.Web;
using Microsoft.OpenApi.Models;
using Microsoft.IdentityModel.Tokens;
using WoodgroveGroceriesApi.Data;
using WoodgroveGroceriesApi.Middleware;
using System.Text.Json;
using System.Diagnostics;

var builder = WebApplication.CreateBuilder(args);

// Service Configuration
// --------------------

// Add Application Insights only in non-development environment
if (!builder.Environment.IsDevelopment())
{
    // Add Application Insights in production
    builder.Services.AddApplicationInsightsTelemetry(options =>
    {
        options.ConnectionString = builder.Configuration["ApplicationInsights:ConnectionString"];
    });

    // Configure TelemetryInitializer to enrich data
    builder.Services.Configure<TelemetryConfiguration>(config =>
    {
        config.TelemetryInitializers.Add(new TelemetryInitializer());
    });
}

// Register the authentication health monitor
builder.Services.AddSingleton<AuthenticationHealthMonitor>();

// Register the custom authorization filter
builder.Services.AddScoped<AllowAnonymousInDevelopmentAttribute>();

// Register the authorization filter for scope auditing
builder.Services.AddScoped<ScopeAuthorizationFilter>();

// Register the MFA service
builder.Services.AddScoped<WoodgroveGroceriesApi.Services.IMfaService, WoodgroveGroceriesApi.Services.SimpleMfaService>();

// Add HttpContextAccessor
builder.Services.AddHttpContextAccessor();

// Load API scopes from configuration
var apiScopes = new Dictionary<string, string>();
var scopesSection = builder.Configuration.GetSection("ApiScopes");
if (scopesSection.Exists())
{
    foreach (var scopeConfig in scopesSection.GetChildren())
    {
        var scopeId = scopeConfig.GetValue<string>("ScopeId");
        var description = scopeConfig.GetValue<string>("Description");

        if (!string.IsNullOrEmpty(scopeId) && !string.IsNullOrEmpty(description))
        {
            apiScopes.Add(scopeId, description);
        }
    }
}


// Add authentication with Microsoft Identity (MSAL)
if (!builder.Environment.IsDevelopment())
{
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            // Configure with ValidationAuthority to download signing keys from configuration
            options.Authority = builder.Configuration["Entra:ValidationAuthority"];
            options.TokenValidationParameters = new TokenValidationParameters
            {
                // Only validate essential aspects: token, issuer, and audience
                ValidateIssuer = true,
                ValidIssuer = builder.Configuration["Entra:ValidIssuer"],
                ValidateAudience = true,
                ValidAudience = builder.Configuration["Entra:ValidAudience"],
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                // Do not set specific properties for name or role
            };

            // Events for logging authentication success/failure with timing
            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    // Start stopwatch to measure authentication time
                    var stopwatch = new System.Diagnostics.Stopwatch();
                    stopwatch.Start();
                    context.HttpContext.Items["AuthTimer"] = stopwatch;
                    return Task.CompletedTask;
                },
                OnTokenValidated = async context =>
                {
                    // Measure the time taken to validate the token
                    var stopwatch = context.HttpContext.Items["AuthTimer"] as System.Diagnostics.Stopwatch;
                    var elapsedMs = stopwatch?.ElapsedMilliseconds ?? 0;

                    var authMonitor = context.HttpContext.RequestServices.GetService<AuthenticationHealthMonitor>();
                    if (authMonitor != null)
                    {
                        authMonitor.TrackTokenValidation(true, elapsedMs);
                    }

                    // Show specific claims in the debug console
                    var claimsPrincipal = context.Principal;
                    if (claimsPrincipal != null)
                    {
                        // Print all claims in the token for debugging
                        Debug.WriteLine("======= ALL TOKEN CLAIMS (PRODUCTION) =======");
                        foreach (var claim in claimsPrincipal.Claims)
                        {
                            Debug.WriteLine($"Claim Type: {claim.Type}, Value: {claim.Value}");
                        }
                        Debug.WriteLine("===============================");
                        
                        var dateOfBirth = claimsPrincipal.FindFirst("DateOfBirth")?.Value ?? "N/A";
                        var displayName = claimsPrincipal.FindFirst("name")?.Value ?? "N/A";
                        
                        // Get the ACR claim (which may come as an array)
                        var acrClaims = claimsPrincipal.FindAll("acrs").Select(c => c.Value).ToList();
                        if (!acrClaims.Any())
                        {
                            // Try alternative claim name "acr" that might be used
                            acrClaims = claimsPrincipal.FindAll("acr").Select(c => c.Value).ToList();
                            Debug.WriteLine("No 'acrs' claims found, trying 'acr' instead");
                        }
                        else
                        {
                            Debug.WriteLine($"Found {acrClaims.Count} 'acrs' claims");
                        }
                        var acrValue = acrClaims.Any() ? string.Join(", ", acrClaims) : "N/A";
                        
                        // Get the DietaryRestrictions claim
                        var dietaryRestrictions = claimsPrincipal.FindFirst("DietaryRestrictions")?.Value ?? "N/A";

                        Debug.WriteLine($"Token validated -> User Name: {displayName} -> DateOfBirth: {dateOfBirth} -> ACRS: {acrValue} -> DietaryRestrictions: {dietaryRestrictions}");
                    }
                },
                OnAuthenticationFailed = context =>
                {
                    // Measure the time taken until the failure is detected
                    var stopwatch = context.HttpContext.Items["AuthTimer"] as System.Diagnostics.Stopwatch;
                    var elapsedMs = stopwatch?.ElapsedMilliseconds ?? 0;

                    var authMonitor = context.HttpContext.RequestServices.GetService<AuthenticationHealthMonitor>();
                    if (authMonitor != null)
                    {
                        authMonitor.TrackTokenValidation(false, elapsedMs);
                    }

                    // Log validation error in the debug console
                    var errorMessage = "Unknown token validation error";

                    if (context.Exception is SecurityTokenExpiredException)
                    {
                        errorMessage = "Validation error: The token has expired";
                    }
                    else if (context.Exception is SecurityTokenInvalidAudienceException)
                    {
                        errorMessage = "Validation error: Invalid audience";
                    }
                    else if (context.Exception is SecurityTokenInvalidIssuerException)
                    {
                        errorMessage = "Validation error: Invalid issuer";
                    }
                    else if (context.Exception is SecurityTokenSignatureKeyNotFoundException)
                    {
                        errorMessage = "Validation error: Signature key not found";
                    }
                    else if (context.Exception is SecurityTokenValidationException)
                    {
                        errorMessage = $"Validation error: {context.Exception.Message}";
                    }

                    Debug.WriteLine(errorMessage);

                    return Task.CompletedTask;
                },
            };
        });
}
else
{
    // In development, set up basic authentication without real validation
    builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    // Start stopwatch to measure authentication time
                    var stopwatch = new System.Diagnostics.Stopwatch();
                    stopwatch.Start();
                    context.HttpContext.Items["AuthTimer"] = stopwatch;
                    return Task.CompletedTask;
                },
                OnTokenValidated = context =>
                {
                    // Measure the time taken to validate the token in development
                    var stopwatch = context.HttpContext.Items["AuthTimer"] as System.Diagnostics.Stopwatch;
                    var elapsedMs = stopwatch?.ElapsedMilliseconds ?? 0;

                    var authMonitor = context.HttpContext.RequestServices.GetService<AuthenticationHealthMonitor>();
                    if (authMonitor != null)
                    {
                        authMonitor.TrackTokenValidation(true, elapsedMs);
                    }

                    // Show specific claims in the debug console
                    var claimsPrincipal = context.Principal;
                    if (claimsPrincipal != null)
                    {
                        // Print all claims in the token for debugging
                        Debug.WriteLine("======= ALL TOKEN CLAIMS =======");
                        foreach (var claim in claimsPrincipal.Claims)
                        {
                            Debug.WriteLine($"Claim Type: {claim.Type}, Value: {claim.Value}");
                        }
                        Debug.WriteLine("===============================");
                        
                        var dateOfBirth = claimsPrincipal.FindFirst("DateOfBirth")?.Value ?? "N/A";
                        var displayName = claimsPrincipal.FindFirst("name")?.Value ?? "N/A";
                        
                        // Get the ACR claim (which may come as an array)
                        var acrClaims = claimsPrincipal.FindAll("acrs").Select(c => c.Value).ToList();
                        if (!acrClaims.Any())
                        {
                            // Try alternative claim name "acr" that might be used
                            acrClaims = claimsPrincipal.FindAll("acr").Select(c => c.Value).ToList();
                            Debug.WriteLine("No 'acrs' claims found, trying 'acr' instead");
                        }
                        else
                        {
                            Debug.WriteLine($"Found {acrClaims.Count} 'acrs' claims");
                        }
                        var acrValue = acrClaims.Any() ? string.Join(", ", acrClaims) : "N/A";

                        // Get the DietaryRestrictions claim
                        var dietaryRestrictions = claimsPrincipal.FindFirst("DietaryRestrictions")?.Value ?? "N/A";

                        Debug.WriteLine($"Token validated -> User Name: {displayName} -> DateOfBirth: {dateOfBirth} -> ACRS: {acrValue} -> DietaryRestrictions: {dietaryRestrictions}");
                    }

                    return Task.CompletedTask;
                },
                OnAuthenticationFailed = context =>
                {
                    // Measure the time taken until the failure is detected in development
                    var stopwatch = context.HttpContext.Items["AuthTimer"] as System.Diagnostics.Stopwatch;
                    var elapsedMs = stopwatch?.ElapsedMilliseconds ?? 0;

                    var authMonitor = context.HttpContext.RequestServices.GetService<AuthenticationHealthMonitor>();
                    if (authMonitor != null)
                    {
                        authMonitor.TrackTokenValidation(false, elapsedMs);
                    }

                    // Log validation error in the debug console
                    var errorMessage = "Unknown token validation error";

                    if (context.Exception is SecurityTokenExpiredException)
                    {
                        errorMessage = "Validation error: The token has expired";
                    }
                    else if (context.Exception is SecurityTokenInvalidAudienceException)
                    {
                        errorMessage = "Validation error: Invalid audience";
                    }
                    else if (context.Exception is SecurityTokenInvalidIssuerException)
                    {
                        errorMessage = "Validation error: Invalid issuer";
                    }
                    else if (context.Exception is SecurityTokenSignatureKeyNotFoundException)
                    {
                        errorMessage = "Validation error: Signature key not found";
                    }
                    else if (context.Exception is SecurityTokenValidationException)
                    {
                        errorMessage = $"Validation error: {context.Exception.Message}";
                    }

                    Debug.WriteLine(errorMessage);

                    return Task.CompletedTask;
                },
                OnChallenge = context =>
                {
                    // In development, don't require a token
                    if (builder.Environment.IsDevelopment())
                    {
                        context.HandleResponse();
                    }
                    return Task.CompletedTask;
                }
            };
        });
}

// Configure DbContext with Entity Framework Core In-Memory
builder.Services.AddDbContext<WoodgroveGroceriesContext>(options =>
    options.UseInMemoryDatabase("WoodgroveGroceriesDb"));

// Configure OData
builder.Services.AddControllers(options =>
{
    // Add scope authorization filter to all requests
    options.Filters.Add<ScopeAuthorizationFilter>();
})
.AddJsonOptions(options =>
{
    // Handle circular references during JSON serialization
    options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.Preserve;
    options.JsonSerializerOptions.WriteIndented = true;
})
.AddOData(options => options
    .Select()
    .Filter()
    .Count()
    .OrderBy()
    .Expand()
    .SetMaxTop(100)
    .AddRouteComponents("odata", WoodgroveGroceriesContext.GetEdmModel()));

// Configure Swagger/OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Woodgrove Groceries API",
        Description = "API for managing products, carts, and checkout/payment operations.",
        Version = "v1"
    });

    if (!builder.Environment.IsDevelopment())
    {
        // Configure authentication in Swagger for non-development environments
        c.AddSecurityDefinition("oauth2", new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.OAuth2,
            Flows = new OpenApiOAuthFlows
            {
                Implicit = new OpenApiOAuthFlow
                {
                    AuthorizationUrl = new Uri(builder.Configuration["Entra:AuthorizationUrl"]),
                    TokenUrl = new Uri(builder.Configuration["Entra:TokenUrl"]),
                    Scopes = apiScopes
                }
            }
        });

        c.AddSecurityRequirement(new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "oauth2" }
                },
                apiScopes.Keys.ToList()
            }
        });
    }
});

var app = builder.Build();

// HTTP Request Pipeline Configuration
// ----------------------------------

// Initialize database with seed data
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var context = services.GetRequiredService<WoodgroveGroceriesContext>();

    // Force the initialization of the database and seed data
    context.Database.EnsureCreated();

    // Verify that products were seeded
    if (!context.Products.Any())
    {
        app.Logger.LogWarning("No products found in the database after initialization.");
    }
    else
    {
        app.Logger.LogInformation($"Database initialized with {context.Products.Count()} products.");
    }
}

// Configure the HTTP request pipeline
// Enable Swagger and SwaggerUI in all environments
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Woodgrove Groceries API v1");
});

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();

// Add the audit middleware before authentication to log both
// authenticated and unauthenticated requests
app.UseAuditLogging();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();

// TelemetryInitializer implementation to enrich AppInsights telemetry
public class TelemetryInitializer : ITelemetryInitializer
{
    public void Initialize(Microsoft.ApplicationInsights.Channel.ITelemetry telemetry)
    {
        telemetry.Context.Cloud.RoleName = "WoodgroveGroceriesApi";
        telemetry.Context.Component.Version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "1.0.0";

        // Add environment info
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
        telemetry.Context.GlobalProperties["Environment"] = environment;
    }
}
