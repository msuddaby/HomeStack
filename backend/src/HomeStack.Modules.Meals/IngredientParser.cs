using System.Globalization;
using System.Text.RegularExpressions;

namespace HomeStack.Modules.Meals;

public record ParsedIngredient(string Text, string Name, double? Quantity, string? Unit, string? Note, bool Optional);

/// <summary>Turns lines like "1 1/2 cups flour, sifted" into quantity, unit, name and note.</summary>
public static partial class IngredientParser
{
    private static readonly Dictionary<char, string> Vulgar = new()
    {
        ['½'] = "1/2", ['⅓'] = "1/3", ['⅔'] = "2/3", ['¼'] = "1/4", ['¾'] = "3/4", ['⅕'] = "1/5", ['⅛'] = "1/8", ['⅜'] = "3/8", ['⅝'] = "5/8", ['⅞'] = "7/8", ['⅙'] = "1/6",
    };

    [GeneratedRegex(@"^\s*(?<q>(\d+\s+\d+/\d+)|(\d+/\d+)|(\d+(?:[.,]\d+)?))(\s*(-|–|to)\s*(\d+(?:[.,]\d+)?|\d+/\d+))?\s*")]
    private static partial Regex Quantity();

    [GeneratedRegex(@"\([^)]*\)")]
    private static partial Regex Parens();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    private static readonly string[] Descriptors = ["large", "medium", "small", "fresh", "freshly", "ground", "chopped", "diced", "minced", "sliced", "boneless", "skinless", "raw", "cooked", "frozen", "ripe", "extra", "extra-virgin"];

    public static ParsedIngredient Parse(string line)
    {
        var text = Spaces().Replace(line.Trim(), " ");
        var s = text;
        // "1½" -> "1 1/2", "½" -> "1/2"
        foreach (var (c, frac) in Vulgar)
            s = Regex.Replace(s, $@"(\d)?\s*{Regex.Escape(c.ToString())}", m => m.Groups[1].Success ? $"{m.Groups[1].Value} {frac} " : $"{frac} ");
        s = Spaces().Replace(s, " ").Trim();

        double? qty = null;
        var m = Quantity().Match(s);
        if (m.Success && m.Groups["q"].Success)
        {
            qty = ParseNumber(m.Groups["q"].Value);
            s = s[m.Length..];
        }
        else if (Regex.IsMatch(s, @"^(a|an|one)\s+", RegexOptions.IgnoreCase))
        {
            // "an onion" is 1; "a pinch of salt" has no countable amount.
            if (!Regex.IsMatch(s, @"^(a|an)\s+(pinch|dash|few|little|handful|splash|drizzle)\b", RegexOptions.IgnoreCase)) qty = 1;
            s = Regex.Replace(s, @"^(a|an|one)\s+", "", RegexOptions.IgnoreCase);
        }

        // Package size in parentheses, e.g. "1 (14 oz) can tomatoes": keep as note.
        var notes = new List<string>();
        foreach (Match p in Parens().Matches(s)) notes.Add(p.Value.Trim('(', ')', ' '));
        s = Parens().Replace(s, " ").Trim();

        string? unit = null;
        var words = s.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (words.Count > 1)
        {
            var two = $"{words[0]} {words[1]}".TrimEnd('.');
            if (Units.Normalize(two) is { } u2) { unit = u2; words.RemoveRange(0, 2); }
            else if (Units.Normalize(words[0]) is { } u1 && !(words[0] == "c" && qty is null)) { unit = u1; words.RemoveAt(0); }
            else if (Units.Packages.Contains(words[0].TrimEnd('.'))) { unit = words[0].TrimEnd('.').ToLowerInvariant(); words.RemoveAt(0); }
        }
        if (words.FirstOrDefault()?.Equals("of", StringComparison.OrdinalIgnoreCase) == true) words.RemoveAt(0);
        s = string.Join(' ', words);

        var optional = Regex.IsMatch(text, @"\b(optional|to taste|for serving|for garnish|as needed)\b", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\b(optional|to taste|for serving|for garnish|as needed)\b", "", RegexOptions.IgnoreCase);

        // "onion, finely diced" -> name "onion", note "finely diced"
        var comma = s.IndexOf(',');
        if (comma > 0) { notes.Add(s[(comma + 1)..].Trim()); s = s[..comma]; }
        var name = Spaces().Replace(s, " ").Trim(' ', ',', '-', '.', ';');
        if (name.Length == 0) name = text;

        var note = string.Join("; ", notes.Where(n => n.Length > 0));
        return new ParsedIngredient(text, name, qty, unit, note.Length > 0 ? note : null, optional);
    }

    /// <summary>The name with prep/size words removed, for matching against products ("large eggs" -> "eggs").</summary>
    public static string CoreName(string name)
    {
        var words = name.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !Descriptors.Contains(w.Trim(','))).ToList();
        return words.Count > 0 ? string.Join(' ', words) : name.ToLowerInvariant();
    }

    private static double ParseNumber(string s)
    {
        s = s.Trim().Replace(',', '.');
        var parts = s.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        double total = 0;
        foreach (var p in parts)
        {
            if (p.Contains('/'))
            {
                var f = p.Split('/');
                total += double.Parse(f[0], CultureInfo.InvariantCulture) / double.Parse(f[1], CultureInfo.InvariantCulture);
            }
            else total += double.Parse(p, CultureInfo.InvariantCulture);
        }
        return Math.Round(total, 3);
    }
}
