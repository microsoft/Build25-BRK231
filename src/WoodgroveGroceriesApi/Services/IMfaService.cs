// Copyright (c) Microsoft Corporation. 
// Licensed under the MIT license.

namespace WoodgroveGroceriesApi.Services
{
    /// <summary>
    /// Interface for multi-factor authentication (MFA) services.
    /// </summary>
    public interface IMfaService
    {
        /// <summary>
        /// Determines if MFA is required based on the transaction amount.
        /// </summary>
        /// <param name="transactionAmount">The amount of the transaction.</param>
        /// <returns>True if MFA is required, false otherwise.</returns>
        Task<bool> RequiresMfaAsync(decimal transactionAmount);
        
        /// <summary>
        /// Checks if MFA has been completed for the current user.
        /// </summary>
        /// <param name="httpContext">The HttpContext containing user claims.</param>
        /// <returns>True if MFA is completed, false otherwise.</returns>
        bool IsMfaCompleted(HttpContext httpContext);
    }
}