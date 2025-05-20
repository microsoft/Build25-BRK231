// Copyright (c) Microsoft Corporation. 
// Licensed under the MIT license.

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace WebAppConRazor.Pages
{
    /// <summary>
    /// Controller for the step-up authentication page.
    /// Handles multi-factor authentication step-up requests and redirections.
    /// </summary>
    public class StepUpModel : PageModel
    {
        private readonly IConfiguration _configuration;

        /// <summary>
        /// Initializes a new instance of the <see cref="StepUpModel"/> class.
        /// </summary>
        /// <param name="configuration">The configuration service.</param>
        public StepUpModel(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        /// <summary>
        /// Gets or sets the URL to return to after authentication.
        /// </summary>
        [BindProperty]
        public string ReturnUrl { get; set; } = "/";

        /// <summary>
        /// Gets or sets the action being requested that requires step-up authentication.
        /// </summary>
        [BindProperty]
        public string Action { get; set; } = "";

        /// <summary>
        /// Handles GET requests to the step-up page.
        /// </summary>
        /// <param name="returnUrl">The URL to return to after authentication.</param>
        /// <param name="action">The action being requested.</param>
        public void OnGet(string returnUrl = "/", string action = "")
        {
            ReturnUrl = returnUrl;
            Action = action;
        }

        /// <summary>
        /// Handles GET requests for modal step-up content.
        /// </summary>
        /// <param name="returnUrl">The URL to return to after authentication.</param>
        /// <param name="action">The action being requested.</param>
        /// <returns>A partial view result containing the modal content.</returns>
        public IActionResult OnGetModalContent(string returnUrl = "/", string action = "")
        {
            ReturnUrl = returnUrl;
            Action = action;
            
            // Return partial content for the modal
            return new PartialViewResult
            {
                ViewName = "_StepUpModalContent",
                ViewData = new Microsoft.AspNetCore.Mvc.ViewFeatures.ViewDataDictionary<StepUpModel>(ViewData, this)
            };
        }

        /// <summary>
        /// Handles POST requests to initiate step-up authentication.
        /// </summary>
        /// <returns>A challenge result that triggers step-up authentication.</returns>
        public async Task<IActionResult> OnPostAsync()
        {
            // If the user is not authenticated, redirect to login
            if (!User.Identity?.IsAuthenticated ?? true)
            {
                return Challenge(new AuthenticationProperties { RedirectUri = "/" }, OpenIdConnectDefaults.AuthenticationScheme);
            }

            // Extract the return URL from the form
            string returnUrl = Request.Form["returnUrl"].ToString();
            if (string.IsNullOrEmpty(returnUrl))
            {
                returnUrl = "/";
            }

            // Extract the action from the form (if available)
            string action = Request.Form["action"].ToString();

            // Get the scopes from configuration
            var scopes = _configuration.GetSection("ChatApi:Scopes").Get<string[]>() ?? Array.Empty<string>();
            string scopesValue = string.Join(" ", scopes);

            // This is the key part that activates step-up authentication with MFA
            var properties = new AuthenticationProperties
            {
                RedirectUri = returnUrl,
                // Add the StepUp parameter that will be used by our OnRedirectToIdentityProvider event
                Items = 
                {
                    { "StepUp", "true" },
                    { "scope", scopesValue }, // Add scopes from the configuration
                },
            };

            // Get the user name (typically the email) and add it as login_hint
            if (User.Identity?.IsAuthenticated == true && !string.IsNullOrEmpty(User.Identity.Name))
            {
                properties.Items.Add("login_hint", User.Identity.Name);
            }

            // If there is a specific action, add it to the properties
            if (!string.IsNullOrEmpty(action))
            {
                properties.Items.Add("action", action);
            }

            // Challenge the user with authentication that will include the claims parameter
            return await Task.FromResult(Challenge(properties, OpenIdConnectDefaults.AuthenticationScheme));
        }
    }
}