namespace Zoomies.Core.Indexing;

/// <summary>Scores how well a typed word matches a file or folder name. Higher is better, 0 means no match.</summary>
public static class TextMatch
{
    public const double Exact = 100;

    /// <param name="token">The typed word, already lower-cased with <see cref="char.ToLowerInvariant"/>.</param>
    public static double Score(ReadOnlySpan<char> name, string token)
    {
        if (token.Length == 0 || name.Length == 0) return 0;

        int idx = name.IndexOf(token, StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
            // Shorter names that the token covers more of rank higher ("notes" beats "meeting-notes-archive").
            double coverage = 10.0 * token.Length / name.Length;
            if (token.Length == name.Length) return Exact;
            int dot = name.LastIndexOf('.');
            if (idx == 0 && dot == token.Length) return 95; // whole name except the extension
            if (idx == 0) return 80 + coverage;
            for (int at = idx; at >= 0;)
            {
                if (IsWordStart(name, at)) return 65 + coverage;
                int next = name[(at + 1)..].IndexOf(token, StringComparison.OrdinalIgnoreCase);
                at = next < 0 ? -1 : at + 1 + next;
            }
            return 50 + coverage;
        }

        if (token.Length < 2) return 0;
        int fuzzy = Fuzzy(name, token);
        return fuzzy <= 0 ? 0 : Math.Min(40, 12 + fuzzy);
    }

    /// <summary>
    /// Letters typed in order, like "fyp" for "final_year_project". The first letter must start a word,
    /// which keeps random letter soup from matching everything.
    /// </summary>
    internal static int Fuzzy(ReadOnlySpan<char> name, string token)
    {
        int ti = 0, score = 0, prev = -2;
        for (int i = 0; i < name.Length && ti < token.Length; i++)
        {
            if (char.ToLowerInvariant(name[i]) != token[ti]) continue;
            bool wordStart = IsWordStart(name, i);
            if (ti == 0 && !wordStart) continue;
            score += i == prev + 1 ? 3 : 1;
            if (wordStart) score += 3;
            prev = i;
            ti++;
        }
        return ti == token.Length ? score : 0;
    }

    public static bool IsWordStart(ReadOnlySpan<char> s, int i)
    {
        if (i == 0) return true;
        char c = s[i], p = s[i - 1];
        if (p is ' ' or '_' or '-' or '.' or '(' or '[' or ',' or '+' or '&' or '\'') return true;
        if (char.IsUpper(c) && char.IsLower(p)) return true; // camelCase
        return char.IsDigit(c) && !char.IsDigit(p);
    }

    /// <summary>Splits what the user typed into lower-cased words. Path separators count as spaces.</summary>
    public static string[] Tokenize(string query) =>
        query.Split([' ', '\\', '/', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
             .Select(t => t.ToLowerInvariant())
             .ToArray();
}
