// Copyright (c) Microsoft Corporation. 
// Licensed under the MIT license.

using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.Options;
using OpenAI.Assistants;
using System.Runtime.CompilerServices;
using MyOpenAIWebApi.Options;
using System.ClientModel;
using System.Text.Json;   
using OpenAI.VectorStores;
using MyOpenAIWebApi.Helpers;
using OpenAI.Files;
using System.Text;
using System.Diagnostics;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace MyOpenAIWebApi.Services;

/// <summary>
/// Defines the interface for interacting with OpenAI assistants
/// </summary>
public interface IOpenAIService
{
    /// <summary>
    /// Initializes the assistant with necessary configurations
    /// </summary>
    Task InitializeAssistantAsync();
    
    /// <summary>
    /// Sends a user message to the assistant and streams the response
    /// </summary>
    /// <param name="userMessage">The message from the user</param>
    /// <param name="cancellationToken">Cancellation token for the operation</param>
    /// <returns>An asynchronous stream of response text chunks</returns>
    IAsyncEnumerable<string> SendMessageAndStreamAsync(string userMessage, CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Resets the current conversation thread
    /// </summary>
    Task ResetThreadAsync();
    
    /// <summary>
    /// Deletes the current conversation thread
    /// </summary>
    Task DeleteThreadAsync();
    
    /// <summary>
    /// Updates the user token used for authentication
    /// </summary>
    /// <param name="userToken">The new user token</param>
    void UpdateUserToken(string userToken);
}

/// <summary>
/// Implementation of the OpenAI service using Azure OpenAI
/// </summary>
public class OpenAIService : IOpenAIService
{
    /// <summary>
    /// Client for interacting with Azure OpenAI services
    /// </summary>
    private readonly AzureOpenAIClient _azureAIClient;
    
    /// <summary>
    /// Client for interacting with the OpenAI Assistants API
    /// </summary>
    private readonly AssistantClient _assistantClient;
    
    /// <summary>
    /// Client for interacting with vector stores
    /// </summary>
    private readonly VectorStoreClient _vectorStoreClient;
    
    /// <summary>
    /// Client for managing OpenAI files
    /// </summary>
    private readonly OpenAIFileClient _fileClient;
    
    /// <summary>
    /// Helper for executing custom API requests
    /// </summary>
    private readonly CustomAPIHelper _customAPIHelper;
    
    /// <summary>
    /// Options for custom API configuration
    /// </summary>
    private readonly CustomAPIOptions _customAPIOptions;
    
    /// <summary>
    /// Options for RAG (Retrieval Augmented Generation) configuration
    /// </summary>
    private readonly RAGOptions _ragOptions;
    
    /// <summary>
    /// The GPT model to use for the assistant
    /// </summary>
    private readonly string _gptModel;
    
    /// <summary>
    /// The user's authentication token
    /// </summary>
    private string _userToken; 
    
    /// <summary>
    /// The current OpenAI assistant instance
    /// </summary>
    private Assistant? _currentAssistant;
    
    /// <summary>
    /// The current conversation thread
    /// </summary>
    private AssistantThread? _currentThread;
    
