namespace LupiraMtgApi.Recognition.Dtos;

public sealed class ScanSetSymbol
{
    public required string SetCode { get; set; }

    public required int HammingDistance { get; set; }

    public required double Score { get; set; }
}