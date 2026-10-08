using System.Text;
using Vault.Core.Models;

namespace Vault.Core.Otp;

/// <summary>
/// Google Authenticator "Hesapları aktar" QR kodu: <c>otpauth-migration://offline?data=…</c>.
/// İçerik Base64 kodlu bir protobuf mesajıdır (MigrationPayload). Harici protobuf kütüphanesi
/// gerektirmesin diye gereken alanlar elle çözülür.
/// </summary>
public static class OtpMigration
{
    public const string Scheme = "otpauth-migration";

    public sealed record Result(IReadOnlyList<OtpAuthInfo> Accounts, IReadOnlyList<string> Warnings);

    public static bool IsMigrationUri(string value) =>
        value.TrimStart().StartsWith(Scheme + "://", StringComparison.OrdinalIgnoreCase);

    public static Result Parse(string uri)
    {
        if (!Uri.TryCreate(uri.Trim(), UriKind.Absolute, out var parsed) || !parsed.Scheme.Equals(Scheme, StringComparison.OrdinalIgnoreCase))
            throw new FormatException("Geçerli bir otpauth-migration:// adresi değil.");

        var data = parsed.Query.TrimStart('?').Split('&')
            .Select(p => p.Split('=', 2))
            .FirstOrDefault(p => p[0] == "data")?.ElementAtOrDefault(1);
        if (string.IsNullOrEmpty(data))
            throw new FormatException("Aktarım adresinde 'data' parametresi yok.");

        byte[] payload;
        try
        {
            var base64 = Uri.UnescapeDataString(data).Replace(' ', '+').Replace('-', '+').Replace('_', '/');
            payload = Convert.FromBase64String(base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '='));
        }
        catch (FormatException ex)
        {
            throw new FormatException("Aktarım verisi çözülemedi.", ex);
        }

        var accounts = new List<OtpAuthInfo>();
        var warnings = new List<string>();
        var reader = new ProtoReader(payload);
        while (reader.Next(out var field, out var wireType))
        {
            if (field == 1 && wireType == 2)
                ReadAccount(reader.ReadBytes(), accounts, warnings);
            else
                reader.Skip(wireType);
        }
        return new Result(accounts, warnings);
    }

    private static void ReadAccount(ReadOnlyMemory<byte> bytes, List<OtpAuthInfo> accounts, List<string> warnings)
    {
        byte[] secret = [];
        string? name = null, issuer = null;
        long algorithm = 0, digits = 0, type = 0;

        var reader = new ProtoReader(bytes);
        while (reader.Next(out var field, out var wireType))
        {
            switch (field, wireType)
            {
                case (1, 2): secret = reader.ReadBytes().ToArray(); break;
                case (2, 2): name = Encoding.UTF8.GetString(reader.ReadBytes().Span); break;
                case (3, 2): issuer = Encoding.UTF8.GetString(reader.ReadBytes().Span); break;
                case (4, 0): algorithm = reader.ReadVarint(); break;
                case (5, 0): digits = reader.ReadVarint(); break;
                case (6, 0): type = reader.ReadVarint(); break;
                default: reader.Skip(wireType); break;
            }
        }

        // Ad bazen "Issuer:hesap" biçimindedir.
        if (name is not null && name.Contains(':') && string.IsNullOrEmpty(issuer))
        {
            issuer = name[..name.IndexOf(':')].Trim();
            name = name[(name.IndexOf(':') + 1)..].Trim();
        }
        var label = issuer ?? name ?? "(adsız)";

        if (type == 1)
        {
            warnings.Add($"{label}: sayaç tabanlı (HOTP) hesaplar desteklenmiyor, atlandı.");
            return;
        }
        if (secret.Length == 0)
        {
            warnings.Add($"{label}: gizli anahtar yok, atlandı.");
            return;
        }

        var settings = new TotpSettings
        {
            Secret = Base32.Encode(secret, padding: false),
            Digits = digits == 2 ? 8 : 6,
            Algorithm = algorithm switch
            {
                2 => OtpHashAlgorithm.SHA256,
                3 => OtpHashAlgorithm.SHA512,
                _ => OtpHashAlgorithm.SHA1,
            },
        };
        if (algorithm == 4)
        {
            warnings.Add($"{label}: MD5 algoritması desteklenmiyor, atlandı.");
            return;
        }
        accounts.Add(new OtpAuthInfo(settings, string.IsNullOrWhiteSpace(issuer) ? null : issuer,
            string.IsNullOrWhiteSpace(name) ? null : name));
    }

    /// <summary>Minimal protobuf okuyucu (varint ve uzunluk önekli alanlar).</summary>
    private sealed class ProtoReader(ReadOnlyMemory<byte> data)
    {
        private int _position;

        public bool Next(out int field, out int wireType)
        {
            field = wireType = 0;
            if (_position >= data.Length)
                return false;
            var tag = ReadVarint();
            field = (int)(tag >> 3);
            wireType = (int)(tag & 7);
            return true;
        }

        public long ReadVarint()
        {
            long result = 0;
            for (var shift = 0; shift < 64; shift += 7)
            {
                if (_position >= data.Length)
                    throw new FormatException("Aktarım verisi bozuk (eksik varint).");
                var b = data.Span[_position++];
                result |= (long)(b & 0x7F) << shift;
                if ((b & 0x80) == 0)
                    return result;
            }
            throw new FormatException("Aktarım verisi bozuk (uzun varint).");
        }

        public ReadOnlyMemory<byte> ReadBytes()
        {
            var length = ReadVarint();
            if (length < 0 || _position + length > data.Length)
                throw new FormatException("Aktarım verisi bozuk (uzunluk).");
            var slice = data.Slice(_position, (int)length);
            _position += (int)length;
            return slice;
        }

        public void Skip(int wireType)
        {
            switch (wireType)
            {
                case 0: ReadVarint(); break;
                case 1: _position += 8; break;
                case 2: ReadBytes(); break;
                case 5: _position += 4; break;
                default: throw new FormatException($"Aktarım verisi bozuk (tür {wireType}).");
            }
        }
    }
}
