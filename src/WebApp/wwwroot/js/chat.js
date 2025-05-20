// Copyright (c) Microsoft Corporation. 
// Licensed under the MIT license.

/**
 * ChatManager class
 * Manages communication with the chat service through SignalR.
 * Handles authentication, message sending/receiving, and UI updates.
 */
class ChatManager {
    /**
     * Initializes a new instance of the ChatManager class.
     * @param {string} accessToken - The access token for authentication.
     * @param {string} hubUrl - The URL of the SignalR hub.
     */
    constructor(accessToken, hubUrl) {
        this.accessToken = accessToken;
        this.hubUrl = hubUrl;
        this.connection = null;
        this.isConnected = false;
        this.isAssistantTyping = false;
        this.messages = [];
        this.currentAssistantIndex = null;
        this.connectionError = null;
        this.messageContainer = document.getElementById('chat-messages');
        this.statusContainer = document.getElementById('connection-status');
        this.messageInput = document.getElementById('message-input');
        this.sendButton = document.getElementById('send-button');
        this.resetButton = document.getElementById('reset-button');
        this.stopButton = document.getElementById('stop-button');
        this.retryButton = document.getElementById('retry-button');

        // Associate events
        this.sendButton.addEventListener('click', () => this.sendMessage());
        this.resetButton.addEventListener('click', () => this.resetConversation());
        this.stopButton.addEventListener('click', () => this.stopConversation());
        if (this.retryButton) {
            this.retryButton.addEventListener('click', () => this.initializeConnection());
        }
        this.messageInput.addEventListener('keydown', (e) => this.handleKeyDown(e));

        // Check if the user has changed and load chat history if it exists
        this.checkUserAndLoadHistory();

        // Initialize connection
        this.initializeConnection();
    }

    /**
     * Checks if the current user is different from the previous session
     * and loads the appropriate chat history.
     */
    checkUserAndLoadHistory() {
        try {
            // Calculate a simple hash of the token to identify the user
            // (We don't use the complete token for security reasons)
            const tokenHash = this.calculateTokenHash(this.accessToken);
            
            // Check if there's a previously saved user
            const savedUserInfo = localStorage.getItem('chatUserInfo');
            
            if (savedUserInfo) {
                const previousTokenHash = savedUserInfo;
                
                // If the user has changed, clear the previous history
                if (previousTokenHash !== tokenHash) {
                    console.log('Different user detected, clearing chat history');
                    this.clearSavedChatHistory();
                    // Save the new user's information
                    localStorage.setItem('chatUserInfo', tokenHash);
                }
            } else {
                // It's the first time, save the user's information
                localStorage.setItem('chatUserInfo', tokenHash);
            }
            
            // Load history
            this.loadChatHistory();
            
        } catch (error) {
            console.error('Error checking user change:', error);
            // In case of error, start with a clean history for security
            this.messages = [];
            this.clearSavedChatHistory();
        }
    }

    /**
     * Calculates a secure identifier based on the token.
     * Attempts to extract the "oid" claim from a JWT token.
     * @param {string} token - The access token.
     * @returns {string} A user identifier derived from the token.
     */
    calculateTokenHash(token) {
        try {
            // Try to extract the "oid" claim from the JWT token that represents the user's unique ID
            // A JWT token has the format: header.payload.signature
            const parts = token.split('.');
            if (parts.length !== 3) {
                console.warn('Token does not appear to be a valid JWT');
                return this.calculateSimpleHash(token); // Fallback to previous method
            }

            // Decode the payload part (second part of the token)
            const payload = JSON.parse(atob(parts[1]));
            
            // Verify if the "oid" claim exists
            if (payload && payload.oid) {
                console.log('Using oid claim for user identification');
                return 'user-' + payload.oid; // Use oid as identifier
            } else {
                console.warn('oid claim not found in token');
                return this.calculateSimpleHash(token); // Fallback to previous method
            }
        } catch (error) {
            console.error('Error processing JWT token:', error);
            return this.calculateSimpleHash(token); // Fallback to previous method
        }
    }

    /**
     * Calculates a simple hash from the token.
     * Used as a fallback when JWT parsing fails.
     * @param {string} token - The access token.
     * @returns {string} A simple identifier derived from the token.
     */
    calculateSimpleHash(token) {
        // Extract only a part of the token to generate an identifier
        // This method is simple but sufficient to identify different users
        // We don't store the complete token for security reasons
        if (!token || token.length < 20) return 'invalid-token';
        
        // Use the first 8 characters and the last 8
        const tokenParts = token.substring(0, 8) + token.substring(token.length - 8);
        return tokenParts;
    }

