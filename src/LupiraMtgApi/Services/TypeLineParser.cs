namespace LupiraMtgApi.Services;

public static class TypeLineParser
{
    private static readonly HashSet<string> KnownSupertypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Basic",
        "Legendary",
        "Snow",
        "World",
        "Ongoing",
        "Token",
        "Tribal",
        "Host",
        "Elite",
    };

    public static (string? Supertype, string Type, string? Subtype) Parse(string? typeLine)
    {
        if (string.IsNullOrWhiteSpace(typeLine))
        {
            return (null, string.Empty, null);
        }

        var trimmed = typeLine.Trim();

        // U+2014 EM DASH is the canonical separator on cards and in Scryfall payloads.
        // Some printings use a hyphen-minus; accept both.
        var dashIndex = trimmed.IndexOf('—');
        if (dashIndex < 0)
        {
            dashIndex = trimmed.IndexOf(" - ", StringComparison.Ordinal);
            if (dashIndex >= 0)
            {
                var lhs = trimmed[..dashIndex].Trim();
                var rhs = trimmed[(dashIndex + 3)..].Trim();
                var (sup, typ) = SplitSupertype(lhs);
                return (sup, typ, string.IsNullOrEmpty(rhs) ? null : rhs);
            }
        }
        else
        {
            var lhs = trimmed[..dashIndex].Trim();
            var rhs = trimmed[(dashIndex + 1)..].Trim();
            var (sup, typ) = SplitSupertype(lhs);
            return (sup, typ, string.IsNullOrEmpty(rhs) ? null : rhs);
        }

        var (supOnly, typOnly) = SplitSupertype(trimmed);
        return (supOnly, typOnly, null);
    }

    private static (string? Supertype, string Type) SplitSupertype(string lhs)
    {
        if (string.IsNullOrWhiteSpace(lhs))
        {
            return (null, string.Empty);
        }

        var tokens = lhs.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var supertypeTokens = new List<string>();
        var i = 0;
        while (i < tokens.Length && KnownSupertypes.Contains(tokens[i]))
        {
            supertypeTokens.Add(tokens[i]);
            i++;
        }

        var typeTokens = tokens[i..];
        var supertype = supertypeTokens.Count == 0 ? null : string.Join(' ', supertypeTokens);
        var type = typeTokens.Length == 0 ? string.Empty : string.Join(' ', typeTokens);
        return (supertype, type);
    }
}
