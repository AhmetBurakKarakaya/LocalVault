using System.Buffers.Binary;
using System.Text;

namespace Vault.Bridge.Tests;

public class MessageAuthTests
{
    // Bu vektörler bağımsız olarak (Python hmac) hesaplandı ve eklentinin
    // extension/test/protocol.test.js dosyasında da aynen kullanılıyor.
    private static readonly byte[] Key = Enumerable.Range(0, 32).Select(i => (byte)i).ToArray();

    [Fact]
    public void RequestMacMatchesSharedVector()
    {
        var mac = MessageAuth.ComputeRequestMac(Key, "get-logins", "client-1", "nonce-1", 1700000000,
            """{"url":"https://github.com/login","interactive":true}""");
        Assert.Equal("uCHXrrgV8znWcMX86B+YcwuGXLtHpTobQiyLfZObhJg=", mac);
    }

    [Fact]
    public void ResponseMacMatchesSharedVector()
    {
        Assert.Equal("XkaDp6Pnc0KKpK6JUXwrqw1t5TKJIxNrMUUUb2Gs+78=",
            MessageAuth.ComputeResponseMac(Key, "req-42", true, """{"logins":[]}"""));
    }

    [Fact]
    public void VerificationCodeMatchesSharedVector()
    {
        Assert.Equal("630-DCD", MessageAuth.VerificationCode(Key));
    }

    [Fact]
    public void VerifyRejectsTamperedFields()
    {
        var request = new BridgeRequest
        {
            Id = "1", Type = "get-credentials", ClientId = "c", Nonce = "n", Ts = 100,
            Payload = """{"url":"https://a.com","entryId":"x"}""",
        };
        request.Mac = MessageAuth.ComputeRequestMac(Key, request.Type, request.ClientId, request.Nonce, request.Ts.Value, request.Payload);
        Assert.True(MessageAuth.VerifyRequest(Key, request));

        request.Payload = """{"url":"https://evil.com","entryId":"x"}""";
        Assert.False(MessageAuth.VerifyRequest(Key, request));

        request.Payload = """{"url":"https://a.com","entryId":"x"}""";
        request.Ts = 101;
        Assert.False(MessageAuth.VerifyRequest(Key, request));

        request.Ts = 100;
        Assert.False(MessageAuth.VerifyRequest(new byte[32], request));

        request.Mac = null;
        Assert.False(MessageAuth.VerifyRequest(Key, request));
    }
}

public class FramingTests
{
    [Fact]
    public async Task RoundTripsMessages()
    {
        using var stream = new MemoryStream();
        await Framing.WriteMessageAsync(stream, Encoding.UTF8.GetBytes("""{"a":"ş"}"""));
        await Framing.WriteMessageAsync(stream, Encoding.UTF8.GetBytes("{}"));
        stream.Position = 0;

        Assert.Equal("""{"a":"ş"}""", Encoding.UTF8.GetString((await Framing.ReadMessageAsync(stream, 1024))!));
        Assert.Equal("{}", Encoding.UTF8.GetString((await Framing.ReadMessageAsync(stream, 1024))!));
        Assert.Null(await Framing.ReadMessageAsync(stream, 1024));
    }

    [Fact]
    public async Task HeaderIsLittleEndianLength()
    {
        using var stream = new MemoryStream();
        await Framing.WriteMessageAsync(stream, new byte[300]);
        Assert.Equal(300, BinaryPrimitives.ReadInt32LittleEndian(stream.ToArray().AsSpan(0, 4)));
    }

    [Fact]
    public async Task RejectsOversizedAndNegativeLengths()
    {
        var header = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(header, 2048);
        await Assert.ThrowsAsync<InvalidDataException>(() => Framing.ReadMessageAsync(new MemoryStream(header), 1024));

        BinaryPrimitives.WriteInt32LittleEndian(header, -1);
        await Assert.ThrowsAsync<InvalidDataException>(() => Framing.ReadMessageAsync(new MemoryStream(header), 1024));
    }

    [Fact]
    public async Task TruncatedMessageThrows()
    {
        var data = new byte[4 + 3];
        BinaryPrimitives.WriteInt32LittleEndian(data, 10);
        await Assert.ThrowsAsync<EndOfStreamException>(() => Framing.ReadMessageAsync(new MemoryStream(data), 1024));
    }
}
