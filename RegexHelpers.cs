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

            using var pooledGroupValues = ShelltracPools.GetStringList();
            var groupValues = pooledGroupValues.Value;
            if (!match.Success)
                return new List<string>();

            for (int i = 1; i < match.Groups.Count; i++)
            {
                groupValues.Add(match.Groups[i].Value);
            }

            return new List<string>(groupValues);
        }

        // Extract all matches of a pattern
        // Returns: List<string> of matches
        public static List<string> ExtractAll(this string input, string pattern)
        {
            var regex = new Regex(pattern);
            var matches = regex.Matches(input);

            using var pooledMatchList = ShelltracPools.GetStringList();
            var matchList = pooledMatchList.Value;
            foreach (Match match in matches)
            {
                matchList.Add(match.Value);
            }

            return new List<string>(matchList);
        }

        // Extract captures from named groups
        // Returns: Dictionary<string, string> of named captures
        public static Dictionary<string, string> ExtractNamed(this string input, string pattern)
        {
            var regex = new Regex(pattern);
            var match = regex.Match(input);

            using var pooledCaptures = ShelltracPools.GetDictionaryStringString();
            var captures = pooledCaptures.Value;
            if (!match.Success)
                return new Dictionary<string, string>();

            foreach (Group group in match.Groups)
            {
                if (group.Name != "0" && !string.IsNullOrEmpty(group.Name))
                {
                    captures[group.Name] = group.Value;
                }
            }

            return new Dictionary<string, string>(captures);
        }

        // Replace matches with a replacement string
        // Returns: new string with replacements
        public static string RegexReplace(this string input, string pattern, string replacement)
        {
            return Regex.Replace(input, pattern, replacement);
        }
    }
}