    /// <summary>
    /// The ID of the vector store used for RAG
    /// </summary>
    private string? _vectorStoreId;    /// <summary>
    /// System prompt for the assistant that defines its behavior, capabilities, and rules.
    /// Contains detailed instructions for handling recipe suggestions, shopping cart operations,
    /// user personalization, and API interaction.
    /// </summary>
    private string CustomPrompt02 => $$"""
    You are an assistant that helps users prepare recipes according to their preferences 
    and manage a shopping cart via a custom OData API.

    Follow these rules:

    1. User Personalization
    - Every time you receive a message from the user call the tool {{getUserInfoFunctionName}} to get the user information.
    - When the chat starts, greet the user by their name from the "Display Name" claim (e.g., "Hello Fabian, how can I help you today?").
    - Always check if the user has the "ProductManagerRole" and then:
        - let the user know that he is a product manager so he can do CRUD operations on the products
        - focus on helping the user of doing CRUD operations on products instead of preparing recipies or adding products to the shopping cart.
    - if the user does not have the "ProductManagerRole" then:
        - focus on helping the user in preparing recipies or adding products to the shopping cart, checkout and pay.
    - Use the user's dietary restrictions from the "DietaryRestrictions" claim when recommending recipes (e.g., suggesting vegan recipes if "DietaryRestrictions" is "Vegan").
    - Use the user's DateOfBirth to calculate the user age based on the current date obtained with the tool {{dateTimeFunctionName}}.
    - For MFA status, check the "acrs" claim value - if its value equals "c1", MFA is completed.

    2. Scope  
    - You should only help the customer:
        a. Find or propose a recipe for a meal, taking into account dietary restrictions from their claims.
        b. Search for existing products and add the required ingredients to the user's shopping cart if they fit the recipe and the user's criteria.
        c. Perform any necessary operations described in the WoodgroveGroceries API Documentation. 
        d. Provide a summary of the cart contents and the total cost of the items in the cart.

    3. Interpretation & Planning  
    - Interpret the user's request for recipes (including any dietary restrictions from their claims).
    - For searching products, ALWAYS use the dedicated search endpoint '/products/search?query=term' instead of OData filters when the user is looking for products by name, category or general terms.
    - Only use OData filters (like $filter) for very specific attribute filtering that cannot be accomplished with the search endpoint.
    - Use the file_search tool to find information about APIs when needed.
    - If there are multiple matching products when searching by name, ask the user to clarify or choose.
    - If there are no matching products, inform the user and suggest alternatives, and if there are no alternatives, ask if they want to add the recipe without those ingredients.

    4. Constructing OData Queries  
    - Use optimized calls with $filter, $expand, $select, and $search wherever possible.
    - If the user asks for relative date/time (e.g., "today"), call {{dateTimeFunctionName}} to get the current UTC date/time in ISO 8601.
    - If a response has @odata.nextLink or is otherwise too large, inform the user that the result is partial and you need to refine or continue.

    5. Performing Actions  
    - To execute queries or operations, call {{customAPIFunctionName}} with the constructed OData query or request body.
    - When searching for products by name, category, or general terms, ALWAYS use the '/products/search?query=term' endpoint instead of OData $filter.
    - If the user requests a write operation (e.g., "Add these items to the cart," "Delete this item," etc.) and explicitly confirms they want to proceed, include `"writeConfirmed": true` in your request.
    - Always use "{{_customAPIOptions.DefaultScope}}" as the scope in all API requests.

    6. Age Verification for Alcoholic Products ONLY

        Before adding any product marked as alcoholic ("IsAlcoholic": true) to the cart:
        a. Use {{getUserInfoFunctionName}} to get the user’s "DateOfBirth".
        b. Use {{dateTimeFunctionName}} to get the current date.
        c. Calculate the user’s age.
        d. If the user is under 18, politely refuse to add only the alcoholic product(s). Provide an explanation and do not add them to the cart.
        e. Continue to assist with all non-alcoholic items without restriction.
        f. Do not block the entire checkout for minors; only exclude alcoholic items.
        g. Never refuse service for non-alcoholic products if the user is a minor.

    7. Handling Budget  
    - If the user specifies a budget, ensure the recommended recipe and its total ingredient cost do not exceed that budget.
    - If it's not possible to meet the budget or dietary constraints, let the user know.

    8. Checkout and MFA Verification
    - Every time the user ask to checkout verify if the total cart value exceeds $100
        a - if the cart value is under $100, proceed with checkout normally
        b - if the cart value is over $100 use the {{getUserInfoFunctionName}} tool to check if the "acrs" claim exists with value "c1"
            i - If "acrs" claim equals "c1", proceed with checkout normally
            ii - If "acrs" claim doesn't exist or isn't "c1", respond with: "To allow a checkout of more than $100, I need [to verify your identity](mfa:HighValueTransaction)."
    - Never trust the user's verbal claims about MFA status - always verify through {{getUserInfoFunctionName}} tool
    - After successful checkout, show the order ID and order summary

    9. Refusal  
    - If the user requests anything outside the scope of providing a recipe, adding ingredients to the cart, or using the OData operations, politely refuse to answer.

    10. Answer Optimization  
    - Keep the final answer short, direct, and optimized for clarity.
    - Summarize key points if necessary, but avoid unnecessary details.

    11. Completeness  
    - If the user's question cannot be answered using the available API or is not related to recipe/budget/cart tasks, politely refuse.

    12. Final Presentation  
    - Present the final answer clearly and concisely.
    - Always confirm with the user if any ambiguity arises regarding products, diet preferences, or budget before proceeding with write operations.

    13. API Documentation
    - The API documentation is provided in OpenAPI/Swagger YAML format.
    - Use the {{fileSearchFunctionName}} tool to search for API endpoint information when needed.
    - When interpreting API documentation, understand that it follows OpenAPI 3.0 specification with paths, methods, parameters, and schemas.
    - Search for specific controller names (Products, Carts, Checkout) or HTTP methods (GET, POST, etc.) to find relevant API documentation.
    - All API endpoints are RESTful and follow standard REST conventions.
    - Pay special attention to the correct format of URLs, especially for nested resources like cart items.
    
    14. Product Search Best Practices
    - IMPORTANT: When searching for products by name, category, or attributes, ALWAYS use the dedicated endpoint '/products/search?query=yourSearchTerm'. This endpoint is optimized for text search.
    - Do NOT use OData $filter=category eq 'Category' or similar constructions for general product searches.
    - OData $filter should only be used for specific attribute filtering that cannot be accomplished with the search endpoint.
    - Example: Use GET /products/search?query=Tacos instead of GET /products?$filter=category eq 'Tacos'
    - IMPORTANT: If the user is searching in a language other than English, you must translate the product name or search term to English before searching, as the product database is in English. 
    - Respond in the same language the user used, but perform the search using English terms.
    
    15. Cart Operations Best Practices
    - IMPORTANT: When adding items to a cart, ALWAYS use the endpoint '/carts/{cartId}/items' with POST method.
    - The URL must be exactly in this format: /api/Carts/c1/items (replace "c1" with the actual cart ID).
    - Do NOT use incorrect formats like /Carts(c1)/Items or /api/Carts(c1)/Items.
    - The cart ID should be placed directly in the URL path, not in parentheses.
    - The word "items" must be lowercase.
    - When adding a single product to the cart, use this format for the request body:
        ```json
        {
        "ProductId": "p123",
        "Quantity": 1
        }
        ```
    - Do NOT try to add multiple items in a single request with arrays like {"items":[...]}.
    - To add multiple products, make separate POST requests for each product.
    - Example of correct request to add an item to cart c1:
        ```
        POST /api/Carts/c1/items
        Content-Type: application/json
        
        {
        "ProductId": "p020",
        "Quantity": 2
        }
        ```
        
    16. Checkout Best Practices
    - IMPORTANT: To checkout a cart, use the endpoint '/checkout' with POST method, NOT '/carts/{cartId}/checkout'.
    - The URL must be exactly in this format: /api/Checkout
    - The checkout process requires a request body in this format:
        ```json
        {
        "CartId": "c1",
        "Address": "123 Main St, Anytown, USA"
        }
        ```
    - After successful checkout, you'll receive a checkout response with the orderId.
    - To make a payment for an order, use the endpoint '/checkout/{orderId}/pay' with POST method in this format:
        ```
        POST /api/Checkout/order123/pay
        Content-Type: application/json
        
        {
        "PaymentMethod": "CreditCard",
        "CardNumber": "4242424242424242",
        "ExpirationDate": "12/25",
        "Cvv": "123"
        }
        ```
    - NEVER try to checkout directly from a cart using '/carts/{cartId}/checkout' as this endpoint does not exist.
    - Example of correct checkout flow:
        1. POST to /api/Checkout with the CartId
        2. Receive an orderId in the response
        3. POST to /api/Checkout/{orderId}/pay to complete payment
    """;

