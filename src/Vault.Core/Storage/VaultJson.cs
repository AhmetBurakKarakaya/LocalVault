using System.Text.Json;
using System.Text.Json.Serialization;
using Vault.Core.Models;

namespace Vault.Core.Storage;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    UseStringEnumConverter = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(VaultFile))]
[JsonSerializable(typeof(VaultData))]
internal sealed partial class VaultJsonContext : JsonSerializerContext;

/// <summary>Kasa modellerinin JSON dönüşümleri.</summary>
public static class VaultJson
{
    // Varsayılan kodlayıcı "+" ve Türkçe karakterleri \uXXXX olarak kaçışlar; dosya HTML'e gömülmediği
    // için gevşek kaçışlama güvenli ve dışa aktarılan JSON okunabilir kalır.
    private static readonly VaultJsonContext Context = new(
        new JsonSerializerOptions(VaultJsonContext.Default.Options)
        {
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        });

    public static byte[] SerializeData(VaultData data) =>
        JsonSerializer.SerializeToUtf8Bytes(data, Context.VaultData);

    public static VaultData DeserializeData(ReadOnlySpan<byte> utf8Json) =>
        Deserialize(utf8Json, Context.VaultData, "Kasa içeriği");

    public static byte[] SerializeFile(VaultFile file) =>
        JsonSerializer.SerializeToUtf8Bytes(file, Context.VaultFile);

    public static VaultFile DeserializeFile(ReadOnlySpan<byte> utf8Json)
    {
        // Dosya bir metin düzenleyicide kaydedildiyse başına UTF-8 BOM eklenmiş olabilir.
        if (utf8Json is [0xEF, 0xBB, 0xBF, ..])
            utf8Json = utf8Json[3..];

        var file = Deserialize(utf8Json, Context.VaultFile, "Kasa dosyası");
        if (file.Format != VaultFile.FormatName)
            throw new VaultFormatException("Bu dosya bir LocalVault kasası değil.");
        if (file.Version != VaultFile.CurrentVersion)
            throw new VaultFormatException($"Desteklenmeyen kasa sürümü: {file.Version}.");
        return file;
    }

    /// <summary>
    /// Şifresiz dışa aktarım. Bu çıktı tüm parolaları açık metin olarak içerir;
    /// yalnızca yedek/taşıma amacıyla ve kullanıcı onayıyla kullanılmalıdır.
    /// </summary>
    public static string ToPlainJson(VaultData data) =>
        JsonSerializer.Serialize(data, Context.VaultData);

    public static VaultData FromPlainJson(string json)
    {
        try
        {
            return JsonSerializer.Deserialize(json, Context.VaultData)
                   ?? throw new VaultFormatException("JSON içeriği boş.");
        }
        catch (JsonException ex)
        {
            throw new VaultFormatException($"JSON okunamadı: {ex.Message}", ex);
        }
    }

    private static T Deserialize<T>(ReadOnlySpan<byte> utf8Json,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo, string what)
    {
        try
        {
            return JsonSerializer.Deserialize(utf8Json, typeInfo)
                   ?? throw new VaultFormatException($"{what} boş.");
        }
        catch (JsonException ex)
        {
            throw new VaultFormatException($"{what} okunamadı: {ex.Message}", ex);
        }
    }
}