    /**
     * Saves the current chat history to localStorage.
     */
    saveChatHistory() {
        if (this.messages && this.messages.length > 0) {
            try {
                localStorage.setItem('chatHistory', JSON.stringify(this.messages));
                console.log('Chat history saved to localStorage:', this.messages.length, 'messages');
            } catch (error) {
                console.error('Error saving chat history to localStorage:', error);
            }
        }
    }

    /**
     * Loads chat history from localStorage.
     */
    loadChatHistory() {
        try {
            const savedHistory = localStorage.getItem('chatHistory');
            if (savedHistory) {
                this.messages = JSON.parse(savedHistory);
                console.log('Chat history loaded from localStorage:', this.messages.length, 'messages');
                this.updateMessageDisplay();
            }
        } catch (error) {
            console.error('Error loading chat history from localStorage:', error);
            // If there's an error, start with an empty history
            this.messages = [];
        }
    }

    /**
     * Clears saved chat history from localStorage.
     */
    clearSavedChatHistory() {
        try {
            localStorage.removeItem('chatHistory');
            // Also clear messages in memory
            this.messages = [];
            console.log('Saved chat history cleared');
        } catch (error) {
            console.error('Error clearing chat history from localStorage:', error);
        }
    }

    /**
     * Initializes the SignalR connection to the chat service.
     */
    async initializeConnection() {
        try {
            this.connectionError = null;
            this.updateStatus('Connecting to chat service...');
            
            // Create connection with SignalR with configuration for better compatibility
            this.connection = new signalR.HubConnectionBuilder()
                .withUrl(this.hubUrl, {
                    accessTokenFactory: () => this.accessToken,
                    // Do not skip negotiation for better compatibility
                    skipNegotiation: false,
                    // Try all available transports
                    transport: signalR.HttpTransportType.WebSockets | signalR.HttpTransportType.ServerSentEvents | signalR.HttpTransportType.LongPolling
                })
                .withAutomaticReconnect([0, 2000, 10000, 30000]) // More aggressive reconnection attempts
                .configureLogging(signalR.LogLevel.Information) // More log information
                .build();

            // Configure events
            this.setupSignalREventHandlers();

            // Start the connection
            await this.connection.start();
            
            this.isConnected = true;
            this.updateStatus('');
            this.addMessage(`System: Connected to chat service`);
            this.updateUI();
        } 
        catch (ex) {
            this.connectionError = ex.message;
            this.isConnected = false;
            this.updateStatus(`<div class="alert alert-warning">
                Connecting to chat service...
                <div class="text-danger">Error: ${this.connectionError}</div>
                <div class="text-info">
                    <p>Verify the following:</p>
                    <ul>
                        <li>The SignalR server is running at ${this.hubUrl}</li>
                        <li>The AssistantHub endpoint exists on the server</li>
                        <li>CORS is properly configured on the server to allow ${window.location.origin}</li>
                        <li>The access token is valid and has the necessary permissions</li>
                    </ul>
                </div>
                <button class="btn btn-sm btn-primary mt-2" id="retry-button">Retry connection</button>
            </div>`);
            this.addMessage(`Error: Could not connect to chat service. ${ex.message}`);
            
            // Re-associate the event to the new button
            const retryButton = document.getElementById('retry-button');
            if (retryButton) {
                retryButton.addEventListener('click', () => this.initializeConnection());
            }
            console.error("SignalR connection error:", ex);
        }
    }

