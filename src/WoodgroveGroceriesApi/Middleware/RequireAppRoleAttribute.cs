// Copyright (c) Microsoft Corporation. 
// Licensed under the MIT license.

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Security.Claims;

namespace WoodgroveGroceriesApi.Middleware
{
    /// <summary>
    /// Attribute that requires the authenticated user to have specific application roles.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
    public class RequireAppRoleAttribute : Attribute, IAuthorizationFilter
    {
        /// <summary>
        /// Gets the roles that are accepted for authorization.
        /// </summary>
        public string[] AcceptedRoles { get; }
        private readonly IConfiguration _configuration;

        /// <summary>
        /// Initializes a new instance of the <see cref="RequireAppRoleAttribute"/> class.
        /// </summary>
        /// <param name="acceptedRoles">The roles that are accepted for authorization.</param>
        public RequireAppRoleAttribute(params string[] acceptedRoles)
        {
            AcceptedRoles = acceptedRoles;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="RequireAppRoleAttribute"/> class.
        /// </summary>
        /// <param name="configuration">The configuration instance to get roles from if none are specified.</param>
        /// <param name="acceptedRoles">The roles that are accepted for authorization.</param>
        public RequireAppRoleAttribute(IConfiguration configuration, params string[] acceptedRoles)
        {
            AcceptedRoles = acceptedRoles;
            _configuration = configuration;
        }

        /// <summary>
        /// Called early in the filter pipeline to confirm authorization is allowed.
        /// </summary>
        /// <param name="context">The authorization filter context.</param>
        public void OnAuthorization(AuthorizationFilterContext context)
        {
            var env = context.HttpContext.RequestServices.GetService<IWebHostEnvironment>();
            
            // In development environment, allow access
            if (env != null && env.IsDevelopment())
            {
                return;
            }

            if (context.HttpContext.User?.Identity?.IsAuthenticated != true)
            {
                context.Result = new UnauthorizedResult();
                return;
            }

            // Get required role from configuration if no roles specified explicitly
            var requiredRoles = AcceptedRoles;
            if ((requiredRoles == null || requiredRoles.Length == 0) && _configuration != null)
            {
                var configRole = _configuration["Authorization:ProductManagerRole"];
                if (!string.IsNullOrEmpty(configRole))
                {
                    requiredRoles = new[] { configRole };
                }
            }

            if (requiredRoles == null || requiredRoles.Length == 0)
            {
                // No roles required, access allowed
                return;
            }

            // Check for role claim in the token (typically comes from app_role in Azure AD)
            var hasRequiredRole = requiredRoles.Any(role => 
                context.HttpContext.User.HasClaim(c => 
                    (c.Type == "roles" || c.Type == ClaimTypes.Role) && c.Value == role));

            if (!hasRequiredRole)
            {
                context.Result = new ForbidResult();
            }
        }
    }
}