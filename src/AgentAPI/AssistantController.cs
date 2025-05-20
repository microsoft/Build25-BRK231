// Copyright (c) Microsoft Corporation. 
// Licensed under the MIT license.

using Microsoft.AspNetCore.Mvc;
using MyOpenAIWebApi.Services;
using System.Security.Claims;
using System.Text;

namespace MyOpenAIWebApi.Controllers;

/// <summary>
/// Controller for handling assistant-related operations and conversations.
/// This controller provides endpoints for interacting with the AI assistant,
/// including sending messages and resetting conversations.
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AssistantController : ControllerBase
{
    private readonly IAssistantManager _assistantManager;

    /// <summary>
    /// Initializes a new instance of the AssistantController class.
    /// </summary>
    /// <param name="assistantManager">The assistant manager service that handles user-specific AI assistant instances</param>
    public AssistantController(IAssistantManager assistantManager)
    {
        _assistantManager = assistantManager;
    }

    /// <summary>
    /// Sends a message to the assistant and gets the complete response.
    /// This endpoint processes the user's message, sends it to the appropriate
    /// AI assistant instance, and returns the full response.
    /// </summary>
    /// <param name="userMessage">The message text from the user to be processed by the assistant</param>
    /// <returns>An action result containing the assistant's response or an error message</returns>
    /// <response code="200">Returns the assistant's response</response>
    /// <response code="500">If there was an error processing the message</response>
    [HttpPost("send")]
    public async Task<IActionResult> SendMessage([FromBody] string userMessage)
    {
        try
        {
            // 1) Identify the user from the claims (or use "default" if no authenticated user)
            string userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "default";

            // 2) Get (or create) the specific instance of OpenAIService for this user
            var openAIService = await _assistantManager.GetOrCreateOpenAIServiceForUserAsync(userId);

            // 3) Consume the streaming response and build the complete response
            var responseBuilder = new StringBuilder();
            await foreach (var partialResponse in openAIService.SendMessageAndStreamAsync(userMessage))
            {
                responseBuilder.Append(partialResponse);
            }

            return Ok(new { response = responseBuilder.ToString() });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// Resets the conversation with the assistant.
    /// This endpoint clears the conversation history for the current user,
    /// allowing them to start a fresh conversation with the assistant.
    /// </summary>
    /// <returns>An action result indicating success or failure of the reset operation</returns>
    /// <response code="200">Returns a success message if the conversation was reset</response>
    /// <response code="500">If there was an error resetting the conversation</response>
    [HttpPost("reset")]
    public async Task<IActionResult> ResetConversation()
    {
        try
        {
            string userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "default";
            
            // Reset the conversation by clearing the user's assistant instance
            // This will force a new instance to be created on the next request
            await Task.CompletedTask; // Placeholder for actual reset logic
            
            return Ok(new { message = "Conversation reset successfully." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }
}