    /// <summary>
    /// JSON serialization options for consistent serialization and deserialization
    /// </summary>
    private readonly JsonSerializerOptions _serializerOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };
    
    /// <summary>
    /// Name of the custom API request function used by the assistant
    /// </summary>
    private const string customAPIFunctionName = "custom_api_request";
    
    /// <summary>
    /// Name of the file search function used by the assistant
    /// </summary>
    private const string fileSearchFunctionName = "file_search";
    
    /// <summary>
    /// Name of the date/time function used by the assistant
    /// </summary>
    private const string dateTimeFunctionName = "get_dateTime";
    
    /// <summary>
    /// Name of the user info function used by the assistant
    /// </summary>
    private const string getUserInfoFunctionName = "get_user_info";



    /// <summary>
    /// Initializes a new instance of the <see cref="OpenAIService"/> class.
    /// </summary>
    /// <param name="optionsAccessor">The accessor for OpenAI configuration options</param>
    /// <param name="customAPIHelper">Helper for executing custom API requests</param>
    /// <param name="customAPIOptions">The accessor for custom API configuration options</param>
    /// <param name="ragOptionsAccessor">The accessor for RAG configuration options</param>
    /// <param name="userToken">The user's authentication token</param>
    public OpenAIService(
        IOptions<OpenAIOptions> optionsAccessor,
        CustomAPIHelper customAPIHelper,
        IOptions<CustomAPIOptions> customAPIOptions,
        IOptions<RAGOptions> ragOptionsAccessor,
        string userToken
        )
    {
        var options = optionsAccessor.Value;
        _customAPIOptions = customAPIOptions.Value;
        _ragOptions = ragOptionsAccessor.Value;
        var endpoint = new Uri(options.Endpoint);

        // Initialize the Azure OpenAI client with appropriate authentication
        if (options.UseKeyAuth)
            _azureAIClient = new AzureOpenAIClient(endpoint, new ApiKeyCredential(options.Key));
        else
            _azureAIClient = new AzureOpenAIClient(endpoint, new DefaultAzureCredential());

        _assistantClient = _azureAIClient.GetAssistantClient();
        _vectorStoreClient = _azureAIClient.GetVectorStoreClient();
        _fileClient = _azureAIClient.GetOpenAIFileClient();
        _gptModel = string.IsNullOrEmpty(options.Model) ? "gpt-4o-mini" : options.Model;
        _userToken = userToken;
        _customAPIHelper = customAPIHelper;    }

