using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

namespace LupiraMtgApi.Auth;

/// <summary>
/// Helpers for the PoC device-token scheme. Tokens are 32 random bytes encoded as
/// URL-safe base64 with a `lmtg_` prefix. They are stored hashed (SHA-256) in
/// `me_devices.TokenHash` — never as plaintext — so a database leak does not expose
/// session tokens.
/// </summary>
public static class DeviceTokens
{
    public const string Prefix = "lmtg_";

    public static (string Token, string TokenHash) Mint()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        var token = Prefix + Base64UrlEncode(bytes);
        var hash = HashToken(token);
        return (token, hash);
    }

    public static string HashToken(string token)
    {
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(Encoding.UTF8.GetBytes(token), hash);
        return Convert.ToHexStringLower(hash);
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes)
    {
        var buffer = new char[Base64.GetMaxEncodedToUtf8Length(bytes.Length)];
        Convert.TryToBase64Chars(bytes, buffer, out var written);
        var sb = new StringBuilder(written);
        for (var i = 0; i < written; i++)
        {
            var c = buffer[i];
            if (c == '=') break;
            sb.Append(c switch
            {
                '+' => '-',
                '/' => '_',
                _ => c,
            });
        }

        return sb.ToString();
    }
}
