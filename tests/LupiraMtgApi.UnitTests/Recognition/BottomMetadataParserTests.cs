using LupiraMtgApi.Recognition.Domain;
using Xunit;

namespace LupiraMtgApi.UnitTests.Recognition;

public class BottomMetadataParserTests
{
    [Fact]
    public void Modern_layout_with_period_separator_parses_set_number_and_lang()
    {
        var parsed = BottomMetadataParser.Parse("U 0163 LTR . EN >CALDER MOORE");

        Assert.NotNull(parsed);
        Assert.Equal("163", parsed.CollectorNumber);
        Assert.Equal("uncommon", parsed.Rarity);
        Assert.Equal("ltr", parsed.SetCode);
        Assert.Equal("en", parsed.Lang);
    }

    [Fact]
    public void Modern_layout_with_garbled_set_keeps_number_and_rarity_only()
    {
        var parsed = BottomMetadataParser.Parse("U 0163 U 016EN SCARER MOORE");

        Assert.NotNull(parsed);
        Assert.Equal("163", parsed.CollectorNumber);
        Assert.Equal("uncommon", parsed.Rarity);
        Assert.Null(parsed.SetCode);
        Assert.Null(parsed.Lang);
    }

    [Theory]
    [InlineData("229/254 R THB • EN", "229", "rare", "thb", "en")]
    [InlineData("045/280 C STX-JP", "45", "common", "stx", "ja")]
    [InlineData("12/274 M", "12", "mythic", null, null)]
    public void Legacy_layout_parses(string text, string number, string rarity, string? set, string? lang)
    {
        var parsed = BottomMetadataParser.Parse(text);

        Assert.NotNull(parsed);
        Assert.Equal(number, parsed.CollectorNumber);
        Assert.Equal(rarity, parsed.Rarity);
        Assert.Equal(set, parsed.SetCode);
        Assert.Equal(lang, parsed.Lang);
    }

    [Theory]
    [InlineData("")]
    [InlineData("CALDER MOORE")]
    [InlineData("TM & © 2023 Wizards of the Coast")]
    public void Text_without_collector_number_returns_null(string text)
    {
        Assert.Null(BottomMetadataParser.Parse(text));
    }
}
