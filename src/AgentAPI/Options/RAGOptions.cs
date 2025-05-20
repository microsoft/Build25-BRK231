// Copyright (c) Microsoft Corporation. 
// Licensed under the MIT license.

namespace MyOpenAIWebApi.Options;

public class RAGOptions
{
    /// <summary>
    /// Path to the OpenAPI YAML file with API documentation for RAG
    /// </summary>
    public string ApiDocsFilePath { get; set; } = "OpenAPI.yaml";

    /// <summary>
    /// Name to use when uploading the file to OpenAI
    /// </summary>
    public string ApiDocsFileName { get; set; } = "WoodgroveAPIDocumentation.json";

    /// <summary>
    /// Vector store name for API documentation
    /// </summary>
    public string VectorStoreName { get; set; } = "WoodgroveAPIDocumentation";
    
    /// <summary>
    /// Maximum tokens per chunk for the chunking strategy
    /// </summary>
    public int MaxTokensPerChunk { get; set; } = 1500;
    
    /// <summary>
    /// Overlapping token count for the chunking strategy
    /// </summary>
    public int OverlappingTokenCount { get; set; } = 250;
    
    /// <summary>
    /// Indicates whether the API documentation file is in OpenAPI/Swagger format
    /// </summary>
    public bool UseOpenApiFormat { get; set; } = true;
    
    /// <summary>
    /// Base URL to prepend to API paths from the OpenAPI spec
    /// </summary>
    public string ApiBaseUrl { get; set; } = "/api";
}