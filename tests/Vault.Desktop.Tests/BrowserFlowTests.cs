using System.Security.Cryptography;
using System.Text.Json;
using Avalonia.Headless.XUnit;
using Vault.Bridge;
using Vault.Core.Storage;
using Vault.Desktop.Services;
using Vault.Desktop.ViewModels;

namespace Vault.Desktop.Tests;

/// <summary>
/// Gerçek köprü işleyicisi + gerçek arayüz ViewModel'leri: eklentiden gelen eşleştirme isteğinin
/// onay katmanına dönüşmesi ve sonucunun kasaya kaydedilmesi.
/// </summary>
public sealed class BrowserFlowTests : IDisposable
{
    private readonly TestEnvironment _env = new();

    public void Dispose() => _env.Dispose();

    /// <summary>App'in IBridgeUi uygulamasının test karşılığı: aynı MainWindowViewModel yöntemlerini çağırır.</summary>
    private sealed class ViewModelBridgeUi(MainWindowViewModel main) : IBridgeUi
    {
        public int UnlockRequests { get; private set; }
        public Task<bool> ApproveAssociationAsync(string browser, string code, CancellationToken ct) =>
            main.RequestBrowserApprovalAsync(browser, code, ct);
        public void RequestUnlock()
        {
            UnlockRequests++;
            main.ShowBrowserUnlockHint();
        }
        public void ShowApp() { }
        public void Notify(string message) => main.Notify(message);
    }

    private async Task<(MainWindowViewModel Main, VaultViewModel Vault, BridgeRequestHandler Handler, ViewModelBridgeUi Ui)> SetupAsync()
    {
        _env.SeedVault(TestEnvironment.SampleEntries());
        var main = new MainWindowViewModel(_env.Services);
        var unlock = (UnlockViewModel)main.CurrentPage;
        unlock.Password = TestEnvironment.MasterPassword;
        await unlock.UnlockCommand.ExecuteAsync(null);
        var ui = new ViewModelBridgeUi(main);
        var handler = new BridgeRequestHandler(new DesktopVaultAccess(_env.Services.Vault), ui);
        return (main, (VaultViewModel)main.CurrentPage, handler, ui);
    }

    private static BridgeRequest Associate(byte[] key, string clientId = "client-1") => new()
    {
        Id = "a1",
        Type = RequestTypes.Associate,
        ClientId = clientId,
        Payload = JsonSerializer.Serialize(new { key = Convert.ToBase64String(key), browser = "Chrome" }),
    };

    [AvaloniaFact]
    public async Task ApprovingPairingStoresAssociationEncryptedInVault()
    {
        var (_, vault, handler, _) = await SetupAsync();
        var key = RandomNumberGenerator.GetBytes(32);

        var pending = handler.HandleAsync(Associate(key));
        var approval = Assert.IsType<BrowserApprovalViewModel>(vault.Overlay);
        Assert.Equal("Chrome", approval.Browser);
        Assert.Equal(MessageAuth.VerificationCode(key), approval.VerificationCode);
        Assert.False(pending.IsCompleted);

        approval.ApproveCommand.Execute(null);
        var response = await pending;

        Assert.True(response.Ok, response.Error?.Message);
        Assert.Null(vault.Overlay);
        Assert.Contains("Chrome eklentisi bağlandı", vault.StatusMessage);

        Assert.DoesNotContain(Convert.ToBase64String(key), File.ReadAllText(_env.VaultPath));
        using var session = VaultSession.Open(_env.VaultPath, TestEnvironment.MasterPassword);
        Assert.Equal("client-1", Assert.Single(session.Data.BrowserAssociations).ClientId);
    }

    [AvaloniaFact]
    public async Task DenyingOrDismissingPairingRejectsIt()
    {
        var (_, vault, handler, _) = await SetupAsync();

        var denied = handler.HandleAsync(Associate(RandomNumberGenerator.GetBytes(32)));
        ((BrowserApprovalViewModel)vault.Overlay!).DenyCommand.Execute(null);
        Assert.Equal(ErrorCodes.Denied, (await denied).Error!.Code);

        // Esc ile kapatmak da reddetmek demektir.
        var dismissed = handler.HandleAsync(Associate(RandomNumberGenerator.GetBytes(32)));
        Assert.IsType<BrowserApprovalViewModel>(vault.Overlay);
        vault.EscapeCommand.Execute(null);
        Assert.Equal(ErrorCodes.Denied, (await dismissed).Error!.Code);

        Assert.Empty(_env.Services.Vault.Session.Data.BrowserAssociations);
    }

    [AvaloniaFact]
    public async Task LockingWhileApprovalIsPendingRejectsIt()
    {
        var (main, vault, handler, _) = await SetupAsync();
        var pending = handler.HandleAsync(Associate(RandomNumberGenerator.GetBytes(32)));
        Assert.IsType<BrowserApprovalViewModel>(vault.Overlay);

        await vault.LockCommand.ExecuteAsync(null);

        var response = await pending;
        Assert.False(response.Ok);
        Assert.IsType<UnlockViewModel>(main.CurrentPage);
    }

    [AvaloniaFact]
    public async Task ApprovalTimesOutWhenCancelled()
    {
        var (_, vault, _, _) = await SetupAsync();
        using var cts = new CancellationTokenSource();

        var pending = vault.RequestBrowserApprovalAsync("Firefox", "ABC-123", cts.Token);
        cts.Cancel();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.False(await pending);
        Assert.Null(vault.Overlay);
    }

    [AvaloniaFact]
    public async Task LockedVaultShowsHintOnUnlockScreen()
    {
        var (main, vault, handler, ui) = await SetupAsync();
        await vault.LockCommand.ExecuteAsync(null);

        var response = await handler.HandleAsync(new BridgeRequest
        {
            Id = "x", Type = RequestTypes.GetLogins, ClientId = "c", Nonce = "n", Ts = 1, Mac = "m",
            Payload = """{"url":"https://github.com","interactive":true}""",
        });

        Assert.Equal(ErrorCodes.Locked, response.Error!.Code);
        Assert.Equal(1, ui.UnlockRequests);
        Assert.Contains("Tarayıcı eklentisi", ((UnlockViewModel)main.CurrentPage).Hint);
    }

    [AvaloniaFact]
    public async Task RemovingAssociationInSettingsRevokesAccess()
    {
        var (_, vault, handler, _) = await SetupAsync();
        var key = RandomNumberGenerator.GetBytes(32);
        var pending = handler.HandleAsync(Associate(key));
        ((BrowserApprovalViewModel)vault.Overlay!).ApproveCommand.Execute(null);
        Assert.True((await pending).Ok);

        vault.OpenSettingsCommand.Execute(null);
        var settings = (SettingsViewModel)vault.Overlay!;
        var item = Assert.Single(settings.Associations);
        Assert.Equal("Chrome", item.Name);
        item.RemoveCommand.Execute(null);
        Assert.False(settings.HasAssociations);

        var request = new BridgeRequest
        {
            Id = "r", Type = RequestTypes.TestAssociate, ClientId = "client-1", Nonce = "n2",
            Ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };
        request.Mac = MessageAuth.ComputeRequestMac(key, request.Type, request.ClientId, request.Nonce, request.Ts.Value, null);
        Assert.Equal(ErrorCodes.Unauthorized, (await handler.HandleAsync(request)).Error!.Code);

        using var session = VaultSession.Open(_env.VaultPath, TestEnvironment.MasterPassword);
        Assert.Empty(session.Data.BrowserAssociations);
    }
}
