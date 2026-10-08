using System.Text.Json;
using Vault.Core.Models;

namespace Vault.Bridge.Tests;

public class SaveLoginTests
{
    private readonly FakeVault _vault = new();
    private readonly FakeUi _ui = new();
    private readonly BridgeRequestHandler _handler;
    private readonly TestClient _client = new(TimeProvider.System);
    private readonly VaultEntry _github = new()
    {
        Title = "GitHub", Username = "ahmet", Password = "eski-parola", Urls = ["https://github.com"],
    };

    public SaveLoginTests()
    {
        _handler = new BridgeRequestHandler(_vault, _ui);
        _vault.Data.Entries.Add(_github);
        _vault.Data.BrowserAssociations.Add(_client.AsAssociation());
    }

    private static T Payload<T>(BridgeResponse r) =>
        JsonSerializer.Deserialize<T>(r.Payload!, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    [Theory]
    [InlineData("ahmet", "eski-parola", LoginCheckStatus.Exists)]
    [InlineData("AHMET", "eski-parola", LoginCheckStatus.Exists)]     // kullanıcı adı büyük/küçük harf duyarsız
    [InlineData("ahmet", "yeni-parola", LoginCheckStatus.Changed)]
    [InlineData("başka", "x", LoginCheckStatus.New)]
    public async Task CheckLoginReportsStatusWithoutReturningPasswords(string username, string password, string expected)
    {
        var response = await _handler.HandleAsync(_client.Signed("check-login",
            new { url = "https://github.com/session", username, password }));

        _client.AssertSignedResponse(response);
        Assert.DoesNotContain("eski-parola", response.Payload);
        var result = Payload<LoginCheckResult>(response);
        Assert.Equal(expected, result.Status);
        if (expected != LoginCheckStatus.New)
            Assert.Equal(_github.Id.ToString("N"), result.EntryId);
    }

    [Fact]
    public async Task CheckLoginOnOtherSiteIsNew()
    {
        var response = await _handler.HandleAsync(_client.Signed("check-login",
            new { url = "https://github.com.evil.com/login", username = "ahmet", password = "eski-parola" }));
        Assert.Equal(LoginCheckStatus.New, Payload<LoginCheckResult>(response).Status);
    }

    [Fact]
    public async Task SaveLoginCreatesEntryForSiteRoot()
    {
        var response = await _handler.HandleAsync(_client.Signed("save-login",
            new { url = "https://www.ornek.com.tr/hesap/giris?next=/", username = " ahmet@ornek.com ", password = "Yeni!1" }));

        _client.AssertSignedResponse(response);
        var saved = Payload<SavedEntry>(response);
        var entry = _vault.Data.FindById(Guid.Parse(saved.EntryId))!;
        Assert.Equal("ornek.com.tr", entry.Title);
        Assert.Equal(["https://ornek.com.tr"], entry.Urls);
        Assert.Equal("ahmet@ornek.com", entry.Username);
        Assert.Equal("Yeni!1", entry.Password);
        Assert.Equal(1, _vault.SaveCount);
        Assert.Contains("kaydedildi", Assert.Single(_ui.Notifications));
    }

    [Fact]
    public async Task SaveLoginKeepsNonDefaultPort()
    {
        var response = await _handler.HandleAsync(_client.Signed("save-login",
            new { url = "http://localhost:8765/login", username = "u", password = "p" }));
        var entry = _vault.Data.FindById(Guid.Parse(Payload<SavedEntry>(response).EntryId))!;
        Assert.Equal(["http://localhost:8765"], entry.Urls);
    }

    [Fact]
    public async Task UpdatePasswordChangesMatchingEntry()
    {
        var before = _github.UpdatedAt;
        var response = await _handler.HandleAsync(_client.Signed("update-password",
            new { url = "https://github.com/settings", entryId = _github.Id.ToString("N"), password = "yeni-parola" }));

        _client.AssertSignedResponse(response);
        Assert.Equal("yeni-parola", _github.Password);
        Assert.True(_github.UpdatedAt >= before);
        Assert.Equal(1, _vault.SaveCount);
    }

    [Fact]
    public async Task UpdatePasswordRefusesOtherSite()
    {
        var response = await _handler.HandleAsync(_client.Signed("update-password",
            new { url = "https://evil.com", entryId = _github.Id.ToString("N"), password = "ele-geçirildi" }));

        Assert.Equal(ErrorCodes.NotFound, response.Error!.Code);
        Assert.Equal("eski-parola", _github.Password);
        Assert.Equal(0, _vault.SaveCount);
    }

    [Theory]
    [InlineData("save-login", """{"url":"https://a.com","username":"u","password":""}""")]
    [InlineData("save-login", """{"url":"ftp://a.com","username":"u","password":"p"}""")]
    [InlineData("check-login", """{"url":"","username":"u","password":"p"}""")]
    [InlineData("update-password", """{"url":"https://github.com","entryId":"x","password":"p"}""")]
    public async Task InvalidSubmissionsAreRejected(string type, string payload)
    {
        var request = _client.Signed(type, null);
        request.Payload = payload;
        request.Mac = MessageAuth.ComputeRequestMac(_client.Key, request.Type, request.ClientId!, request.Nonce!, request.Ts!.Value, payload);

        Assert.Equal(ErrorCodes.BadRequest, (await _handler.HandleAsync(request)).Error!.Code);
        Assert.Equal(0, _vault.SaveCount);
    }

    [Fact]
    public async Task SavingRequiresPairing()
    {
        var stranger = new TestClient(TimeProvider.System);
        var response = await _handler.HandleAsync(stranger.Signed("save-login",
            new { url = "https://a.com", username = "u", password = "p" }));
        Assert.Equal(ErrorCodes.Unauthorized, response.Error!.Code);
        Assert.Single(_vault.Data.Entries);
    }
}
