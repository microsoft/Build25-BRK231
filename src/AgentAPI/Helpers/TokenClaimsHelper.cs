// Copyright (c) Microsoft Corporation. 
// Licensed under the MIT license.

using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;

namespace MyOpenAIWebApi.Helpers;

/// <summary>
/// Helper class for working with JWT token claims
/// </summary>
public static class TokenClaimsHelper
{
    /// <summary>
    /// Extracts claims from a JWT token and returns them as a dictionary
    /// </summary>
    /// <param name="token">The JWT token to parse</param>
    /// <returns>A dictionary containing all claims from the token</returns>
    public static Dictionary<string, object> GetTokenClaims(string token)
    {
        var result = new Dictionary<string, object>();
        
        if (string.IsNullOrEmpty(token))
            return result;
            
        try
        {
            var handler = new JwtSecurityTokenHandler();
            if (handler.CanReadToken(token))
            {
                var jwtToken = handler.ReadJwtToken(token);
                
                foreach (var claim in jwtToken.Claims)
                {
                    // Try to parse claims that could be JSON arrays or objects
                    if ((claim.Value.StartsWith("[") && claim.Value.EndsWith("]")) || 
                        (claim.Value.StartsWith("{") && claim.Value.EndsWith("}")))
                    {
                        try
                        {
                            var jsonElement = JsonSerializer.Deserialize<JsonElement>(claim.Value);
                            result[claim.Type] = jsonElement;
                            continue;
                        }
                        catch
                        {
                            // If deserialization fails, treat it as a string
                        }
                    }
                    
                    // Claims that are string arrays are handled specially
                    if (result.ContainsKey(claim.Type))
                    {
                        if (result[claim.Type] is List<string> list)
                        {
                            list.Add(claim.Value);
                        }
                        else
                        {
                            result[claim.Type] = new List<string> { result[claim.Type].ToString(), claim.Value };
                        }                    }
                    else
                    {
                        result[claim.Type] = claim.Value;
                    }
                }
                
                // Also add information about the token
                result["token_expiration"] = jwtToken.ValidTo.ToUniversalTime().ToString("o");
                result["token_issued"] = jwtToken.ValidFrom.ToUniversalTime().ToString("o");
                result["token_issuer"] = jwtToken.Issuer;
                
                // Calculate age if DateOfBirth exists
                if (result.ContainsKey("DateOfBirth") && result["DateOfBirth"] is string dateOfBirthStr)
                {
                    if (DateTime.TryParse(dateOfBirthStr, out DateTime dateOfBirth))
                    {
                        int age = DateTime.Today.Year - dateOfBirth.Year;
                        if (dateOfBirth > DateTime.Today.AddYears(-age)) age--;
                        result["Age"] = age;
                    }
                }
                
                // Verify if the user has completed MFA
                if (result.ContainsKey("acrs"))
                {
                    var acrs = result["acrs"];
                    if (acrs is JsonElement jsonElement)
                    {
                        if (jsonElement.ValueKind == JsonValueKind.Object &&
                            jsonElement.TryGetProperty("value", out var valueElement) &&
                            valueElement.GetString() == "c1")
                        {
                            result["mfa_completed"] = true;
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            // In case of error, add error information
            result["error"] = $"Error parsing token: {ex.Message}";
        }
        
        return result;
    }
    
    /// <summary>
    /// Calculates a person's age based on their date of birth
    /// </summary>
    /// <param name="dateOfBirthStr">Date of birth as a string</param>
    /// <returns>The calculated age or null if the date couldn't be parsed</returns>
    public static int? CalculateAge(string dateOfBirthStr)
    {
        if (DateTime.TryParse(dateOfBirthStr, out DateTime dateOfBirth))
        {
            int age = DateTime.Today.Year - dateOfBirth.Year;
            if (dateOfBirth > DateTime.Today.AddYears(-age)) age--;
            return age;
        }
        return null;
    }
    
    /// <summary>
    /// Checks if a person is considered an adult based on their date of birth
    /// </summary>
    /// <param name="dateOfBirthStr">Date of birth as a string</param>
    /// <param name="adultAge">The age at which someone is considered an adult (default is 18)</param>
    /// <returns>True if the person is an adult, false otherwise</returns>
    public static bool IsAdult(string dateOfBirthStr, int adultAge = 18)
    {
        var age = CalculateAge(dateOfBirthStr);
        return age.HasValue && age.Value >= adultAge;
    }
    
    /// <summary>
    /// Checks if a user has completed multi-factor authentication (MFA)
    /// </summary>
    /// <param name="claims">Dictionary of token claims</param>
    /// <returns>True if MFA has been completed, false otherwise</returns>
    public static bool HasCompletedMfa(Dictionary<string, object> claims)
    {
        // Directly check if we've already calculated mfa_completed
        if (claims.ContainsKey("mfa_completed") && claims["mfa_completed"] is bool mfaCompleted)
            return mfaCompleted;
            
        // Verify based on the acrs claim
        if (claims.ContainsKey("acrs"))
        {
            var acrs = claims["acrs"];
            if (acrs is JsonElement jsonElement)
            {
                if (jsonElement.ValueKind == JsonValueKind.Object &&
                    jsonElement.TryGetProperty("value", out var valueElement) &&
                    valueElement.GetString() == "c1")
                {
                    return true;
                }
            }
        }
        
        return false;
    }
}