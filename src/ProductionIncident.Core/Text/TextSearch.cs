using System.Text.RegularExpressions;

namespace ProductionIncident.Core.Text;

/// <summary>Tiny lexical helpers shared by the in-memory RAG/memory stores and deterministic checks.</summary>
public static partial class TextSearch
{
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "an", "and", "are", "as", "at", "be", "by", "for", "from", "has", "have", "in", "is", "it",
        "of", "on", "or", "that", "the", "this", "to", "was", "were", "with", "after", "before", "into",
        "not", "no", "its", "than", "then", "when", "which", "while", "we", "our", "you", "your",
    };

    [GeneratedRegex("[a-z0-9][a-z0-9_.\\-/]*", RegexOptions.IgnoreCase)]
    private static partial Regex TokenRegex();

    public static IReadOnlyList<string> Tokenize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        return TokenRegex().Matches(text.ToLowerInvariant())
            .Select(m => m.Value.Trim('.', '-', '/'))
            .Where(t => t.Length > 1 && !StopWords.Contains(t))
            .ToList();
    }

    /// <summary>Fraction of the query's distinct terms that appear in the text (0..1).</summary>
    public static double Overlap(string? query, string? text)
    {
        var q = Tokenize(query).ToHashSet();
        if (q.Count == 0)
        {
            return 0;
        }

        var t = Tokenize(text).ToHashSet();
        return (double)q.Count(t.Contains) / q.Count;
    }

    public static bool ContainsAll(string? text, IEnumerable<string> keywords) =>
        text is not null && keywords.All(k => text.Contains(k, StringComparison.OrdinalIgnoreCase));
}
