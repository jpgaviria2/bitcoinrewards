#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BTCPayServer.Plugins.BitcoinRewards.Services;

public class OffensiveWordFilter
{
    private readonly HashSet<string> _offensiveWords;
    
    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "admin", "moderator", "support", "official", "trails", "trailscoffee",
        "coffee", "manager", "staff", "team", "help", "info", "system", "bot", "root"
    };

    public OffensiveWordFilter()
    {
        _offensiveWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var assemblyDir = Path.GetDirectoryName(typeof(OffensiveWordFilter).Assembly.Location) ?? ".";
        var candidates = new[]
        {
            Path.Combine(assemblyDir, "offensive-words-en.txt"),
            Path.Combine(assemblyDir, "Data", "offensive-words-en.txt"),
            Path.Combine(AppContext.BaseDirectory, "offensive-words-en.txt"),
            Path.Combine(AppContext.BaseDirectory, "Data", "offensive-words-en.txt"),
        };
        var filePath = candidates.FirstOrDefault(File.Exists) ?? candidates[0];
        if (File.Exists(filePath))
        {
            foreach (var line in File.ReadAllLines(filePath))
            {
                var word = line.Trim().ToLowerInvariant();
                if (!string.IsNullOrEmpty(word))
                    _offensiveWords.Add(word);
            }
        }
    }

    /// <summary>
    /// Normalize leet-speak substitutions: 1→i, @→a, 3→e, $→s, 0→o
    /// </summary>
    private static string NormalizeLeetSpeak(string input)
    {
        return input
            .Replace('1', 'i')
            .Replace('@', 'a')
            .Replace('3', 'e')
            .Replace('$', 's')
            .Replace('0', 'o');
    }

    /// <summary>
    /// Returns (isOffensive, reason). If offensive, reason explains why.
    /// </summary>
    public (bool IsOffensive, string? Reason) Check(string username)
    {
        var lower = username.ToLowerInvariant();

        if (ReservedNames.Contains(lower))
            return (true, "Reserved name");

        var normalized = NormalizeLeetSpeak(lower);

        // Exact match
        if (_offensiveWords.Contains(lower) || _offensiveWords.Contains(normalized))
            return (true, "Contains inappropriate language");

        // Contains check
        foreach (var word in _offensiveWords.Where(w => w.Length >= 4))
        {
            if (lower.Contains(word) || normalized.Contains(word))
                return (true, "Contains inappropriate language");
        }

        return (false, null);
    }
}
