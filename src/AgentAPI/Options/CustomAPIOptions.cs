// Copyright (c) Microsoft Corporation. 
// Licensed under the MIT license.

namespace MyOpenAIWebApi.Options;

/// <summary>
/// Configuration options for the custom API
/// </summary>
public class CustomAPIOptions
{
    /// <summary>
    /// Base URL for the API (without trailing slash)
    /// </summary>
    public string BaseUrl { get; set; } = "http://localhost:5173";
    
    /// <summary>
    /// Base path for OData endpoints
    /// </summary>
    public string BasePath { get; set; } = "odata";
    
    /// <summary>
    /// Default OAuth scope for API access
    /// </summary>
    public string DefaultScope { get; set; } = "";
    
    /// <summary>
    /// Gets the complete base URL ending with /
    /// </summary>
    /// <returns>The base URL with trailing slash</returns>
    public string GetFullBaseUrl()
    {
        var baseUrl = BaseUrl.TrimEnd('/');
        return $"{baseUrl}/";
    }
    
    /// <summary>
    /// Gets the base URL for OData endpoints
    /// </summary>
    /// <returns>The base URL for OData endpoints with trailing slash</returns>
    public string GetODataBaseUrl()
    {
        var baseUrl = BaseUrl.TrimEnd('/');
        var basePath = BasePath.Trim('/');
        return $"{baseUrl}/{basePath}/";
    }
    
    /// <summary>
    /// Gets the base URL for standard REST API endpoints
    /// </summary>
    public string GetApiBaseUrl()
    {
        var baseUrl = BaseUrl.TrimEnd('/');
        return $"{baseUrl}/api/";
    }
    
    /// <summary>
    /// Builds a complete URL based on the provided path
    /// </summary>
    /// <param name="path">Relative path</param>
    /// <param name="useOData">If true, uses the OData prefix, if false uses the API prefix</param>
    public string BuildUrl(string path, bool useOData)
    {
        path = path.TrimStart('/');
        
        // If the path already includes 'api/' or BasePath, don't add a prefix
        if (path.StartsWith("api/", StringComparison.OrdinalIgnoreCase) || 
            path.StartsWith($"{BasePath}/", StringComparison.OrdinalIgnoreCase))
        {
            return $"{GetFullBaseUrl()}{path}";
        }
        
        // Determine if we use the OData or API prefix
        return useOData 
            ? $"{GetODataBaseUrl()}{path}" 
            : $"{GetApiBaseUrl()}{path}";
    }
}