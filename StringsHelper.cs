using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Shelltrac
{
    public static class StringsHelper
    {
        /// <summary>
        /// Parses a JSON string into a Shelltrac-compatible Dictionary
        /// </summary>
        /// <param name="json">The JSON string to parse</param>
        /// <returns>A Dictionary representing the parsed JSON</returns>
        public static Dictionary<string, object?> ParseJson(this string json)
        {
            try
            {
                // Create the result dictionary
                var result = new Dictionary<string, object?>();

                // Parse the JSON document
                using (JsonDocument document = JsonDocument.Parse(json))
                {
                    JsonElement root = document.RootElement;

                    // Process based on the JSON root element type
                    switch (root.ValueKind)
                    {
                        case JsonValueKind.Object:
                            ProcessJsonObject(root, result);
                            break;
                        case JsonValueKind.Array:
                            // For arrays, we'll return a dictionary with numeric keys
                            ProcessJsonArray(root, result);
                            break;
                        default:
                            // For primitive types, store a single value with key "value"
                            result["value"] = ConvertJsonElement(root);
                            break;
                    }
                }

                return result;
            }
            catch (Exception ex)
            {
                throw new Exception($"Error parsing JSON: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Try to parse a JSON string into a Shelltrac-compatible Dictionary
        /// </summary>
        /// <param name="json">The JSON string to parse</param>
        /// <param name="result">The resulting dictionary if successful</param>
        /// <returns>True if parsing was successful, false otherwise</returns>
        public static bool TryParseJson(this string json, out Dictionary<string, object?> result)
        {
            try
            {
                result = ParseJson(json);
                return true;
            }
            catch
            {
                result = new Dictionary<string, object?>();
                return false;
            }
        }

        /// <summary>
        /// Process a JSON object into a dictionary
        /// </summary>
        private static void ProcessJsonObject(
            JsonElement element,
            Dictionary<string, object?> target
        )
        {
            foreach (JsonProperty property in element.EnumerateObject())
            {
                target[property.Name] = ConvertJsonElement(property.Value);
            }
        }

        /// <summary>
        /// Process a JSON array into a dictionary with numeric keys
        /// </summary>
        private static void ProcessJsonArray(JsonElement element, Dictionary<string, object?> target)
        {
            int index = 0;
            var items = new List<object?>();

            foreach (JsonElement item in element.EnumerateArray())
            {
                items.Add(ConvertJsonElement(item));
                index++;
            }

            // Store the actual list rather than numeric keys
            target["items"] = items;
        }

        /// <summary>
        /// Convert a JsonElement to the appropriate .NET type
        /// </summary>
        private static object? ConvertJsonElement(JsonElement element)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    var objResult = new Dictionary<string, object?>();
                    ProcessJsonObject(element, objResult);
                    return objResult;

                case JsonValueKind.Array:
                    var arrayResult = new List<object?>();
                    foreach (JsonElement item in element.EnumerateArray())
                    {
                        arrayResult.Add(ConvertJsonElement(item));
                    }
                    return arrayResult;

                case JsonValueKind.String:
                    return element.GetString();

                case JsonValueKind.Number:
                    // Try to parse as Int32 first, then as double
                    if (element.TryGetInt32(out int intValue))
                        return intValue;

                    return element.GetDouble();

                case JsonValueKind.True:
                    return true;

                case JsonValueKind.False:
                    return false;

                case JsonValueKind.Null:
                    return null;

                default:
                    return element.ToString();
            }
        }
    }
}
