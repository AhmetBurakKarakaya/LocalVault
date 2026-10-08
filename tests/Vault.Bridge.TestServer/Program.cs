using Vault.Bridge;
using Vault.Core.Models;

// Gerçek tarayıcıyla uçtan uca test için köprü sunucusu: masaüstü uygulamasının yerine geçer.
// Bellek içi örnek kasa kullanır ve eşleştirme isteklerini otomatik onaylar.
//   dotnet run --project tests/Vault.Bridge.TestServer -- <pipe-adı>

Console.OutputEncoding = System.Text.Encoding.UTF8;
var pipeName = args.Length > 0 ? args[0] : BridgeProtocol.DefaultPipeName;
var vault = new MemoryVault();
vault.Data.Entries.AddRange(
[
    new VaultEntry
    {
        Title = "Yerel test sitesi", Username = "e2e-kullanici", Password = "E2E-Parola-42!",
        Urls = ["http://localhost:8765"], Totp = new TotpSettings { Secret = "JBSWY3DPEHPK3PXP" },
    },
    new VaultEntry { Title = "GitHub", Username = "ahmet", Password = "gh", Urls = ["https://github.com"] },
]);

var handler = new BridgeRequestHandler(vault, new AutoApproveUi());
await using var server = new BridgeServer(pipeName, async (request, ct) =>
{
    var response = await handler.HandleAsync(request, ct);
    Console.WriteLine($"{request.Type,-16} → {(response.Ok ? "ok" : response.Error?.Code)}");
    return response;
});
server.Error += ex => Console.WriteLine($"HATA: {ex.Message}");
server.Start();
Console.WriteLine($"HAZIR {pipeName}");

// stdin kapanana kadar çalış (betik süreci sonlandırır).
await Console.In.ReadToEndAsync();

sealed class MemoryVault : IVaultAccess
{
    public bool IsUnlocked => true;
    public VaultData Data { get; } = new();
    public void Save() => Console.WriteLine($"kaydedildi ({Data.BrowserAssociations.Count} tarayıcı bağlantısı)");
}

sealed class AutoApproveUi : IBridgeUi
{
    public Task<bool> ApproveAssociationAsync(string browser, string verificationCode, CancellationToken ct)
    {
        Console.WriteLine($"eşleştirme isteği: {browser}, kod {verificationCode} → otomatik onay");
        return Task.FromResult(true);
    }

    public void RequestUnlock() => Console.WriteLine("kilit açma istendi");
    public void ShowApp() => Console.WriteLine("pencere istendi");
    public void Notify(string message) => Console.WriteLine($"bildirim: {message}");
}
