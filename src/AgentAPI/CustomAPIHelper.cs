// Copyright (c) Microsoft Corporation. 
// Licensed under the MIT license.

using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Web;
using Azure.Identity;
using Microsoft.Extensions.Options;
using MyOpenAIWebApi.Options;

namespace MyOpenAIWebApi.Helpers;

/// <summary>
/// Parameters for making custom API requests
/// </summary>
public class CustomAPIParameters
{
    /// <summary>
    /// HTTP method for the request (GET, POST, PUT, DELETE, etc.)
    /// </summary>
    [JsonPropertyName("method")]
    public string Method { get; set; } = string.Empty;
    
    /// <summary>
    /// URI path and query parameters for the request
    /// </summary>
    [JsonPropertyName("uri")]
    public string Uri { get; set; } = string.Empty;
    
    /// <summary>
    /// Optional JSON body for the request
    /// </summary>
    [JsonPropertyName("body")]
    public string? Body { get; set; }
    
    /// <summary>
    /// Indicates if the user has confirmed a write operation
    /// </summary>
    [JsonPropertyName("writeConfirmed")]
    public bool WriteConfirmed { get; set; }
    
    /// <summary>
    /// OAuth scopes required for the API request
    /// </summary>
    [JsonPropertyName("scopes")]
    public string[] Scopes { get; set; } = [];
    
    private CustomAPIOptions? _apiOptions;
    
    /// <summary>
    /// Parameterless constructor for deserialization
    /// </summary>
    public CustomAPIParameters() { }
    
    /// <summary>
    /// Constructor with all parameters for manual instance creation
    /// </summary>
    /// <param name="method">HTTP method for the request</param>
    /// <param name="uri">URI path and query parameters</param>
    /// <param name="body">Optional JSON body</param>
    /// <param name="writeConfirmed">Indicates if the user has confirmed a write operation</param>
    /// <param name="scopes">OAuth scopes required for the API request</param>
    /// <param name="apiOptions">API configuration options</param>
    public CustomAPIParameters(string method, string uri, string? body, bool writeConfirmed, string[] scopes, CustomAPIOptions? apiOptions = null)
    {
        Method = method;
        Uri = uri;
        Body = body;
        WriteConfirmed = writeConfirmed;
        Scopes = scopes;
        _apiOptions = apiOptions;
    }

    /// <summary>
    /// Gets the complete base URI with the proper formatting
    /// </summary>
    public string FixedBaseUri
    {
        get
        {
            // If it's already an absolute URL, leave it as is
            if (Uri.StartsWith("https://") || Uri.StartsWith("http://"))
                return Uri;
                
            // If we have API options configured, use BuildUrl
            if (_apiOptions != null)
            {
                // Detect if the URI should use OData prefix based on whether it contains OData operators ($filter, $top, etc.)
                bool isODataQuery = Uri.Contains("$filter") || Uri.Contains("$top") || 
                                   Uri.Contains("$skip") || Uri.Contains("$select") || 
                                   Uri.Contains("$expand") || Uri.Contains("$orderby") || 
                                   Uri.Contains("$count");
                
                return _apiOptions.BuildUrl(Uri, isODataQuery);
            }
                
            // We should never return a relative URL in any case,
            // so throw an exception if we get here
            throw new InvalidOperationException(
                "No valid base URL has been configured for the API. " +
                "Check the 'CustomAPI:BaseUrl' configuration in appsettings.json");
        }
    }
}

/// <summary>
/// Helper class for making requests to custom APIs
/// </summary>
public class CustomAPIHelper
{
    /// <summary>
    /// Maximum number of requests allowed
    /// </summary>
    public const byte MaxRequests = 100;
    
    /// <summary>
    /// Current number of requests that have been made
    /// </summary>
    public static byte CurrentRequestCount { get; set; } = 0;

    private readonly HttpClient _apiClient;
    private readonly CustomAPIOptions _apiOptions;
    
