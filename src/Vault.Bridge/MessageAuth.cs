using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Vault.Bridge;

/// <summary>
/// İstek/yanıt imzaları. Eklentideki lib/protocol.js aynı kanonik metni üretir;
/// iki tarafın uyumu ortak test vektörleriyle doğrulanır.
/// </summary>
public static class MessageAuth
{
    public const int KeySize = 32;

    /// <summary>type \n clientId \n nonce \n ts \n payload</summary>
    public static string ComputeRequestMac(ReadOnlySpan<byte> key, string type, string clientId, string nonce, long ts, string? payload) =>
        Mac(key, $"{type}\n{clientId}\n{nonce}\n{ts.ToString(CultureInfo.InvariantCulture)}\n{payload ?? ""}");

    /// <summary>id \n ok(1/0) \n payload</summary>
    public static string ComputeResponseMac(ReadOnlySpan<byte> key, string id, bool ok, string? payload) =>
        Mac(key, $"{id}\n{(ok ? "1" : "0")}\n{payload ?? ""}");

    public static bool VerifyRequest(ReadOnlySpan<byte> key, BridgeRequest request)
    {
        if (request.ClientId is null || request.Nonce is null || request.Ts is null || request.Mac is null)
            return false;

        var expected = ComputeRequestMac(key, request.Type, request.ClientId, request.Nonce, request.Ts.Value, request.Payload);
        return FixedTimeEquals(expected, request.Mac);
    }

    public static void SignResponse(ReadOnlySpan<byte> key, BridgeResponse response) =>
        response.Mac = ComputeResponseMac(key, response.Id, response.Ok, response.Payload);

    /// <summary>
    /// Eşleştirme sırasında hem eklentide hem masaüstünde gösterilen kısa kod (ör. "3FA-9C2").
    /// Kullanıcı ikisinin aynı olduğunu görerek doğru eklentiyi onayladığından emin olur.
    /// </summary>
    public static string VerificationCode(ReadOnlySpan<byte> key)
    {
        var hash = SHA256.HashData(key);
        var hex = Convert.ToHexString(hash, 0, 3);
        return $"{hex[..3]}-{hex[3..]}";
    }

    private static string Mac(ReadOnlySpan<byte> key, string message) =>
        Convert.ToBase64String(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(message)));

    private static bool FixedTimeEquals(string expected, string actual)
    {
        var a = Encoding.ASCII.GetBytes(expected);
        var b = Encoding.ASCII.GetBytes(actual);
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }
}