    /// <summary>
    /// Initializes the assistant by preparing RAG resources and configuring tools.
    /// This method is called from the InMemoryAssistantManager when creating a new instance.
    /// </summary>
    /// <returns>A task representing the asynchronous initialization operation.</returns>
    public async Task InitializeAssistantAsync()
    {
        // 1. Prepare RAG resources first
        var vectorStore = await GetVectorStoreAsync();
        _vectorStoreId = vectorStore.Id;
        
        var fileSearchToolResources = new FileSearchToolResources();
        fileSearchToolResources.VectorStoreIds.Add(_vectorStoreId);

        // 🔍 DEMO POINT: AGENT INSTANTIATION
        AssistantCreationOptions options = new AssistantCreationOptions
        {
            Name = "Woodgrove Assistant",
            Instructions = CustomPrompt02,
            ToolResources = new() { FileSearch = fileSearchToolResources }
        };

        // 🔍 DEMO POINT: API TOOL
        FunctionToolDefinition CustomAPITool = new()
        {
            FunctionName = customAPIFunctionName,
            Description = "Custom API Request",
            Parameters = BinaryData.FromObjectAsJson(new
            {
                Type = "object",
                Properties = new
                {
                    Method = new    
                    {
                        Type = "string",
                        Description = "HTTP Method for the API request",
                        Enum = new[] { "GET", "POST", "PATCH", "PUT", "DELETE" },
                    },
                    Uri = new
                    {
                        Type = "string",
                        Description = "API Request Path and eventual Query Parameters",
                    },
                    Body = new
                    {
                        Type = "string",
                        Description = "JSON Body of POST, PATCH, or PUT request (do not use if method is GET or DELETE)",
                    },
                    WriteConfirmed = new
                    {
                        Type = "boolean",
                        Description = "Set to True if the user EXPLICITLY confirmed the write operation, otherwise explain all the request parameters to the user and ask to confirm (ignored if method is GET)",
                    },
                    Scopes = new
                    {
                        Type = "array",
                        Items = new { Type = "string" },
                        Description = "API scopes required for executing this API request"
                    }
                },
                Required = new[] { "method", "uri", "writeConfirmed", "scopes" },
            }, _serializerOptions)
        };

        // 🔍 DEMO POINT: TIME TOOL
        // Get the current UTC Date and Time in ISO 8601
        FunctionToolDefinition GetDateTimeTool = new()
        {
            FunctionName = dateTimeFunctionName,
            Description = "Get the current UTC Date and Time in ISO 8601",
        };
        
        // 🔍 DEMO POINT: TOKEN READ TOOL
        //Get claims from the Access Token
        FunctionToolDefinition GetUserInfoTool = new()
        {
            FunctionName = getUserInfoFunctionName,
            Description = "Get user information from the access token, including identity claims, roles, and authentication status",
            Parameters = BinaryData.FromObjectAsJson(new
            {
                Type = "object",
                Properties = new { },
                Required = Array.Empty<string>()
            }, _serializerOptions)
        };
        
        // 🔍 DEMO POINT: ADDS TOOLS TO OPTIONS
        // Initialize the read-only Tools property separately
        options.Tools.Add(CustomAPITool);
        options.Tools.Add(GetDateTimeTool);
        options.Tools.Add(GetUserInfoTool);  

        // 🔍 DEMO POINT: FILE SEARCH TOOL
        options.Tools.Add(ToolDefinition.CreateFileSearch(5));

        // 🔍 DEMO POINT: CREATES AGENT WITH OPTIONS
        // Create the asistnt using options
        _currentAssistant = await _assistantClient.CreateAssistantAsync(_gptModel, options);

        // Creates a thread
        _currentThread = await _assistantClient.CreateThreadAsync();
    }