    /**
     * Sets up event handlers for SignalR messages.
     */
    setupSignalREventHandlers() {
        // Event: StartTyping
        this.connection.on("ReceiveStartTyping", () => {
            this.isAssistantTyping = true;
            this.addMessage("Agent: ");
            this.currentAssistantIndex = this.messages.length - 1;
            this.updateUI();
        });

        // Event: PartialResponse
        this.connection.on("ReceivePartialResponse", (partial) => {
            if (this.currentAssistantIndex !== null && this.currentAssistantIndex < this.messages.length) {
                this.messages[this.currentAssistantIndex] += partial;
            }
            else {
                this.addMessage("Agent: " + partial);
                this.currentAssistantIndex = this.messages.length - 1;
            }
            this.updateUI();
        });

        // Event: EndTyping
        this.connection.on("ReceiveEndTyping", () => {
            this.isAssistantTyping = false;
            this.currentAssistantIndex = null;
            this.updateUI();
        });

        // Event: Error
        this.connection.on("ReceiveError", (err) => {
            this.isAssistantTyping = false;
            this.currentAssistantIndex = null;
            this.addMessage(`Error: ${err}`);
            this.updateUI();
        });

        // Event: SystemMessage
        this.connection.on("ReceiveSystemMessage", (msg) => {
            this.addMessage(`System: ${msg}`);
            this.updateUI();
        });

        // Reconnection events
        this.connection.onreconnecting((error) => {
            this.isConnected = false;
            this.addMessage("System: Attempting to reconnect...");
            this.updateUI();
        });

        this.connection.onreconnected((connectionId) => {
            this.isConnected = true;
            this.addMessage("System: Reconnected to chat service");
            this.updateUI();
        });

        this.connection.onclose((error) => {
            this.isConnected = false;
            this.connectionError = error ? error.message : null;
            this.addMessage(`System: Connection closed${error ? ` due to an error: ${error.message}` : ""}`);
            this.updateUI();
        });
    }

    /**
     * Updates the connection status display.
     * @param {string} statusHtml - HTML content for the status message.
     */
    updateStatus(statusHtml) {
        if (this.statusContainer) {
            this.statusContainer.innerHTML = statusHtml;
        }
    }

    /**
     * Adds a message to the chat history.
     * @param {string} message - The message to add.
     */
    addMessage(message) {
        this.messages.push(message);
        this.updateMessageDisplay();
        // Save history every time a message is added
        this.saveChatHistory();
    }

    /**
     * Updates the message display in the UI.
     */
    updateMessageDisplay() {
        if (this.messageContainer) {
            this.messageContainer.innerHTML = '';
            for (let msg of this.messages) {
                const div = document.createElement('div');
                div.className = 'chat-message';
                
                // Detect the type of message and assign corresponding CSS class
                if (msg.startsWith('You: ')) {
                    div.className += ' user-message';
                    const content = msg.substring(5); // Remove "You: "
                    div.innerHTML = `<strong>You:</strong> ${content}`;
                }
                else if (msg.startsWith('Agent: ')) {
                    div.className += ' assistant-message';
                    const content = msg.substring(7); // Corrected: Remove "Agent: " (7 characters) instead of 11
                    try {
                        // Use standard marked.js rendering - we'll handle mfa: links later
                        div.innerHTML = `<strong>Agent:</strong> ${marked.parse(content)}`;
                    } catch (e) {
                        // Fallback in case of error rendering markdown
                        console.error("Error rendering markdown:", e);
                        div.innerHTML = `<strong>Agent:</strong> ${content}`;
                    }
                }
                else if (msg.startsWith('System: ')) {
                    div.className += ' system-message';
                    const content = msg.substring(8); // Remove "System: "
                    div.innerHTML = `<strong>System:</strong> ${content}`;
                }
                else if (msg.startsWith('Error: ')) {
                    div.className += ' error-message';
                    const content = msg.substring(7); // Remove "Error: "
                    div.innerHTML = `<strong>Error:</strong> ${content}`;
                }
                else {
                    // For other messages without specific prefix
                    div.textContent = msg;
                }
                
                this.messageContainer.appendChild(div);
            }

            // Process MFA links after they've been rendered in the DOM
            this.processMfaLinks();
            
            this.scrollToBottom();
        }
    }

