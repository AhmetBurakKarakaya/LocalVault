using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Vault.Core.Models;

namespace Vault.Bridge.Tests;

/// <summary>
/// Gerçek LocalVault.NativeHost.exe sürecini başlatır, ona tarayıcının yaptığı gibi native messaging
/// çerçeveleri gönderir ve yanıtların gerçek named pipe sunucusundan geldiğini doğrular.
/// </summary>
public sealed class EndToEndTests : IAsyncLifetime
{
    private readonly string _pipeName = "LocalVault.Test." + Guid.NewGuid().ToString("N");
    private readonly FakeVault _vault = new();
    private readonly FakeUi _ui = new();
    private BridgeServer? _server;
    private Process? _host;

    private static string HostPath => Path.Combine(AppContext.BaseDirectory,
        OperatingSystem.IsWindows() ? "LocalVault.NativeHost.exe" : "LocalVault.NativeHost");

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_host is { HasExited: false })
        {
            _host.StandardInput.Close();
            if (!_host.WaitForExit(5000))
                _host.Kill();
        }
        _host?.Dispose();
        if (_server is not null)
            await _server.DisposeAsync();
    }

    private void StartServer()
    {
        var handler = new BridgeRequestHandler(_vault, _ui);
        _server = new BridgeServer(_pipeName, (request, ct) => handler.HandleAsync(request, ct));
        _server.Start();
    }

    private void StartHost()
    {
        var psi = new ProcessStartInfo(HostPath, "chrome-extension://test/")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.Environment["LOCALVAULT_PIPE_NAME"] = _pipeName;
        _host = Process.Start(psi)!;
    }

    private async Task SendAsync(object message)
    {
        var bytes = message is BridgeRequest request
            ? BridgeProtocol.Serialize(request)
            : Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));
        await Framing.WriteMessageAsync(_host!.StandardInput.BaseStream, bytes);
    }

    private async Task<JsonObject> ReceiveAsync()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var bytes = await Framing.ReadMessageAsync(_host!.StandardOutput.BaseStream, BridgeProtocol.MaxMessageBytes, timeout.Token);
        Assert.NotNull(bytes);
        return JsonNode.Parse(bytes)!.AsObject();
    }

    [Fact]
    public async Task HostReportsAppNotRunningWhenNoServer()
    {
        StartHost();
        await SendAsync(new { id = "p1", type = "ping" });

        var response = await ReceiveAsync();
        Assert.Equal("p1", response["id"]!.GetValue<string>());
        Assert.False(response["ok"]!.GetValue<bool>());
        Assert.Equal(ErrorCodes.AppNotRunning, response["error"]!["code"]!.GetValue<string>());
    }

    [Fact]
    public async Task FullFlowThroughRealHostProcess()
    {
        var client = new TestClient(TimeProvider.System);
        var entry = new VaultEntry { Title = "GitHub", Username = "ahmet", Password = "p@ss", Urls = ["https://github.com"] };
        _vault.Data.Entries.Add(entry);
        StartServer();
        StartHost();

        await SendAsync(new { id = "p1", type = "ping" });
        Assert.True((await ReceiveAsync())["ok"]!.GetValue<bool>());

        // Eşleştirme → imzalı istekler
        await SendAsync(client.Associate("Chrome"));
        var associate = await ReceiveAsync();
        Assert.True(associate["ok"]!.GetValue<bool>(), associate.ToJsonString());

        await SendAsync(client.Signed("get-logins", new { url = "https://github.com/login" }));
        var logins = await ReceiveAsync();
        Assert.Contains("ahmet", logins["payload"]!.GetValue<string>());

        await SendAsync(client.Signed("get-credentials", new { url = "https://github.com/login", entryId = entry.Id.ToString("N") }));
        var credentials = await ReceiveAsync();
        var response = BridgeProtocol.TryParseResponse(Encoding.UTF8.GetBytes(credentials.ToJsonString()))!;
        client.AssertSignedResponse(response);
        Assert.Contains("p@ss", response.Payload);
    }

    [Fact]
    public async Task ConcurrentRequestsDoNotBlockEachOther()
    {
        // Onay bekleyen bir eşleştirme isteği varken ping hemen yanıtlanmalı.
        _ui.NeverAnswer = true;
        StartServer();
        StartHost();

        await SendAsync(new TestClient(TimeProvider.System).Associate());
        await SendAsync(new { id = "p2", type = "ping" });

        var first = await ReceiveAsync();
        Assert.Equal("p2", first["id"]!.GetValue<string>());
    }

    [Fact]
    public async Task HostRepliesWhenAppGoesAwayAndReconnectsLater()
    {
        StartServer();
        StartHost();
        await SendAsync(new { id = "p1", type = "ping" });
        Assert.True((await ReceiveAsync())["ok"]!.GetValue<bool>());

        await _server!.DisposeAsync();
        _server = null;
        await Task.Delay(300);

        await SendAsync(new { id = "p2", type = "ping" });
        Assert.Equal(ErrorCodes.AppNotRunning, (await ReceiveAsync())["error"]!["code"]!.GetValue<string>());

        StartServer();
        await Task.Delay(300);
        await SendAsync(new { id = "p3", type = "ping" });
        Assert.True((await ReceiveAsync())["ok"]!.GetValue<bool>());
    }

    [Fact]
    public async Task HostExitsWhenBrowserClosesStdin()
    {
        StartHost();
        _host!.StandardInput.Close();
        Assert.True(_host.WaitForExit(5000));
        Assert.Equal(0, _host.ExitCode);
    }
}
