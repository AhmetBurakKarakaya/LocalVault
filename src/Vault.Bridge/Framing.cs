using System.Buffers.Binary;

namespace Vault.Bridge;

/// <summary>
/// Native messaging çerçevesi: 4 bayt uzunluk (little-endian, yerel bayt sırası) + UTF-8 JSON.
/// Aynı biçim named pipe üzerinde de kullanılır.
/// </summary>
public static class Framing
{
    /// <summary>Bir mesaj okur; akış kapandıysa null döner.</summary>
    public static async Task<byte[]?> ReadMessageAsync(Stream stream, int maxBytes, CancellationToken ct = default)
    {
        var header = new byte[4];
        if (!await ReadExactlyOrEofAsync(stream, header, ct))
            return null;

        var length = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (length < 0 || length > maxBytes)
            throw new InvalidDataException($"Mesaj boyutu geçersiz: {length} bayt (üst sınır {maxBytes}).");

        var body = new byte[length];
        if (!await ReadExactlyOrEofAsync(stream, body, ct))
            throw new EndOfStreamException("Mesaj tamamlanmadan akış kapandı.");
        return body;
    }

    public static async Task WriteMessageAsync(Stream stream, ReadOnlyMemory<byte> message, CancellationToken ct = default)
    {
        if (message.Length > BridgeProtocol.MaxMessageBytes)
            throw new InvalidDataException("Mesaj çok büyük.");

        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, message.Length);
        await stream.WriteAsync(header, ct);
        await stream.WriteAsync(message, ct);
        await stream.FlushAsync(ct);
    }

    private static async Task<bool> ReadExactlyOrEofAsync(Stream stream, Memory<byte> buffer, CancellationToken ct)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer[read..], ct);
            if (n == 0)
            {
                if (read == 0)
                    return false;
                throw new EndOfStreamException("Mesaj tamamlanmadan akış kapandı.");
            }
            read += n;
        }
        return true;
    }
}
