// Copyright (c) Microsoft Corporation. 
// Licensed under the MIT license.

namespace WoodgroveGroceriesApi.Services
{
    /// <summary>
    /// Simple implementation of the multi-factor authentication service.
    /// </summary>
    public class SimpleMfaService : IMfaService
    {
        private readonly IConfiguration _configuration;
        private const decimal DEFAULT_MFA_THRESHOLD = 100.0m;

        /// <summary>
        /// Initializes a new instance of the <see cref="SimpleMfaService"/> class.
        /// </summary>
        /// <param name="configuration">The configuration instance.</param>
        public SimpleMfaService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        /// <summary>
        /// Determines if MFA is required based on the transaction amount.
        /// </summary>
        /// <param name="transactionAmount">The amount of the transaction.</param>
        /// <returns>True if MFA is required, false otherwise.</returns>
        public Task<bool> RequiresMfaAsync(decimal transactionAmount)
        {
            // Get the threshold from configuration or use default
            // Usando la configuración existente Authorization:CheckoutThresholdForMFA
            decimal mfaThreshold = _configuration.GetValue<decimal>("Authorization:CheckoutThresholdForMFA", DEFAULT_MFA_THRESHOLD);
            
            // Require MFA if transaction amount exceeds threshold
            bool requiresMfa = transactionAmount >= mfaThreshold;
            
            return Task.FromResult(requiresMfa);
        }
        
        /// <summary>
        /// Checks if MFA has been completed for the current user by examining the access token claims.
        /// </summary>
        /// <param name="httpContext">The HttpContext containing user claims.</param>
        /// <returns>True if MFA is completed, false otherwise.</returns>
        public bool IsMfaCompleted(HttpContext httpContext)
        {
            // 🔍 DEMO POINT: CHECK MFA IN RESOURCE API
            // Check if the access token includes the MFA claim (acrs with value "c1")
            var acrsClaim = httpContext.User?.FindFirst("acrs");
            return acrsClaim != null && 
                (acrsClaim.Value == "c1" || (acrsClaim.Value.StartsWith("[") && acrsClaim.Value.Contains("\"c1\"")));
        }
    }
}