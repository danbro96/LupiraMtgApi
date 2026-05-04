namespace LupiraMtgApi.Services.SetSymbol;

public sealed class SetSymbolMatch
{
    public required string SetCode { get; set; }

    public required int HammingDistance { get; set; }

    public required double Score { get; set; }
}