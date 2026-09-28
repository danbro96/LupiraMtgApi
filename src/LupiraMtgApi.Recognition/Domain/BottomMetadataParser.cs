using System.Text.RegularExpressions;

namespace LupiraMtgApi.Recognition.Domain;

/// <summary>
/// Parses the OCR'd bottom-left collector line. Handles both layouts:
/// legacy <c>229/254 R</c> + <c>THB • EN</c>, and 2023+ <c>U 0163</c> + <c>LTR • EN</c> (no set total, rarity
/// letter first). OCR routinely reads the bullet as a period or drops it, so the separator is optional and the
/// language is restricted to printed codes to keep that loose separator from matching arbitrary words.
/// </summary>
public static partial class BottomMetadataParser
{
    private static readonly Dictionary<string, string> PrintedLangToScryfall = new(StringComparer.Ordinal)
    {
        ["EN"] = "en",
        ["ES"] = "es",
        ["SP"] = "es",
        ["FR"] = "fr",
        ["DE"] = "de",
        ["IT"] = "it",
        ["PT"] = "pt",
        ["JP"] = "ja",
        ["JA"] = "ja",
        ["KO"] = "ko",
        ["RU"] = "ru",
        ["CS"] = "zhs",
        ["CT"] = "zht",
        ["PH"] = "ph",
    };

    public static BottomMetadata? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string number;
        string? rarityLetter;
        var legacy = LegacyCollectorRegex().Match(text);
        if (legacy.Success)
        {
            number = legacy.Groups["num"].Value;
            rarityLetter = legacy.Groups["rarity"].Success ? legacy.Groups["rarity"].Value : null;
        }
        else
        {
            var modern = ModernCollectorRegex().Match(text);
            if (!modern.Success)
            {
                return null;
            }

            number = modern.Groups["num"].Value;
            rarityLetter = modern.Groups["rarity"].Value;
        }

        var collectorNumber = number.TrimStart('0');
        var setLang = SetLangRegex().Match(text);

        return new BottomMetadata
        {
            CollectorNumber = collectorNumber.Length > 0 ? collectorNumber : number,
            Rarity = rarityLetter is null ? null : MapRarity(rarityLetter.ToUpperInvariant()),
            SetCode = setLang.Success ? setLang.Groups["set"].Value.ToLowerInvariant() : null,
            Lang = setLang.Success ? PrintedLangToScryfall[setLang.Groups["lang"].Value] : null,
        };
    }

    private static string? MapRarity(string letter) => letter switch
    {
        "C" => "common",
        "U" => "uncommon",
        "R" => "rare",
        "M" => "mythic",
        "S" => "special",
        "L" => "common",
        _ => null,
    };

    [GeneratedRegex(@"(?<num>\d{1,4})\s*/\s*\d{1,4}\s*(?<rarity>[CURMS])?", RegexOptions.IgnoreCase)]
    private static partial Regex LegacyCollectorRegex();

    [GeneratedRegex(@"\b(?<rarity>[CURMSL])\s*(?<num>\d{3,4})\b")]
    private static partial Regex ModernCollectorRegex();

    // Set must start with a letter so a digit run glued to the language ("016EN") isn't read as a set code.
    [GeneratedRegex(@"\b(?<set>[A-Z][A-Z0-9]{2,4})\s*[•·.\-‐‒*,]?\s*(?<lang>EN|ES|SP|FR|DE|IT|PT|JP|JA|KO|RU|CS|CT|PH)\b")]
    private static partial Regex SetLangRegex();
}