    /// <summary>
    /// Formats an error message into a JSON structure
    /// </summary>
    /// <param name="code">Error code</param>
    /// <param name="message">Error message</param>
    /// <returns>Formatted JSON error message</returns>
    private static string FormatErrorMessage(string? code, string message) =>
       //language=JSON
       $$"""
        { "error": { "code": "{{code}}", "message": "{{message}}" } }
        """;

    private readonly TokenHelpers _tokenHelper;
    
    /// <summary>
    /// Factory to create HttpClient instances for CustomAPI
    /// </summary>
    private static class CustomAPIClientFactory
    {
        /// <summary>
        /// Creates a new HttpClient with appropriate default headers
        /// </summary>
        /// <returns>Configured HttpClient instance</returns>
        /// TODO: Check if this could lead to port exhaustion
        public static HttpClient Create()
        {
            var client = new HttpClient();
            
            // Configure default headers for your custom API
            client.DefaultRequestHeaders.Add("Accept", "application/json");
            client.DefaultRequestHeaders.Add("ConsistencyLevel", "eventual");
            client.DefaultRequestHeaders.Add("Prefer", "odata.include-annotations=*");
            
            return client;
        }
    }

    /// <summary>
    /// Initializes a new instance of the CustomAPIHelper class
    /// </summary>
    /// <param name="tokenHelper">Helper for token operations</param>
    /// <param name="options">Custom API configuration options</param>
    public CustomAPIHelper(TokenHelpers tokenHelper, IOptions<CustomAPIOptions> options)
    {
        _apiClient = CustomAPIClientFactory.Create();
        _tokenHelper = tokenHelper;
        _apiOptions = options.Value;
        
        // Set the base URL in the HTTP client
        var baseUrl = _apiOptions.GetFullBaseUrl();
        if (!string.IsNullOrEmpty(baseUrl))
        {
            _apiClient.BaseAddress = new Uri(baseUrl);
        }
    }

    //TODO: Add documentation comments
    public async Task<string> ExecuteQuery(CustomAPIParameters requestParameters, string userToken)
    {
        // 🔍 DEMO POINT: Calls the token helper to get a new access token 
        // Get the token based on the provided scopes
        _apiClient.DefaultRequestHeaders.Authorization = new("Bearer",
            await _tokenHelper.GetTokenOnBehalfOfAsync(userToken, requestParameters.Scopes));

        Debug.WriteLine($"Generated Url: {requestParameters.Method} {requestParameters.Uri}");

        if (++CurrentRequestCount >= MaxRequests)
        {
            var error = FormatErrorMessage("ClientRequestException",
                $"Maximum numbers of request reached ({MaxRequests}); this query plan is too complex.");
            OutputError(error);
            return error;
        }

        var content = requestParameters.Method == "GET"
            ? await HandleRead(requestParameters.FixedBaseUri)
            : await HandleWrite(requestParameters);
        return content;
    }

    //TODO: Add documentation comments
    // Overload that automatically uses the configured base URL
    public async Task<string> ExecuteQuery(string method, string relativeUri, string? body, bool writeConfirmed, string[] scopes, string userToken)
    {
        var parameters = new CustomAPIParameters(method, relativeUri, body, writeConfirmed, scopes, _apiOptions);
        return await ExecuteQuery(parameters, userToken);
    }

