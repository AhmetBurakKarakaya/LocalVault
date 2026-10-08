using System.Buffers.Binary;
using System.Security.Cryptography;
using Vault.Core.Models;

namespace Vault.Core.Otp;

public enum OtpHashAlgorithm
{
    SHA1,
    SHA256,
    SHA512,
}

public readonly record struct TotpCode(string Code, int RemainingSeconds, int Period);

/// <summary>HOTP (RFC 4226) ve TOTP (RFC 6238) kod üretimi.</summary>
public static class OtpGenerator
{
    public static string ComputeHotp(ReadOnlySpan<byte> secret, long counter, int digits, OtpHashAlgorithm algorithm)
    {
        if (digits is < 1 or > TotpSettings.MaxDigits)
            throw new ArgumentOutOfRangeException(nameof(digits));

        Span<byte> counterBytes = stackalloc byte[8];
        BinaryPrimitives.WriteInt64BigEndian(counterBytes, counter);

        var hash = algorithm switch
        {
            OtpHashAlgorithm.SHA1 => HMACSHA1.HashData(secret, counterBytes),
            OtpHashAlgorithm.SHA256 => HMACSHA256.HashData(secret, counterBytes),
            OtpHashAlgorithm.SHA512 => HMACSHA512.HashData(secret, counterBytes),
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm)),
        };

        // Dinamik kırpma (RFC 4226 §5.3)
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24)
                     | (hash[offset + 1] << 16)
                     | (hash[offset + 2] << 8)
                     | hash[offset + 3];

        var modulo = (long)Math.Pow(10, digits);
        return (binary % modulo).ToString().PadLeft(digits, '0');
    }

    public static TotpCode ComputeTotp(TotpSettings settings, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();

        var secret = Base32.Decode(settings.Secret);
        try
        {
            var unixSeconds = now.ToUnixTimeSeconds();
            var counter = unixSeconds / settings.Period;
            var remaining = settings.Period - (int)(unixSeconds % settings.Period);
            var code = ComputeHotp(secret, counter, settings.Digits, settings.Algorithm);
            return new TotpCode(code, remaining, settings.Period);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(secret);
        }
    }

    public static TotpCode ComputeTotp(TotpSettings settings) => ComputeTotp(settings, DateTimeOffset.UtcNow);
}