    /**
     * Processes MFA links in the chat messages.
     * Converts mfa: protocol links into interactive buttons.
     */
    processMfaLinks() {
        // Find all links with mfa: protocol in href
        const mfaLinks = this.messageContainer.querySelectorAll('a[href^="mfa:"]');
        
        mfaLinks.forEach(link => {
            const href = link.getAttribute('href');
            // Extract the action name from mfa:ActionName - sanitize any trailing characters
            let action = href.substring(4); // Skip "mfa:"
            
            // Clean up: remove any closing tag or strange characters
            // that might be present due to how markdown is rendered
            // that might be present due to how markdown is rendered
            action = action.replace(/"[^"]*$/g, ''); // Remove quotes and anything after
            action = action.replace(/'\s*\/>$/g, ''); // Remove '/>
            action = action.replace(/"\s*\/>$/g, ''); // Remove "/>
            action = action.replace(/\s*\/>$/g, '');  // Remove />
            action = action.trim();                   // Remove whitespace
            
            // Instead of changing the href attribute (which can trigger browser errors),
            // add a click handler to process these special links
            link.addEventListener('click', (e) => {
                e.preventDefault(); // Prevent the browser from trying to navigate to mfa: URL
                
                // Call the MFA verification function if available
                if (typeof window.showMfaVerification === 'function') {
                    window.showMfaVerification(action);
                } else {
                    console.error("showMfaVerification function not found");
                }
            });
            
            // Improve accessibility by adding attributes that indicate this is a special link
            link.setAttribute('role', 'button');
            link.setAttribute('data-mfa-action', action);
            
            // For browser dev tools display, change href to # (but navigation is already 
            // handled by our event handler)
            link.setAttribute('href', '#');
        });
    }

    /**
     * Updates the UI state based on current connection and typing status.
     */
    updateUI() {
        // Update button states
        if (this.sendButton) {
            this.sendButton.disabled = !this.isConnected;
        }
        if (this.resetButton) {
            this.resetButton.disabled = !this.isConnected;
        }
        if (this.stopButton) {
            this.stopButton.disabled = !this.isConnected || !this.isAssistantTyping;
        }
        
        this.updateMessageDisplay();
    }

    /**
     * Sends a message to the chat service.
     */
    async sendMessage() {
        if (!this.isConnected) {
            this.addMessage("Error: Not connected to chat service");
            return;
        }

        const message = this.messageInput.value.trim();
        if (message) {
            try {
                // Add our local message
                this.addMessage(`You: ${message}`);

                // Clear input
                this.messageInput.value = '';

                // Invoke the hub
                await this.connection.invoke("SendMessage", message);
                this.scrollToBottom();
            }
            catch (ex) {
                this.addMessage(`Error sending message: ${ex.message}`);
                if (!this.isConnected) {
                    await this.initializeConnection();
                }
            }
        }
    }

    /**
     * Resets the conversation with the chat service.
     */
    async resetConversation() {
        if (!this.isConnected) {
            await this.initializeConnection();
            return;
        }

        try {
            await this.connection.invoke("ResetConversation");
            // Clear saved history when conversation is reset
            this.clearSavedChatHistory();
        }
        catch (ex) {
            this.addMessage(`Error: Failed to reset conversation: ${ex.message}`);
            if (!this.isConnected) {
                await this.initializeConnection();
            }
        }
    }

    /**
     * Stops the current conversation.
     */
    async stopConversation() {
        if (!this.isConnected) {
            return;
        }

        try {
            await this.connection.invoke("StopConversation");
            this.isAssistantTyping = false;
            this.updateUI();
        }
        catch (ex) {
            this.addMessage(`Error: Failed to stop conversation: ${ex.message}`);
        }
    }

    /**
     * Handles keydown events in the message input.
     * @param {KeyboardEvent} e - The keyboard event.
     */
    handleKeyDown(e) {
        if (e.key === "Enter" && !e.shiftKey) {
            e.preventDefault();
            this.sendMessage();
        }
    }

    /**
     * Scrolls the message container to the bottom.
     */
    scrollToBottom() {
        if (this.messageContainer) {
            this.messageContainer.scrollTop = this.messageContainer.scrollHeight;
        }
    }

    /**
     * Disposes of the chat manager resources.
     */
    async dispose() {
        if (this.connection) {
            // Save history before closing the connection
            this.saveChatHistory();
            await this.connection.stop();
            this.connection = null;
        }
    }
}

/**
 * Initializes the chat with the provided access token and hub URL.
 * @param {string} accessToken - The access token for authentication.
 * @param {string} hubUrl - The URL of the SignalR hub.
 */
function initializeChat(accessToken, hubUrl) {
    // Wait for the DOM to be ready
    document.addEventListener('DOMContentLoaded', () => {
        window.chatManager = new ChatManager(accessToken, hubUrl);
    });
}

/**
 * Scrolls an element to its bottom.
 * @param {string} elementId - The ID of the element to scroll.
 */
function scrollElementToBottom(elementId) {
    const element = document.getElementById(elementId);
    if (element) {
        element.scrollTop = element.scrollHeight;
    }
}