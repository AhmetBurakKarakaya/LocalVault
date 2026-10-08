using System.Security.Cryptography;
using System.Text.Json;
using Vault.Core.Models;

namespace Vault.Bridge.Tests;

public sealed class FakeVault : IVaultAccess
{
    public bool IsUnlocked { get; set; } = true;
    public VaultData Data { get; } = new();
    public int SaveCount { get; private set; }
    public void Save() => SaveCount++;
}

public sealed class FakeUi : IBridgeUi
{
    public bool ApproveResult { get; set; } = true;
    /// <summary>true ise onay hiç gelmez (zaman aşımı testleri için).</summary>
    public bool NeverAnswer { get; set; }
    public List<(string Browser, string Code)> ApprovalRequests { get; } = [];
    public int UnlockRequests { get; private set; }
    public int ShowAppRequests { get; private set; }
    public List<string> Notifications { get; } = [];

    public async Task<bool> ApproveAssociationAsync(string browser, string verificationCode, CancellationToken ct)
    {
        ApprovalRequests.Add((browser, verificationCode));
        if (NeverAnswer)
            await Task.Delay(Timeout.Infinite, ct);
        return ApproveResult;
    }

    public void RequestUnlock() => UnlockRequests++;
    public void ShowApp() => ShowAppRequests++;
    public void Notify(string message) => Notifications.Add(message);
}

public sealed class ManualTime(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>Eklentinin yaptığı gibi imzalı istek üreten test istemcisi.</summary>
public sealed class TestClient(TimeProvider time)
{
    private int _counter;

    public string ClientId { get; } = "client-" + Guid.NewGuid().ToString("N")[..8];
    public byte[] Key { get; } = RandomNumberGenerator.GetBytes(32);

    public BridgeRequest Associate(string browser = "Chrome") => new()
    {
        Id = NextId(),
        Type = RequestTypes.Associate,
        ClientId = ClientId,
        Payload = JsonSerializer.Serialize(new { key = Convert.ToBase64String(Key), browser }),
    };

    public BridgeRequest Signed(string type, object? payload, string? nonce = null, long? ts = null)
    {
        var request = new BridgeRequest
        {
            Id = NextId(),
            Type = type,
            ClientId = ClientId,
            Nonce = nonce ?? Guid.NewGuid().ToString("N"),
            Ts = ts ?? time.GetUtcNow().ToUnixTimeSeconds(),
            Payload = payload is null ? null : JsonSerializer.Serialize(payload),
        };
        request.Mac = MessageAuth.ComputeRequestMac(Key, request.Type, request.ClientId, request.Nonce, request.Ts.Value, request.Payload);
        return request;
    }

    public BrowserAssociation AsAssociation(string name = "Chrome") => new()
    {
        ClientId = ClientId,
        Key = Convert.ToBase64String(Key),
        Name = name,
    };

    public void AssertSignedResponse(BridgeResponse response)
    {
        Assert.True(response.Ok, response.Error?.Message);
        Assert.Equal(MessageAuth.ComputeResponseMac(Key, response.Id, response.Ok, response.Payload), response.Mac);
    }

    private string NextId() => $"req-{Interlocked.Increment(ref _counter)}";
}