    //TODO: Add documentation comments
    private async Task<string> HandleRead(string uri)
    {
        var urlParts = uri.Split('?');

        // Extract URL segments for additional processing if needed
        var segments = urlParts[0]
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .ToHashSet();

        Dictionary<string, string> parameters = [];

        if (urlParts.Length == 2)
        {
            var parsedQueryString = HttpUtility.ParseQueryString(urlParts[1]);
            parameters = parsedQueryString.AllKeys.ToDictionary(key => key!.ToLower(), key => parsedQueryString[key]!);
        }

        //TODO: CHECK IF THIS IS TRUE OR ONLY GRAPH SPECIFIC
        // OData parameter management
        if (parameters.TryGetValue("$expand", out var expandValue) && expandValue.Contains("$filter"))
        {
            var error = FormatErrorMessage("BadRequest",
                "$filter inside $expand not supported, execute the query without $filter inside $expand and then filter in the next query");
            OutputError(error);
            return error;
        }

        // Process any additional customization for your OData APIs
        // Preserve original values especially for OData functions like contains()
        var queryParams = new List<string>();
        foreach (var kvp in parameters)
        {
            var key = kvp.Key;
            var value = kvp.Value;

            // Special handling for $filter with functions like contains(), startswith(), etc.
            if (key == "$filter" &&
               (value.Contains("contains(") || value.Contains("startswith(") || value.Contains("endswith(")))
            {
                // Preserve the $filter as it is, avoiding re-encoding
                queryParams.Add($"{key}={value}");
            }
            else
            {
                // For other parameters, use standard encoding
                queryParams.Add($"{key}={HttpUtility.UrlEncode(value)}");
            }
        }

        var queryString = string.Join("&", queryParams);
        uri = string.Join('?', new[] { urlParts[0], queryString }.Where(s => !string.IsNullOrEmpty(s)));

        // Perform the GET request to your OData API
        Debug.WriteLine($"Performing GET request to: {uri}");
        var response = await _apiClient.GetAsync(uri);
        var content = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            OutputError(content);

        return content;
    }


    //TODO: Add documentation comments
    private void OutputError(string content)
    {
        try
        {
            using var outputJson = JsonDocument.Parse(content);
            if (outputJson.RootElement.ValueKind == JsonValueKind.Object &&
                outputJson.RootElement.TryGetProperty("error", out var errorArgument))
            {
                throw new Exception(content);
            }
        }
        catch (JsonException)
        {
            // If it's not valid JSON, simply throw an exception with the original content
            throw new Exception(content);
        }
    }

    //TODO: Add documentation comments
    private async Task<string> HandleWrite(CustomAPIParameters requestParameters)
    {
        if (!requestParameters.WriteConfirmed)
        {
            return $"Ask the user to confirm the following write operation: {requestParameters.Method} {requestParameters.FixedBaseUri}\nRequest Body: {requestParameters.Body}";
        }

        // Process the URL in the same way as in HandleRead
        string uri = requestParameters.FixedBaseUri;
        var urlParts = uri.Split('?');

        Dictionary<string, string> parameters = [];

        if (urlParts.Length == 2)
        {
            var parsedQueryString = HttpUtility.ParseQueryString(urlParts[1]);
            parameters = parsedQueryString.AllKeys.ToDictionary(key => key!.ToLower(), key => parsedQueryString[key]!);
        }

        // Apply the same OData parameter validation as in HandleRead
        if (parameters.TryGetValue("$expand", out var expandValue) && expandValue.Contains("$filter"))
        {
            var error = FormatErrorMessage("BadRequest",
                "$filter inside $expand not supported, execute the query without $filter inside $expand and then filter in the next query");
            OutputError(error);
            return error;
        }

        // Rebuild the URL with processed parameters
        var queryString = string.Join("&", parameters.Select(kvp => $"{kvp.Key}={kvp.Value}"));
        uri = string.Join('?', new[] { urlParts[0], queryString }.Where(s => !string.IsNullOrEmpty(s)));

        // Create the request with the processed URL
        var request = new HttpRequestMessage(new HttpMethod(requestParameters.Method), uri);

        if (requestParameters.Body != null)
            request.Content = new StringContent(requestParameters.Body, Encoding.UTF8, "application/json");

        // Log for debugging
        Debug.WriteLine($"Executing {requestParameters.Method} to {uri}");
        if (requestParameters.Body != null)
            Debug.WriteLine($"Request Body: {requestParameters.Body}");

        var response = await _apiClient.SendAsync(request);
        var content = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            OutputError(content);

        return $"HTTP/1.1 {response.StatusCode}\n{content}";
    }
}