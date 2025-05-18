// Copyright (c) Microsoft Corporation. 
// Licensed under the MIT license.

using Microsoft.ApplicationInsights;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Identity.Web.Resource;
using System.Security.Claims;

namespace WoodgroveGroceriesApi.Middleware
{
    /// <summary>
    /// Authorization filter that validates required OAuth scopes in the access token.
    /// </summary>
    public class ScopeAuthorizationFilter : IAuthorizationFilter
    {
        private readonly IWebHostEnvironment _env;
        private readonly TelemetryClient _telemetryClient;

        /// <summary>
        /// Initializes a new instance of the <see cref="ScopeAuthorizationFilter"/> class.
        /// </summary>
        /// <param name="env">The web hosting environment information.</param>
        /// <param name="telemetryClient">The Application Insights telemetry client.</param>
        public ScopeAuthorizationFilter(IWebHostEnvironment env, TelemetryClient telemetryClient)
        {
            _env = env;
            _telemetryClient = telemetryClient;
        }

        /// <summary>
        /// Called early in the filter pipeline to confirm authorization is allowed based on OAuth scopes.
        /// </summary>
        /// <param name="context">The authorization filter context.</param>
        public void OnAuthorization(AuthorizationFilterContext context)
        {
            // Find all RequiredScopeOrAppPermission attributes in the controller action
            var requiredScopeAttributes = context.ActionDescriptor.EndpointMetadata
                .OfType<RequiredScopeOrAppPermissionAttribute>().ToList();

            if (requiredScopeAttributes.Count == 0)
            {
                // No scope requirements found, allow access
                return;
            }

            // Combine all required scopes from all attributes
            var requiredScopes = requiredScopeAttributes
                .SelectMany(attr => attr.AcceptedScope ?? Array.Empty<string>())
                .ToArray();

            // Store in HttpContext to be used by AuditMiddleware for logging
            context.HttpContext.Items["RequiredScopes"] = requiredScopes;

            //TODO: REVIEW THIS
            // In development, bypass scope validation
            if (_env.IsDevelopment())
            {
                // Record metrics for development access
                _telemetryClient.TrackEvent("DevelopmentScopeBypass",
                    properties: new Dictionary<string, string> {
                        { "requiredScopes", string.Join(",", requiredScopes) },
                        { "path", context.HttpContext.Request.Path }
                    });

                return;
            }

            //TODO: REVIEW THIS
            // For production, validate scopes
            // Note: This filter doesn't enforce access control directly,
            // it just captures scope requirements for auditing
            // The actual enforcement is done by Microsoft.Identity.Web.Resource
        }
    }

    //TODO: REVIEW IF THIS IS NEEDED
    /// <summary>
    /// Attribute that allows anonymous access to API endpoints in development environment.
    /// In production, this enforces authentication requirements.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
    public class AllowAnonymousInDevelopmentAttribute : Attribute, IFilterFactory
    {
        /// <summary>
        /// Gets a value indicating whether the filter instance can be reused across requests.
        /// </summary>
        public bool IsReusable => false;

        /// <summary>
        /// Creates a new instance of the filter.
        /// </summary>
        /// <param name="serviceProvider">The service provider.</param>
        /// <returns>A new instance of the authorization filter.</returns>
        public IFilterMetadata CreateInstance(IServiceProvider serviceProvider)
        {
            var env = serviceProvider.GetRequiredService<IWebHostEnvironment>();
            var telemetryClient = serviceProvider.GetService<TelemetryClient>();
            return new AnonymousDevelopmentFilter(env, telemetryClient);
        }

        private class AnonymousDevelopmentFilter : IAuthorizationFilter
        {
            private readonly IWebHostEnvironment _env;
            private readonly TelemetryClient _telemetryClient;

            /// <summary>
            /// Initializes a new instance of the <see cref="AnonymousDevelopmentFilter"/> class.
            /// </summary>
            /// <param name="env">The web hosting environment information.</param>
            /// <param name="telemetryClient">The Application Insights telemetry client.</param>
            public AnonymousDevelopmentFilter(IWebHostEnvironment env, TelemetryClient telemetryClient)
            {
                _env = env;
                _telemetryClient = telemetryClient ?? new TelemetryClient();
            }

            /// <summary>
            /// Called early in the filter pipeline to confirm authorization is allowed.
            /// In development environment, allows anonymous access and sets up mock identity.
            /// In production, rejects unauthenticated requests.
            /// </summary>
            /// <param name="context">The authorization filter context.</param>
            public void OnAuthorization(AuthorizationFilterContext context)
            {
                if (_env.IsDevelopment())
                {
                    // Allow anonymous access in development
                    _telemetryClient.TrackEvent("DevelopmentAnonymousAccess",
                        properties: new Dictionary<string, string> {
                            { "path", context.HttpContext.Request.Path }
                        });
                    
                    // Set up a mock identity with appropriate scopes for development testing
                    var identity = new ClaimsIdentity(new[] {
                        new Claim(ClaimTypes.Name, "DevelopmentUser"),
                        new Claim(ClaimTypes.NameIdentifier, "dev-user-id"),
                        new Claim("scope", "Products.Read Products.Write Carts.Read Carts.Write Checkout.Process api.access")
                    }, "Development");
                    
                    context.HttpContext.User = new ClaimsPrincipal(identity);
                }
                else
                {
                    // En producción, rechazar solicitudes no autenticadas
                    if (!context.HttpContext.User.Identity.IsAuthenticated)
                    {
                        context.Result = new UnauthorizedResult();
                        
                        // Registrar el intento de acceso no autorizado
                        _telemetryClient.TrackEvent("UnauthorizedAccess",
                            properties: new Dictionary<string, string> {
                                { "path", context.HttpContext.Request.Path }
                            });
                    }
                }
            }
        }
    }
}