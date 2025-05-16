// Copyright (c) Microsoft Corporation. 
// Licensed under the MIT license.

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using MyOpenAIWebApi.Services;

namespace MyOpenAIWebApi.Hubs;

/// <summary>
/// SignalR hub for real-time communication with the AI assistant.
/// This hub enables bidirectional communication between clients and the server,
/// allowing for streaming responses from the AI assistant directly to the client.
/// </summary>
/// <remarks>
/// This hub requires authentication to ensure only authorized users can connect.
/// It handles connection management, message processing, and streaming of assistant
/// responses to connected clients.
/// </remarks>
[Authorize]
public class AssistantHub : Hub
{
    private readonly IAssistantManager _assistantManager;

    private string? _token;

    /// <summary>
    /// Initializes a new instance of the AssistantHub class.
    /// </summary>
    /// <param name="assistantManager">The assistant manager service that handles user-specific AI assistant instances</param>
    public AssistantHub(IAssistantManager assistantManager)
    {
        _assistantManager = assistantManager;
    }

    /// <summary>
    /// Handles the connection of a client to the hub.
    /// This method is automatically called when a client establishes a connection
    /// to the SignalR hub. It extracts the authentication token and performs
    /// initialization for the client connection.
    /// </summary>
    /// <returns>A task representing the asynchronous connection process</returns>
    public override async Task OnConnectedAsync()
    {
        // Get the token from the Authorization header
        string? token = Context.GetHttpContext()?.Request.Headers["Authorization"].FirstOrDefault()?.Split(" ").Last();

        // If not found in header, try query string (used for WebSocket connections)
        if (string.IsNullOrEmpty(token))
        {
            token = Context.GetHttpContext()?.Request.Query["access_token"];
        }
        
        // Store the token for later use with the assistant service
        // This will be null if no token was provided
        _token = token;
        
        await base.OnConnectedAsync();
    }
    
    /// <summary>
    /// Sends a message to the assistant and streams the response back to the client.
    /// This method processes the user's message, sends it to the appropriate AI assistant
    /// instance, and streams the response chunks back to the client in real-time.
    /// </summary>
    /// <param name="userMessage">The message text from the user to be processed by the assistant</param>
    /// <exception cref="HubException">Thrown when there's an error processing the message</exception>
    public async Task SendMessage(string userMessage)
    {
        try
        {
            // Get the current token each time a message is received
            string token = Context.GetHttpContext().Request.Headers["Authorization"].FirstOrDefault()?.Split(" ").Last();
            if (string.IsNullOrEmpty(token))
            {
                token = Context.GetHttpContext().Request.Query["access_token"];
            }
            
            // If there's still no token, try to use the one saved during connection
            if (string.IsNullOrEmpty(token))
            {
                token = _token;
            }
            else
            {
                // Update the stored token
                _token = token;
            }

            // Check if we have a valid token
            if (string.IsNullOrEmpty(token))
            {
                throw new Exception("Could not obtain a valid authentication token");
            }

            var openAIService = await _assistantManager.GetOrCreateOpenAIServiceForUserAsync(token);

            // Notify start of typing
            await Clients.Caller.SendAsync("ReceiveStartTyping");

            await foreach (var partialResponse in openAIService.SendMessageAndStreamAsync(userMessage))
            {
                await Clients.Caller.SendAsync("ReceivePartialResponse", partialResponse);
            }

            // Notify end of typing
            await Clients.Caller.SendAsync("ReceiveEndTyping");
        }        catch (Exception ex)
        {
            await Clients.Caller.SendAsync("ReceiveError", ex.Message);
        }
    }

    /// <summary>
    /// Resets the conversation with the assistant.
    /// This method clears the conversation history for the current user,
    /// allowing them to start a fresh conversation with the assistant.
    /// </summary>
    /// <returns>A task representing the asynchronous reset operation</returns>    
    public async Task ResetConversation()
    {
        try
        {
            var userId = Context.UserIdentifier ?? Context.ConnectionId;
            await Clients.Caller.SendAsync("ReceiveSystemMessage", "Conversation reset successfully.");
        }
        catch (Exception ex)
        {
            await Clients.Caller.SendAsync("ReceiveError", ex.Message);
        }
    }
}
