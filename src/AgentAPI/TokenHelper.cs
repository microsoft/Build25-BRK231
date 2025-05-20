// Copyright (c) Microsoft Corporation. 
// Licensed under the MIT license.

using Microsoft.Identity.Client;
using System.Diagnostics;

namespace MyOpenAIWebApi.Helpers;

/// <summary>
/// Helper class for obtaining and managing authentication tokens
/// </summary>
public class TokenHelpers
{
    private readonly IConfidentialClientApplication _app;

    /// <summary>
    /// Initializes a new instance of the TokenHelpers class
    /// </summary>
    /// <param name="tenantId">The Microsoft Entra ID tenant ID</param>
    /// <param name="clientId">The client ID (application ID)</param>
    /// <param name="clientSecret">The client secret</param>
    /// <param name="authority">The authority URL</param>
    public TokenHelpers(string tenantId, string clientId, string clientSecret, string authority)
    {
        _app = ConfidentialClientApplicationBuilder.Create(clientId)
            .WithClientSecret(clientSecret)
            .WithAuthority($"{authority}{tenantId}")
            .Build();
    }

    /// <summary>
    /// Gets a token using the On-Behalf-Of (OBO) flow. This method delegates
    /// the user's identity to another service by exchanging the original token
    /// for a new token with different resource access.
    /// </summary>
    /// <param name="userAccessToken">The access token received from the client. This token represents
    /// the authenticated user's identity and permissions.</param>
    /// <param name="scopes">The scopes (permissions) required for the new token. These define
    /// what resources the delegated token will have access to.</param>
    /// <returns>An access token for the requested resource that maintains the user's identity context</returns>
    /// <remarks>
    /// The On-Behalf-Of flow allows microservices to propagate user identity across service boundaries.
    /// Common errors include:
    /// - Consent issues: The user needs to consent to the application using their identity
    /// - Scope issues: The application may request scopes it's not authorized for
    /// - Token issues: The original token may be expired or invalid
    /// </remarks>
    public async Task<string> GetTokenOnBehalfOfAsync(string userAccessToken, string[] scopes)
    {
        try
        {
            // Execute the OAuth 2.0 On-Behalf-Of flow to exchange the user's token
            // for a new token with the requested scopes
            var result = await _app.AcquireTokenOnBehalfOf(scopes, new UserAssertion(userAccessToken))
                                .ExecuteAsync();

            return result.AccessToken;
        }
        catch (Exception ex)
        {
            // Log detailed error information to debug channels
            // This helps troubleshoot authentication issues during development and in production
            Debug.WriteLine($"Error in GetTokenOnBehalfOfAsync: {ex.Message}");
            Console.WriteLine($"Error getting OBO token: {ex.GetType().Name}: {ex.Message}");

            if (ex.InnerException != null)
            {
                Debug.WriteLine($"Inner exception: {ex.InnerException.Message}");
                Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
            }

            // In a real production environment, you would implement more robust error handling:
            // - Retry logic with exponential backoff for transient failures
            // - Telemetry capture for monitoring authentication issues
            // - User-friendly error messages based on exception types
            // - Circuit breakers to prevent cascading failures

            // Return a placeholder error token instead of throwing an exception
            // This allows the application to gracefully handle auth failures
            return "error-obtaining-token"; // In production, consider throwing a custom exception
        }
    }
}
