// Copyright (c) Microsoft Corporation. 
// Licensed under the MIT license.

using Microsoft.Extensions.Caching.Memory;
using MyOpenAIWebApi.Options;
using Microsoft.Extensions.Options;
using MyOpenAIWebApi.Helpers;
using System.IdentityModel.Tokens.Jwt;

namespace MyOpenAIWebApi.Services;

/// <summary>
/// Interface for managing OpenAI assistant services for different users.
/// This interface defines the contract for creating, retrieving, and managing
/// user-specific OpenAI service instances.
/// </summary>
public interface IAssistantManager
{
    /// <summary>
    /// Gets an existing or creates a new OpenAI service for a specific user.
    /// This method handles caching of service instances to avoid recreating
    /// services unnecessarily.
    /// </summary>
    /// <param name="userId">The unique identifier for the user</param>
    /// <returns>A configured OpenAI service instance customized for the user</returns>
    Task<IOpenAIService> GetOrCreateOpenAIServiceForUserAsync(string userId);
}

/// <summary>
/// In-memory implementation of the assistant manager that caches OpenAI services
/// per user. This implementation uses an in-memory cache to store and retrieve
/// user-specific OpenAI service instances.
/// </summary>
public class InMemoryAssistantManager : IAssistantManager
{
    private readonly IMemoryCache _cache;
    private readonly IOptions<OpenAIOptions> _options; 
    private readonly IOptions<CustomAPIOptions> _customAPIOptions;
    private readonly IOptions<RAGOptions> _ragOptions;
    private readonly CustomAPIHelper _customAPIHelper;

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryAssistantManager"/> class
    /// </summary>
    /// <param name="cache">Memory cache for storing service instances</param>
    /// <param name="options">OpenAI configuration options</param>
    /// <param name="customAPIOptions">Custom API configuration options</param>
    /// <param name="ragOptions">RAG (Retrieval Augmented Generation) configuration options</param>
    /// <param name="customAPIHelper">Helper for making custom API calls</param>
    public InMemoryAssistantManager(
        IMemoryCache cache,
        IOptions<OpenAIOptions> options,
        IOptions<CustomAPIOptions> customAPIOptions,
        IOptions<RAGOptions> ragOptions,
        CustomAPIHelper customAPIHelper
    )
    {
        _cache = cache;
        _options = options;
        _customAPIOptions = customAPIOptions;
        _ragOptions = ragOptions;
        _customAPIHelper = customAPIHelper;
    }    /// <summary>
    /// Gets an existing OpenAI service for a user from the cache or creates a new one.
    /// This method ensures each user has a dedicated, properly initialized OpenAI service
    /// instance while efficiently managing resources through caching.
    /// </summary>
    /// <param name="userToken">The user's authentication token containing identity information</param>
    /// <returns>A configured OpenAI service instance ready to process the user's requests</returns>
    /// <remarks>
    /// The method handles:
    /// - Extracting the user ID from the authentication token
    /// - Checking if a service instance already exists in cache
    /// - Updating the token in existing instances to maintain current authentication
    /// - Creating and initializing new service instances when needed
    /// - Caching services with an appropriate expiration policy
    /// </remarks>
    public async Task<IOpenAIService> GetOrCreateOpenAIServiceForUserAsync(string userToken)
    {
        // Extract the userId from the access token
        string userId = await ExtractUserIdFromTokenAsync(userToken);
        
        // Check if a service already exists in the cache
        if (_cache.TryGetValue<IOpenAIService>(userId, out var existingService))
        {
            // If a service exists in the cache, just update the token
            if (existingService is OpenAIService openAIService)
            {
                // Update the token directly in the existing service
                openAIService.UpdateUserToken(userToken);
            }
            
            return existingService;
        }

        // Create a new service and configure it with the necessary information
        var service = new OpenAIService(_options, _customAPIHelper, _customAPIOptions, _ragOptions, userToken);

        // Initialize asynchronous components
        await service.InitializeAssistantAsync();

        // Store in cache for 4 hours
        _cache.Set(userId, service, TimeSpan.FromHours(4));

        return service;
    }    /// <summary>
    /// Extracts the unique user identifier from the JWT access token.
    /// This method parses and processes the JWT token to find the appropriate
    /// claim containing the user's identity.
    /// </summary>
    /// <param name="accessToken">The user's access token, with or without the "Bearer " prefix</param>
    /// <returns>
    /// The unique user identifier (typically the object ID from Microsoft Entra ID
    /// or subject ID from other identity providers)
    /// </returns>
    /// <remarks>
    /// This method handles both Microsoft Entra ID tokens (which use "oid" claim) 
    /// and standard OAuth/OIDC tokens (which use "sub" claim). It provides graceful
    /// error handling by returning an error identifier if token parsing fails.
    /// 
    /// Security note: This method does NOT validate the token signature or expiration.
    /// It is assumed that token validation has already been performed by the authentication
    /// middleware. This is only for extracting the user ID from a validated token.
    /// </remarks>
    private Task<string> ExtractUserIdFromTokenAsync(string accessToken)
    {
        try
        {
            // Remove "Bearer " prefix if it exists
            if (accessToken.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                accessToken = accessToken.Substring(7);
            }

            // Decode the JWT token without validating the signature
            var handler = new JwtSecurityTokenHandler();
            var token = handler.ReadJwtToken(accessToken);

            // Find the claim containing the user ID
            // Usually "oid" (object ID) in Microsoft/Azure AD tokens
            // or "sub" (subject) in other providers
            var oidClaim = token.Claims.FirstOrDefault(c => c.Type == "oid");
            var subClaim = token.Claims.FirstOrDefault(c => c.Type == "sub");
            
            string? userId = null;
            if (oidClaim != null)
            {
                userId = oidClaim.Value;
            }
            else if (subClaim != null)
            {
                userId = subClaim.Value;
            }
        
            if (userId == null)
            {
                throw new Exception("Could not find 'oid' or 'sub' claim in the token.");
            }

            return Task.FromResult(userId);
        }
        catch (Exception ex)
        {
            // Return a special error value instead of throwing an exception
            // This allows the application to continue functioning even when
            // token parsing fails, with a clear error identifier
            return Task.FromResult("error-parsing-token: error " + ex.Message);
        }
    }

    /// <summary>
    /// Resets the OpenAI service for a specific user by removing it from the cache
    /// </summary>
    /// <param name="userId">The user's unique identifier</param>
    public Task ResetOpenAIServiceForUserAsync(string userId)
    {
        // Remove from cache so it's recreated on the next request
        _cache.Remove(userId);
        return Task.CompletedTask;
    }
}
