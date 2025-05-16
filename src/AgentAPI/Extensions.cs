// Copyright (c) Microsoft Corporation. 
// Licensed under the MIT license.

using System.Text.RegularExpressions;

namespace MyOpenAIWebApi;

/// <summary>
/// Extension methods and utility functions for the application
/// </summary>
public static partial class Extensions
{
    /// <summary>
    /// Regular expression for validating SHA-256 hash strings
    /// </summary>
    [GeneratedRegex(@"\A\b[0-9a-fA-F]{64}\b\Z")]
    public static partial Regex ShaValidation();

    /// <summary>
    /// Regular expression for validating email addresses
    /// </summary>
    [GeneratedRegex(@"\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b")]
    public static partial Regex MailValidation();

    /// <summary>
    /// Regular expression for validating file paths for image files
    /// </summary>
    [GeneratedRegex(@"""((?i:[a-z]):\\+[^""]+\.(png|jpg|jpeg))""")]
    public static partial Regex FilePathValidation();
    
    /// <summary>
    /// Gets a value from a dictionary, returning null if the key doesn't exist
    /// </summary>
    /// <typeparam name="TKey">The dictionary key type</typeparam>
    /// <typeparam name="TValue">The dictionary value type</typeparam>
    /// <param name="dict">The dictionary to get the value from</param>
    /// <param name="key">The key to look up</param>
    /// <returns>The value or null if not found</returns>
    public static TValue? GetNullableValue<TKey, TValue>(this IDictionary<TKey, TValue> dict, TKey key) => dict.TryGetValue(key, out var val) ? val : default;

    /// <summary>
    /// Joins an enumerable of strings with a character separator
    /// </summary>
    /// <param name="source">The strings to join</param>
    /// <param name="separator">The separator character</param>
    /// <returns>A joined string</returns>
    public static string StringJoin(this IEnumerable<string> source, char separator) => string.Join(separator, source);
    
    /// <summary>
    /// Joins an enumerable of strings with a string separator
    /// </summary>
    /// <param name="source">The strings to join</param>
    /// <param name="separator">The separator string</param>
    /// <returns>A joined string</returns>
    public static string StringJoin(this IEnumerable<string> source, string separator) => string.Join(separator, source);
    
    /// <summary>
    /// Checks if an item is in a collection of items
    /// </summary>
    /// <typeparam name="T">The type of the items</typeparam>
    /// <param name="item">The item to check</param>
    /// <param name="items">The collection of items to check against</param>
    /// <returns>True if the item is in the collection, false otherwise</returns>
    public static bool In<T>(this T? item, params T[] items) => item.In((IEnumerable<T>)items);
      /// <summary>
    /// Checks if an item is in an enumerable collection
    /// </summary>
    /// <typeparam name="T">The type of the items</typeparam>
    /// <param name="item">The item to check</param>
    /// <param name="items">The enumerable collection to check against</param>
    /// <returns>True if the item is in the collection, false otherwise</returns>
    public static bool In<T>(this T? item, IEnumerable<T> items) => items.Any(i => i?.Equals(item) ?? false);
}
