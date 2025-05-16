// Copyright (c) Microsoft Corporation. 
// Licensed under the MIT license.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Identity.Web;
using Microsoft.Identity.Client;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authentication;

namespace WebAppConRazor.Pages
{
    /// <summary>
    /// Controller for the protected page that requires authentication.
    /// Manages token acquisition and authentication flows for accessing secured resources.
    /// </summary>
    [Authorize]
    public class ProtectedModel : PageModel
    {
        private readonly ITokenAcquisition _tokenAcquisition;
        private readonly IConfiguration _configuration;

        /// <summary>
        /// Gets or sets the access token used to authorize API calls.
        /// </summary>
        public string AccessToken { get; private set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets the ID token containing user claims.
        /// </summary>
        public string IdToken { get; private set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets the error message if token acquisition fails.
        /// </summary>
        public string TokenError { get; private set; } = string.Empty;
        
        /// <summary>
        /// Gets or sets a value indicating whether the user needs to provide additional consent.
        /// </summary>
        public bool NeedsConsent { get; private set; }
        
        /// <summary>
        /// Gets or sets the scopes required for API access.
        /// </summary>
        public string[] Scopes { get; private set; } = Array.Empty<string>();
        
        /// <summary>
        /// Gets or sets the URL for the chat SignalR hub.
        /// </summary>
        public string ChatHubUrl { get; private set; } = string.Empty;

        /// <summary>
        /// Initializes a new instance of the <see cref="ProtectedModel"/> class.
        /// </summary>
        /// <param name="tokenAcquisition">The token acquisition service.</param>
        /// <param name="configuration">The configuration service.</param>
        public ProtectedModel(ITokenAcquisition tokenAcquisition, IConfiguration configuration)
        {
            _tokenAcquisition = tokenAcquisition;
            _configuration = configuration;
        }

        /// <summary>
        /// Handles GET requests to the protected page.
        /// Acquires access tokens for the authenticated user and prepares data for the view.
        /// </summary>
        /// <returns>The page result or a challenge if authentication is required.</returns>
        public async Task<IActionResult> OnGetAsync()
        {
            try
            {
                // Get the configured scopes for ChatApi
                Scopes = _configuration.GetSection("ChatApi:Scopes").Get<string[]>() ?? Array.Empty<string>();
                
                if (Scopes.Length == 0)
                {
                    TokenError = "No scopes configured in the ChatApi:Scopes section";
                    return Page();
                }

                // Get the SignalR Hub URL
                string baseUrl = _configuration["ChatApi:BaseUrl"] ?? string.Empty;
                string hubPath = _configuration["ChatApi:HubPath"] ?? "/assistantHub";
                ChatHubUrl = $"{baseUrl}{hubPath}";

                // Get token for the current user using the configured scopes
                AccessToken = await _tokenAcquisition.GetAccessTokenForUserAsync(Scopes);
                
                // Get the id_token from the user claims
                IdToken = await GetIdTokenAsync();
                
                return Page();
            }
            catch (MicrosoftIdentityWebChallengeUserException)
            {
                NeedsConsent = true;
                return Challenge(
                    new AuthenticationProperties 
                    { 
                        RedirectUri = Url.Page("/Protected") 
                    },
                    OpenIdConnectDefaults.AuthenticationScheme);
            }
            catch (MsalUiRequiredException)
            {
                NeedsConsent = true;
                return Challenge(
                    new AuthenticationProperties 
                    { 
                        RedirectUri = Url.Page("/Protected") 
                    },
                    OpenIdConnectDefaults.AuthenticationScheme);
            }
            catch (System.Exception ex)
            {
                TokenError = $"Error obtaining token: {ex.Message}";
                return Page();
            }
        }

        /// <summary>
        /// Retrieves the ID token from the current authentication context.
        /// </summary>
        /// <returns>The ID token or a message indicating it's not available.</returns>
        private async Task<string> GetIdTokenAsync()
        {
            // Get the id_token from the current authentication
            var result = await HttpContext.AuthenticateAsync(OpenIdConnectDefaults.AuthenticationScheme);
            if (result?.Properties?.Items != null && 
                result.Properties.Items.TryGetValue(".TokenNames", out var tokenNames) &&
                result.Properties.Items.TryGetValue(".Token.id_token", out var idToken) &&
                idToken != null)
            {
                return idToken;
            }
            return "(Id token not available)";
        }

        /// <summary>
        /// Handles POST requests for consent to access additional scopes.
        /// Redirects to the identity provider for consent.
        /// </summary>
        /// <returns>A challenge result that triggers authentication.</returns>
        public IActionResult OnPostConsentAsync()
        {
            Scopes = _configuration.GetSection("ChatApi:Scopes").Get<string[]>() ?? Array.Empty<string>();
            if (Scopes.Length == 0)
            {
                TokenError = "No scopes configured";
                return Page();
            }

            return Challenge(
                new AuthenticationProperties 
                { 
                    RedirectUri = Url.Page("/Protected") 
                },
                OpenIdConnectDefaults.AuthenticationScheme);
        }
    }
}