    /// <summary>
    /// Gets or creates a vector store for API documentation
    /// <returns>The vector store</returns>
    /// </summary>
    private async Task<VectorStore> GetVectorStoreAsync()
    {
        try
        {
            // Check if we have existing vector stores with our name
            var vectorStores = await _vectorStoreClient.GetVectorStoresAsync()
                .Where(vs => vs.Name == _ragOptions.VectorStoreName)
                .ToListAsync();
                
            if (vectorStores.Count > 0)
            {
                // If we found an existing store, return the most recently created one
                var store = vectorStores.OrderByDescending(vs => vs.CreatedAt).First();
                
                // Check if the source markdown file is newer than the vector store
                // If so, we need to update it with the new content
                if (File.Exists(_ragOptions.ApiDocsFilePath) &&
                    File.GetLastWriteTimeUtc(_ragOptions.ApiDocsFilePath) > store.CreatedAt)
                {
                    await UpdateVectorStoreAsync(store);
                }
                
                return store;
            }
            else
            {
                // Create a new vector store
                var ragFile = await GetApiDocsFileAsync();
                
                // 🔍 DEMO POINT: TOKENS PER CHUNK + OVERLAPP FOR RAG
                var chunkingStrategy = FileChunkingStrategy.CreateStaticStrategy(
                    maxTokensPerChunk: _ragOptions.MaxTokensPerChunk,
                    overlappingTokenCount: _ragOptions.OverlappingTokenCount);
                
                var creationOptions = new VectorStoreCreationOptions() 
                { 
                    Name = _ragOptions.VectorStoreName, 
                    ChunkingStrategy = chunkingStrategy 
                };
                
                creationOptions.FileIds.Add(ragFile.Id);
                var createOperation = await _vectorStoreClient.CreateVectorStoreAsync(true, creationOptions);
                await createOperation.WaitForCompletionAsync();
                
                return createOperation.Value!;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error creating vector store: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Updates a vector store with new content from the API docs file
    /// </summary>
    /// <param name="store">The vector store to update</param>
    private async Task UpdateVectorStoreAsync(VectorStore store)
    {
        try
        {
            // Get the current file associations for this vector store
            var fileAssociations = await _vectorStoreClient.GetFileAssociationsAsync(store.Id).ToListAsync();
            
            // Upload the new file
            var newRagFile = await GetApiDocsFileAsync(forceUpload: true);
            
            // Add the new file to the vector store
            await _vectorStoreClient.AddFileToVectorStoreAsync(store.Id, newRagFile.Id, true);
            
            // Remove the old files and delete them
            foreach (var association in fileAssociations)
            {
                await Task.WhenAll(
                    _vectorStoreClient.RemoveFileFromStoreAsync(store.Id, association.FileId),
                    _fileClient.DeleteFileAsync(association.FileId));
            }
            
            Debug.WriteLine($"Updated vector store {store.Id} with new content from {_ragOptions.ApiDocsFilePath}");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error updating vector store: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Gets or creates the API documentation file
    /// </summary>
    /// <param name="forceUpload">Force upload even if file exists</param>
    /// <returns>The uploaded file</returns>
    private async Task<OpenAIFile> GetApiDocsFileAsync(bool forceUpload = false)
    {
        try
        {
            OpenAIFile? ragFile = null;
            
            if (!forceUpload)
            {
                // Check if we have an existing file with our name
                OpenAIFileCollection assistantFiles = await _fileClient.GetFilesAsync(FilePurpose.Assistants);
                ragFile = assistantFiles
                    .Where(f => f.Filename == _ragOptions.ApiDocsFileName)
                    .OrderByDescending(f => f.CreatedAt)
                    .FirstOrDefault();
            }
            
            if (ragFile == null || forceUpload)
            {
                // Process and upload the OpenAPI file
                var document = ProcessOpenApiFile(_ragOptions.ApiDocsFilePath);
                
                ragFile = await _fileClient.UploadFileAsync(
                    document,
                    _ragOptions.ApiDocsFileName,
                    FileUploadPurpose.Assistants);
                
                Debug.WriteLine($"Uploaded API docs file with ID: {ragFile.Id}");
            }
            
            return ragFile;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error getting API docs file: {ex.Message}");
            throw;
        }
    }
    
    /// <summary>
    /// Process an OpenAPI YAML file into structured JSON for RAG
    /// </summary>
    /// <param name="filePath">Path to the OpenAPI YAML file</param>
    /// <returns>Stream containing the structured API documentation in JSON format</returns>
    private Stream ProcessOpenApiFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"OpenAPI file not found: {filePath}");
        }   
        
        // Read the YAML content
        var yamlContent = File.ReadAllText(filePath);
        
        // Parse the YAML content
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();
            
        var yamlObject = deserializer.Deserialize<Dictionary<object, object>>(yamlContent);
        
        // Extract API information
        var apiInfo = yamlObject.ContainsKey("info") ? yamlObject["info"] as Dictionary<object, object> : null;
        var apiTitle = apiInfo != null && apiInfo.ContainsKey("title") ? apiInfo["title"]?.ToString() : "API Documentation";
        var apiDescription = apiInfo != null && apiInfo.ContainsKey("description") ? apiInfo["description"]?.ToString() : "";
        
        // Extract paths
        var paths = yamlObject.ContainsKey("paths") ? yamlObject["paths"] as Dictionary<object, object> : null;
        
        // Create structured endpoints from paths
        var endpoints = new List<Dictionary<string, object>>();
        
        if (paths != null)
        {
            foreach (var pathEntry in paths)
            {
                var pathKey = pathEntry.Key.ToString();
                var pathData = pathEntry.Value as Dictionary<object, object>;
                
                if (pathData != null)
                {
                    foreach (var methodEntry in pathData)
                    {
                        var httpMethod = methodEntry.Key.ToString().ToUpperInvariant();
                        var methodData = methodEntry.Value as Dictionary<object, object>;
                        
                        if (methodData != null)
                        {
                            var endpoint = new Dictionary<string, object>
                            {
                                ["path"] = _ragOptions.ApiBaseUrl + pathKey,
                                ["method"] = httpMethod,
                                ["summary"] = methodData.ContainsKey("summary") ? methodData["summary"]?.ToString() : "",
                                ["description"] = methodData.ContainsKey("description") ? methodData["description"]?.ToString() : "",
                                ["operationId"] = methodData.ContainsKey("operationId") ? methodData["operationId"]?.ToString() : ""
                            };
                            
                            // Extract parameters
                            if (methodData.ContainsKey("parameters") && methodData["parameters"] is List<object> parameters)
                            {
                                var paramList = new List<Dictionary<string, string>>();
                                foreach (Dictionary<object, object> param in parameters)
                                {
                                    var paramInfo = new Dictionary<string, string>
                                    {
                                        ["name"] = param.ContainsKey("name") ? param["name"]?.ToString() : "",
                                        ["in"] = param.ContainsKey("in") ? param["in"]?.ToString() : "",
                                        ["required"] = param.ContainsKey("required") ? param["required"]?.ToString() : "false",
                                        ["description"] = param.ContainsKey("description") ? param["description"]?.ToString() : ""
                                    };
                                    
                                    paramList.Add(paramInfo);
                                }
                                
                                endpoint["parameters"] = paramList;
                            }
                            
                            // Extract request body
                            if (methodData.ContainsKey("requestBody") && methodData["requestBody"] is Dictionary<object, object> requestBody)
                            {
                                var requestInfo = new Dictionary<string, object>
                                {
                                    ["required"] = requestBody.ContainsKey("required") ? requestBody["required"]?.ToString() : "false"
                                };
                                
                                if (requestBody.ContainsKey("content") && requestBody["content"] is Dictionary<object, object> content)
                                {
                                    if (content.ContainsKey("application/json") && 
                                        content["application/json"] is Dictionary<object, object> requestJsonContent &&
                                        requestJsonContent.ContainsKey("schema") && 
                                        requestJsonContent["schema"] is Dictionary<object, object> schema)
                                    {
                                        if (schema.ContainsKey("$ref"))
                                        {
                                            requestInfo["schemaRef"] = schema["$ref"]?.ToString();
                                        }
                                    }
                                }
                                
                                endpoint["requestBody"] = requestInfo;
                            }
                            
                            // Extract responses
                            if (methodData.ContainsKey("responses") && methodData["responses"] is Dictionary<object, object> responses)
                            {
                                var responseList = new List<Dictionary<string, string>>();
                                foreach (var response in responses)
                                {
                                    var responseCode = response.Key.ToString();
                                    var responseData = response.Value as Dictionary<object, object>;
                                    
                                    if (responseData != null)
                                    {
                                        var responseInfo = new Dictionary<string, string>
                                        {
                                            ["code"] = responseCode,
                                            ["description"] = responseData.ContainsKey("description") ? responseData["description"]?.ToString() : ""
                                        };
                                        
                                        responseList.Add(responseInfo);
                                    }
                                }
                                
                                endpoint["responses"] = responseList;
                            }
                            
                            endpoints.Add(endpoint);
                        }
                    }
                }
            }
        }
        
        // Extract components/schemas to document data models
        var schemas = new Dictionary<string, object>();
        if (yamlObject.ContainsKey("components") && 
            yamlObject["components"] is Dictionary<object, object> components &&
            components.ContainsKey("schemas") && 
            components["schemas"] is Dictionary<object, object> schemasDefs)
        {
            foreach (var schemaDef in schemasDefs)
            {
                var schemaName = schemaDef.Key.ToString();
                var schemaData = schemaDef.Value as Dictionary<object, object>;
                
                if (schemaData != null)
                {
                    schemas[schemaName] = ProcessSchema(schemaData);
                }
            }
        }
        
        // Create the final API documentation structure
        var apiDocumentation = new Dictionary<string, object>
        {
            ["title"] = apiTitle,
            ["description"] = apiDescription,
            ["baseUrl"] = _ragOptions.ApiBaseUrl,
            ["endpoints"] = endpoints,
            ["schemas"] = schemas
        };
        
        // Serialize to JSON
        var jsonContent = JsonSerializer.Serialize(apiDocumentation, _serializerOptions);
        Debug.WriteLine($"Processed OpenAPI documentation with {endpoints.Count} endpoints");
        
        return new MemoryStream(Encoding.UTF8.GetBytes(jsonContent));
    }
    
    /// <summary>
    /// Processes a schema definition from OpenAPI specification into a structured dictionary.
    /// </summary>
    /// <param name="schemaData">The schema data from the OpenAPI specification</param>
    /// <returns>A structured dictionary representation of the schema</returns>
    private Dictionary<string, object> ProcessSchema(Dictionary<object, object> schemaData)
    {
        var result = new Dictionary<string, object>();

        if (schemaData.ContainsKey("type"))
            result["type"] = schemaData["type"]?.ToString();

        if (schemaData.ContainsKey("description"))
            result["description"] = schemaData["description"]?.ToString();

        if (schemaData.ContainsKey("required") && schemaData["required"] is List<object> requiredProps)
            result["required"] = requiredProps.Select(p => p?.ToString()).ToList();

        if (schemaData.ContainsKey("properties") && schemaData["properties"] is Dictionary<object, object> props)
        {
            var properties = new Dictionary<string, Dictionary<string, string>>();

            foreach (var prop in props)
            {
                var propName = prop.Key.ToString();
                var propData = prop.Value as Dictionary<object, object>;

                if (propData != null)
                {
                    var propInfo = new Dictionary<string, string>
                    {
                        ["type"] = propData.ContainsKey("type") ? propData["type"]?.ToString() : "string"
                    };

                    if (propData.ContainsKey("description"))
                        propInfo["description"] = propData["description"]?.ToString();

                    if (propData.ContainsKey("format"))
                        propInfo["format"] = propData["format"]?.ToString();

                    properties[propName] = propInfo;
                }
            }

            result["properties"] = properties;
        }

        return result;
    }

    /// <summary>
    /// Resets the current thread by deleting it and creating a new one.
    /// This effectively starts a new conversation while maintaining the same assistant.
    /// </summary>
    /// <returns>A task representing the asynchronous reset operation.</returns>
    public async Task ResetThreadAsync()
    {
        // Delete the previous thread in Azure
        if (_currentThread is not null)
            await _assistantClient.DeleteThreadAsync(_currentThread.Id);

        // Create a new one
        _currentThread = await _assistantClient.CreateThreadAsync();
    }

    /// <summary>
    /// Deletes the current thread from Azure.
    /// This removes all conversation history associated with the current thread.
    /// </summary>
    /// <returns>A task representing the asynchronous delete operation.</returns>
    public async Task DeleteThreadAsync()
    {
        if (_currentThread is not null)
            await _assistantClient.DeleteThreadAsync(_currentThread.Id);    }

    /// <summary>
    /// Sends a user message to the assistant and streams the response as it's generated.
    /// </summary>
    /// <param name="userMessage">The message from the user to send to the assistant</param>
    /// <param name="cancellationToken">Optional token for cancelling the operation</param>
    /// <returns>An asynchronous stream of response text chunks from the assistant</returns>
    /// <exception cref="InvalidOperationException">Thrown if the assistant or thread is not initialized</exception>
    public async IAsyncEnumerable<string> SendMessageAndStreamAsync(
        string userMessage,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        if (_currentAssistant is null || _currentThread is null)
            throw new InvalidOperationException("Assistant o Thread no inicializado.");

        // Extract user information from token for logging purposes
        string userName = "Usuario";
        string userId = "anonymous";
        
        // Try to get user information from the token
        var userClaims = TokenClaimsHelper.GetTokenClaims(_userToken);
        if (userClaims != null && userClaims.ContainsKey("name"))
        {
            userName = userClaims["name"].ToString() ?? "Usuario";
        }
        if (userClaims != null && userClaims.ContainsKey("sub") || userClaims.ContainsKey("oid"))
        {
            userId = userClaims.ContainsKey("sub") ? userClaims["sub"].ToString() : userClaims["oid"].ToString();        }

        //Create the user message in the thread
        List<MessageContent> messageContents = [userMessage];
        await _assistantClient.CreateMessageAsync(
            _currentThread.Id, 
            MessageRole.User, 
            messageContents, 
            new MessageCreationOptions(), 
            cancellationToken);

        //tart the streaming response
        var runOptions = new RunCreationOptions();
        var asyncUpdates = _assistantClient.CreateRunStreamingAsync(
            _currentThread.Id, 
            _currentAssistant.Id, 
            runOptions, 
            cancellationToken);

        ThreadRun? currentRun = null;
        bool errorOccurred = false;
        string errorMessage = string.Empty;
        StringBuilder fullAssistantResponse = new StringBuilder();

        do
        {
            var outputsToSubmit = new List<ToolOutput>();

            await foreach (var update in asyncUpdates.WithCancellation(cancellationToken))
            {
                switch (update)
                {
                    case RequiredActionUpdate requiredActionUpdate:
                        {
                            // Process tool call requests from the assistant
                            var toolOutput = await GetResolvedToolOutput(
                                requiredActionUpdate.ToolCallId,
                                requiredActionUpdate.FunctionName,
                                requiredActionUpdate.FunctionArguments
                            );
                            outputsToSubmit.Add(toolOutput);
                            break;
                        }
                    case MessageContentUpdate contentUpdate:
                        {
                            // Stream the text as-is - it already comes in markdown format from the model
                            var partialText = contentUpdate.Text;
                            fullAssistantResponse.Append(partialText);
                            yield return partialText;
                            break;
                        }
                    case RunUpdate runUpdate:
                        {
                            currentRun = runUpdate.Value;

                            // If the run failed, report error with markdown formatting
                            if (runUpdate.UpdateKind == StreamingUpdateReason.RunFailed && runUpdate.Value.LastError != null)
                            {
                                var errorText = $"\n❌ **Error:** {runUpdate.Value.LastError.Message}";
                                fullAssistantResponse.Append(errorText);
                                yield return errorText;
                            }
                            break;
                        }
                }
            }

            // If we have tool outputs to submit AND we have a valid run that hasn't completed,
            // submit the outputs and continue streaming
            if (currentRun != null && outputsToSubmit.Count > 0 && !currentRun.Status.IsTerminal)
            {
                try
                {
                    asyncUpdates = _assistantClient.SubmitToolOutputsToRunStreamingAsync(
                        _currentThread.Id,
                        currentRun.Id,
                        outputsToSubmit,
                        cancellationToken
                    );
                }
                catch (Exception ex)
                {
                    // Capture the error to handle it outside the loop
                    errorOccurred = true;
                    errorMessage = $"\n❌ **Error al procesar herramientas:** {ex.Message}";
                    fullAssistantResponse.Append(errorMessage);
                    
                    await TryCancelRunAsync(_currentThread.Id, currentRun.Id);
                    break; // Exit the do-while loop
                }
            }

        } while (currentRun?.Status.IsTerminal == false && !errorOccurred);

        // Return the error message after exiting the loop, if necessary
        if (errorOccurred)
        {
            yield return errorMessage;
        }
    }

    /// <summary>
    /// Helper method to cancel a run without throwing exceptions.
    /// </summary>
    /// <param name="threadId">The ID of the thread containing the run</param>
    /// <param name="runId">The ID of the run to cancel</param>
    /// <returns>A task representing the asynchronous cancel operation</returns>
    private async Task TryCancelRunAsync(string threadId, string runId)
    {
        try
        {
            await _assistantClient.CancelRunAsync(threadId, runId);
        }
        catch
        {
            // Ignore errors in cancellation
        }    }

    /// <summary>
    /// Processes a tool call from the assistant and returns the appropriate output.
    /// </summary>
    /// <param name="toolCallId">The ID of the tool call</param>
    /// <param name="functionName">The name of the function to execute</param>
    /// <param name="functionArguments">The arguments for the function, as a JSON string</param>
    /// <returns>The output from the tool execution</returns>
    private async Task<ToolOutput> GetResolvedToolOutput(string toolCallId, string functionName, string functionArguments)
    {
        try
        {
            switch (functionName)
            {
                // 🔍 DEMO POINT: READ OUTPUT FROM API CALL 
                case customAPIFunctionName:
                    {
                        var requestParameters = JsonSerializer.Deserialize<CustomAPIParameters>(functionArguments, _serializerOptions);
                        
                        // Ensure API options are configured
                        if (requestParameters != null)
                        {
                            // Create a new object with complete API options
                            requestParameters = new CustomAPIParameters(
                                requestParameters.Method,
                                requestParameters.Uri,
                                requestParameters.Body,
                                requestParameters.WriteConfirmed,
                                requestParameters.Scopes,
                                _customAPIOptions // Pass the complete configuration object
                            );
                        }
                          var output = await _customAPIHelper.ExecuteQuery(requestParameters!, _userToken);
                        return new ToolOutput(toolCallId, output);
                    }
                case dateTimeFunctionName:
                    {
                        // Return the current UTC date and time in ISO 8601 format
                        var output = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
                        return new ToolOutput(toolCallId, output);
                    }
                case getUserInfoFunctionName:
                    {
                        // Get claims from the token and return them as JSON
                        var userClaims = TokenClaimsHelper.GetTokenClaims(_userToken);
                        var output = JsonSerializer.Serialize(userClaims, _serializerOptions);
                        return new ToolOutput(toolCallId, output);
                    }
                default:
                    // Return an error for unsupported tools
                    return new ToolOutput(toolCallId, JsonSerializer.Serialize(new {
                        error = new {
                            code = "UnsupportedTool",
                            message = $"The tool '{functionName}' is not supported."
                        }
                    }));
            }
        }
        catch (Exception ex)
        {
            // Captures any error and returns it as json
            var errorResponse = JsonSerializer.Serialize(new {
                error = new {
                    code = ex.GetType().Name,
                    message = ex.Message
                }
            });
            
            return new ToolOutput(toolCallId, errorResponse);
        }
    }

    /// <summary>
    /// Updates the user's access token in the existing service.
    /// This allows the service to make authenticated requests on behalf of the updated user identity.
    /// </summary>
    /// <param name="newToken">The new access token for the user</param>
    public void UpdateUserToken(string newToken)
    {
        // Update the internal token reference
        _userToken = newToken;
    }

}
