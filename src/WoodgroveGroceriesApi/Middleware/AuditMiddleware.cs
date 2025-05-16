// Copyright (c) Microsoft Corporation. 
// Licensed under the MIT license.

using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;

namespace WoodgroveGroceriesApi.Middleware
{
    /// <summary>
    /// Middleware for auditing all HTTP requests and responses in the application.
    /// </summary>
    public class AuditMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly TelemetryClient _telemetryClient;
        private readonly ILogger<AuditMiddleware> _logger;
        private readonly IWebHostEnvironment _environment;

        /// <summary>
        /// Initializes a new instance of the <see cref="AuditMiddleware"/> class.
        /// </summary>
        /// <param name="next">The next middleware in the pipeline.</param>
        /// <param name="telemetryClient">The Application Insights telemetry client.</param>
        /// <param name="logger">The logger instance.</param>
        /// <param name="environment">The web hosting environment information.</param>
        public AuditMiddleware(
            RequestDelegate next,
            TelemetryClient telemetryClient,
            ILogger<AuditMiddleware> logger,
            IWebHostEnvironment environment)
        {
            _next = next;
            _telemetryClient = telemetryClient;
            _logger = logger;
            _environment = environment;
        }

        /// <summary>
        /// Processes an HTTP request by logging request and response data for auditing purposes.
        /// </summary>
        /// <param name="context">The HTTP context for the current request.</param>
        /// <returns>A task that represents the completion of request processing.</returns>
        public async Task InvokeAsync(HttpContext context)
        {
            // Capture start time to measure duration
            var startTime = DateTime.UtcNow;
            var stopwatch = Stopwatch.StartNew();

            // Create a container for all audit data
            var auditData = new Dictionary<string, object>();
            
            // 1. Request context
            auditData["timestamp"] = startTime.ToString("o"); // ISO 8601 format
            auditData["traceId"] = context.TraceIdentifier;
            auditData["requestPath"] = context.Request.Path.ToString();
            auditData["requestMethod"] = context.Request.Method;
            auditData["remoteIp"] = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            auditData["userAgent"] = context.Request.Headers.UserAgent.ToString();
            
            // Prepare container to capture response size
            var originalBodyStream = context.Response.Body;
            using var responseBody = new MemoryStream();
            context.Response.Body = responseBody;
            
            try
            {
                // 2. Authentication events - Extract JWT token info
                var authHeader = context.Request.Headers.Authorization.ToString();
                if (!string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    var token = authHeader.Substring("Bearer ".Length).Trim();
                    var tokenInfo = ExtractTokenInfo(token);
                    auditData["authToken"] = tokenInfo;
                    
                    // Get authenticated user info (if available)
                    var userId = context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "anonymous";
                    var userName = context.User?.FindFirst("name")?.Value ?? 
                                context.User?.FindFirst(ClaimTypes.Name)?.Value ?? "unknown";
                    var userEmail = context.User?.FindFirst(ClaimTypes.Email)?.Value ?? "unknown";
                    
                    // Get scopes from token (if exists)
                    var scopesClaim = context.User?.FindFirst("scp") ?? context.User?.FindFirst("scope");
                    var scopes = scopesClaim?.Value?.Split(' ') ?? Array.Empty<string>();
                    
                    // 3. Authorization events
                    auditData["userIdentity"] = new Dictionary<string, string>
                    {
                        ["userId"] = userId,
                        ["userName"] = userName,
                        ["userEmail"] = MaskPII(userEmail) // Mask email for security
                    };
                    
                    auditData["authorization"] = new Dictionary<string, object>
                    {
                        ["providedScopes"] = scopes
                    };
                    
                    // Get roles and groups if they exist
                    var rolesClaim = context.User?.FindFirst("roles") ?? context.User?.FindFirst(ClaimTypes.Role);
                    if (rolesClaim != null)
                    {
                        var roles = rolesClaim.Value.Contains(',') 
                            ? rolesClaim.Value.Split(',') 
                            : new[] { rolesClaim.Value };
                        
                        // Verify that authorization is a dictionary before using
                        if (auditData["authorization"] is Dictionary<string, object> authDict)
                        {
                            authDict["roles"] = roles;
                        }
                    }

                    var groupsClaim = context.User?.FindFirst("groups");
                    if (groupsClaim != null)
                    {
                        var groups = groupsClaim.Value.Contains(',') 
                            ? groupsClaim.Value.Split(',') 
                            : new[] { groupsClaim.Value };
                        
                        // Verify that authorization is a dictionary before using
                        if (auditData["authorization"] is Dictionary<string, object> authDict)
                        {
                            authDict["groups"] = groups;
                        }
                    }
                }
                else
                {
                    auditData["authToken"] = "No token provided";
                }
                
                // Get required scopes for the endpoint (if captured)
                if (context.Items.TryGetValue("RequiredScopes", out var requiredScopesObj) && 
                    requiredScopesObj is string[] requiredScopesArray)
                {
                    if (!auditData.ContainsKey("authorization"))
                    {
                        auditData["authorization"] = new Dictionary<string, object>();
                    }
                    
                    ((Dictionary<string, object>)auditData["authorization"])["requiredScopes"] = requiredScopesArray;
                }

                // Execute the next middleware in the pipeline
                await _next(context);
                
                // 4. Response result
                stopwatch.Stop();
                responseBody.Seek(0, SeekOrigin.Begin);
                string responseBodyContent = string.Empty;
                
                // Don't store response content in logs (only size)
                long responseSize = responseBody.Length;
                
                // Copy content back to original stream for the client to receive
                responseBody.Seek(0, SeekOrigin.Begin);
                await responseBody.CopyToAsync(originalBodyStream);
                
                auditData["response"] = new Dictionary<string, object>
                {
                    ["statusCode"] = context.Response.StatusCode,
                    ["duration"] = stopwatch.ElapsedMilliseconds,
                    ["size"] = responseSize
                };

                // Check if there's an authorization error (401, 403)
                if (context.Response.StatusCode == 401 || context.Response.StatusCode == 403)
                {
                    if (context.Response.StatusCode == 401)
                    {
                        auditData["errorType"] = "Authentication";
                        auditData["errorMessage"] = "User not authenticated or invalid token";
                    }
                    else // 403
                    {
                        auditData["errorType"] = "Authorization";
                        auditData["errorMessage"] = "User lacks required permissions";
                        
                        // Try to get the required scope that was missing
                        var requiredScopes = GetRequiredScopes(context);
                        if (!string.IsNullOrEmpty(requiredScopes))
                        {
                            auditData["missingScopes"] = requiredScopes;
                        }
                    }
                    
                    // Send error event to Application Insights
                    var eventTelemetry = new EventTelemetry(
                        context.Response.StatusCode == 401 ? "AuthenticationFailed" : "AuthorizationFailed");
                    
                    AddPropertiesToTelemetry(eventTelemetry, auditData);
                    _telemetryClient.TrackEvent(eventTelemetry);
                    
                    // Log the error in JSON format
                    _logger.LogWarning("Access denied: {AuditData}", JsonSerializer.Serialize(auditData));
                }
                else
                {
                    // 5. Metrics and health - Log successful access
                    // Send event to Application Insights
                    var eventTelemetry = new EventTelemetry("ApiAccess");
                    AddPropertiesToTelemetry(eventTelemetry, auditData);
                    _telemetryClient.TrackEvent(eventTelemetry);
                    
                    // Log successful access in JSON format
                    _logger.LogInformation("Successful access: {AuditData}", JsonSerializer.Serialize(auditData));
                }
                
                // 5. Additional metrics based on response code
                if (context.Response.StatusCode >= 500)
                {
                    _telemetryClient.TrackMetric("ServerErrors", 1);
                }
                else if (context.Response.StatusCode >= 400)
                {
                    _telemetryClient.TrackMetric("ClientErrors", 1);
                }
                else
                {
                    _telemetryClient.TrackMetric("SuccessfulRequests", 1);
                }
                
                // Latency metrics
                _telemetryClient.TrackMetric("RequestDuration", stopwatch.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                // Restore original body in case of exception
                context.Response.Body = originalBodyStream;
                
                // Log the exception
                auditData["exception"] = new Dictionary<string, string>
                {
                    ["type"] = ex.GetType().Name,
                    ["message"] = ex.Message
                    // Don't include stack trace in production
                };
                
                if (_environment.IsDevelopment())
                {
                    ((Dictionary<string, string>)auditData["exception"])["stackTrace"] = ex.StackTrace ?? "No stack trace available";
                }
                
                // Log exception in Application Insights
                var exceptionTelemetry = new ExceptionTelemetry(ex);
                AddPropertiesToTelemetry(exceptionTelemetry, auditData);
                _telemetryClient.TrackException(exceptionTelemetry);
                
                // Log exception with JSON format
                _logger.LogError(ex, "Unhandled error: {AuditData}", JsonSerializer.Serialize(auditData));
                
                // Re-throw exception to be handled by error middleware
                throw;
            }
        }
        
        /// <summary>
        /// Extracts information from a JWT token.
        /// </summary>
        /// <param name="token">The JWT token string.</param>
        /// <returns>A dictionary containing token information.</returns>
        private Dictionary<string, string> ExtractTokenInfo(string token)
        {
            var result = new Dictionary<string, string>();
            
            try
            {
                var handler = new JwtSecurityTokenHandler();
                if (handler.CanReadToken(token))
                {
                    var jwtToken = handler.ReadJwtToken(token);
                    
                    if (jwtToken.Header?.Kid != null)
                    {
                        result["kid"] = jwtToken.Header.Kid;
                    }
                    
                    if (jwtToken.Issuer != null)
                    {
                        result["issuer"] = jwtToken.Issuer;
                    }
                    
                    if (jwtToken.ValidTo != DateTime.MinValue)
                    {
                        result["expiry"] = jwtToken.ValidTo.ToString("o");
                    }
                    
                    // Add token type (JWT, etc.)
                    result["tokenType"] = "JWT";
                    
                    // Don't log the actual token for security
                }
                else
                {
                    result["error"] = "Not a valid JWT token";
                }
            }
            catch
            {
                result["error"] = "Error parsing token";
            }
            
            return result;
        }

        /// <summary>
        /// Masks personally identifiable information (PII) in a string.
        /// </summary>
        /// <param name="data">The string containing PII.</param>
        /// <returns>The masked string.</returns>
        private string MaskPII(string data)
        {
            // Simple PII masking - keep first 2 chars and mask the rest
            if (string.IsNullOrEmpty(data) || data == "unknown" || data.Length <= 4)
                return data;
                
            var prefix = data.Substring(0, 2);
            var maskLength = Math.Max(data.Length - 4, 0);
            var mask = new string('*', maskLength);
            var suffix = data.Substring(data.Length - 2);
            
            return $"{prefix}{mask}{suffix}";
        }
        
        /// <summary>
        /// Gets the required scopes for the current HTTP context.
        /// </summary>
        /// <param name="context">The HTTP context.</param>
        /// <returns>A comma-separated string of required scopes.</returns>
        private string GetRequiredScopes(HttpContext context)
        {
            // Try to get the required scopes from the response context
            if (context.Items.TryGetValue("RequiredScopes", out var requiredScopesObj) && 
                requiredScopesObj is string[] requiredScopesArray)
            {
                return string.Join(", ", requiredScopesArray);
            }
            
            return string.Empty;
        }
        
        /// <summary>
        /// Adds properties to telemetry data.
        /// </summary>
        /// <param name="telemetry">The telemetry data.</param>
        /// <param name="data">The dictionary containing properties to add.</param>
        private void AddPropertiesToTelemetry(ISupportProperties telemetry, Dictionary<string, object> data)
        {
            // Helper function to add properties to telemetry events
            // Application Insights only accepts strings as property values
            foreach (var entry in FlattenDictionary(data))
            {
                if (entry.Value != null)
                {
                    telemetry.Properties[entry.Key] = entry.Value;
                }
            }
        }
        
        /// <summary>
        /// Flattens a nested dictionary into a single-level dictionary.
        /// </summary>
        /// <param name="nestedDict">The nested dictionary.</param>
        /// <param name="prefix">The prefix for keys in the flattened dictionary.</param>
        /// <returns>A single-level dictionary with flattened keys.</returns>
        private Dictionary<string, string> FlattenDictionary(Dictionary<string, object> nestedDict, string prefix = "")
        {
            var result = new Dictionary<string, string>();
            
            foreach (var entry in nestedDict)
            {
                var key = string.IsNullOrEmpty(prefix) ? entry.Key : $"{prefix}.{entry.Key}";
                
                if (entry.Value is Dictionary<string, object> nestedObject)
                {
                    var flattenedNested = FlattenDictionary(nestedObject, key);
                    foreach (var nested in flattenedNested)
                    {
                        result[nested.Key] = nested.Value;
                    }
                }
                else if (entry.Value is Dictionary<string, string> stringDict)
                {
                    foreach (var dictEntry in stringDict)
                    {
                        result[$"{key}.{dictEntry.Key}"] = dictEntry.Value;
                    }
                }
                else if (entry.Value is IEnumerable<string> stringList && !(entry.Value is string))
                {
                    result[key] = string.Join(",", stringList);
                }
                else
                {
                    result[key] = entry.Value?.ToString() ?? "null";
                }
            }
            
            return result;
        }
    }

    /// <summary>
    /// Extension methods for adding the AuditMiddleware to the application pipeline.
    /// </summary>
    public static class AuditMiddlewareExtensions
    {
        /// <summary>
        /// Adds the audit logging middleware to the application pipeline.
        /// </summary>
        /// <param name="builder">The application builder instance.</param>
        /// <returns>The application builder with audit logging middleware configured.</returns>
        public static IApplicationBuilder UseAuditLogging(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<AuditMiddleware>();
        }
    }
}