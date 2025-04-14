using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Shelltrac
{
    public static class RegexHelpers
    {
        // Check if a string matches a pattern
        // Returns: bool
        public static bool IsMatch(this string input, string pattern)
        {
            return Regex.IsMatch(input, pattern);
        }

        // Extract the first match of a pattern
        // Returns: (matchText, matchGroups)
        // Simplified Extract that only returns the groups
        public static List<string> Extract(this string input, string pattern)
        {
            var regex = new Regex(pattern);
            var match = regex.Match(input);

            var groupValues = new List<string>();
            if (!match.Success)
                return groupValues;

            for (int i = 1; i < match.Groups.Count; i++)
            {
                groupValues.Add(match.Groups[i].Value);
            }

            return groupValues;
        }

        // Extract all matches of a pattern
        // Returns: List<string> of matches
        public static List<string> ExtractAll(this string input, string pattern)
        {
            var regex = new Regex(pattern);
            var matches = regex.Matches(input);

            var matchList = new List<string>();
            foreach (Match match in matches)
            {
                matchList.Add(match.Value);
            }

            return matchList;
        }

        // Extract captures from named groups
        // Returns: Dictionary<string, string> of named captures
        public static Dictionary<string, string> ExtractNamed(this string input, string pattern)
        {
            var regex = new Regex(pattern);
            var match = regex.Match(input);

            var captures = new Dictionary<string, string>();
            if (!match.Success)
                return captures;

            foreach (Group group in match.Groups)
            {
                if (group.Name != "0" && !string.IsNullOrEmpty(group.Name))
                {
                    captures[group.Name] = group.Value;
                }
            }

            return captures;
        }

        // Replace matches with a replacement string
        // Returns: new string with replacements
        public static string RegexReplace(this string input, string pattern, string replacement)
        {
            return Regex.Replace(input, pattern, replacement);
        }
    }
}
