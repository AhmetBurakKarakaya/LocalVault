using System.Text.Json;
using Vault.Core.Models;

namespace Vault.Bridge.Tests;

public class HandlerTests
{
    private readonly FakeVault _vault = new();
    private readonly FakeUi _ui = new();
    private readonly ManualTime _time = new(DateTimeOffset.FromUnixTimeSeconds(1_800_000_000));
    private readonly BridgeRequestHandler _handler;
    private readonly TestClient _client;

    private readonly VaultEntry _github = new()
    {
        Title = "GitHub", Username = "ahmet", Password = "gh-secret",
        Urls = ["https://github.com"], Totp = new TotpSettings { Secret = "JBSWY3DPEHPK3PXP" },
    };
    private readonly VaultEntry _bank = new()
    {
        Title = "Banka", Username = "12345", Password = "bank-secret", Urls = ["https://bank.com.tr"],
    };

    public HandlerTests()
    {
        _handler = new BridgeRequestHandler(_vault, _ui, _time);
        _client = new TestClient(_time);
        _vault.Data.Entries.AddRange([_github, _bank]);
    }

    private void Pair() => _vault.Data.BrowserAssociations.Add(_client.AsAssociation());

    private static T Payload<T>(BridgeResponse response) => JsonSerializer.Deserialize<T>(response.Payload!,
        new JsonSerializerOptions(JsonSerializerDefaults.Web))!;

    // ---- ping / eşleştirme ----

    [Fact]
    public async Task PingReportsLockStateWithoutAuthentication()
    {
        var response = await _handler.HandleAsync(new BridgeRequest { Id = "1", Type = "ping" });
        Assert.True(response.Ok);
        Assert.False(Payload<PingResult>(response).Locked);

        _vault.IsUnlocked = false;
        response = await _handler.HandleAsync(new BridgeRequest { Id = "2", Type = "ping" });
        Assert.True(Payload<PingResult>(response).Locked);
    }

    [Fact]
    public async Task ApprovedAssociationIsStoredAndUsable()
    {
        var response = await _handler.HandleAsync(_client.Associate("Chrome"));

        _client.AssertSignedResponse(response);
        var (browser, code) = Assert.Single(_ui.ApprovalRequests);
        Assert.Equal("Chrome", browser);
        Assert.Equal(MessageAuth.VerificationCode(_client.Key), code);

        var stored = Assert.Single(_vault.Data.BrowserAssociations);
        Assert.Equal(_client.ClientId, stored.ClientId);
        Assert.Equal(1, _vault.SaveCount);

        _client.AssertSignedResponse(await _handler.HandleAsync(_client.Signed("test-associate", null)));
    }

    [Fact]
    public async Task DeniedAssociationIsNotStored()
    {
        _ui.ApproveResult = false;
        var response = await _handler.HandleAsync(_client.Associate());

        Assert.Equal(ErrorCodes.Denied, response.Error!.Code);
        Assert.Empty(_vault.Data.BrowserAssociations);
        Assert.Equal(0, _vault.SaveCount);
    }

    [Fact]
    public async Task AssociationWhileLockedAsksToUnlock()
    {
        _vault.IsUnlocked = false;
        var response = await _handler.HandleAsync(_client.Associate());

        Assert.Equal(ErrorCodes.Locked, response.Error!.Code);
        Assert.Equal(1, _ui.UnlockRequests);
        Assert.Empty(_ui.ApprovalRequests);
    }

    [Theory]
    [InlineData("""{"key":"kısa","browser":"Chrome"}""")]
    [InlineData("""{"key":"AAAA","browser":"Chrome"}""")]       // 3 bayt
    [InlineData("""{"browser":"Chrome"}""")]
    [InlineData("bozuk json")]
    public async Task InvalidAssociationRequestsAreRejected(string payload)
    {
        var request = _client.Associate();
        request.Payload = payload;
        var response = await _handler.HandleAsync(request);

        Assert.Equal(ErrorCodes.BadRequest, response.Error!.Code);
        Assert.Empty(_ui.ApprovalRequests);
    }

    [Fact]
    public async Task SecondBrowserGetsUniqueName()
    {
        _vault.Data.BrowserAssociations.Add(new TestClient(_time).AsAssociation("Chrome"));
        var response = await _handler.HandleAsync(_client.Associate("Chrome"));

        Assert.Equal("Chrome (2)", Payload<AssociateResult>(response).Name);
    }

    // ---- kimlik doğrulama ----

    [Fact]
    public async Task UnpairedClientIsUnauthorized()
    {
        var response = await _handler.HandleAsync(_client.Signed("get-logins", new { url = "https://github.com" }));
        Assert.Equal(ErrorCodes.Unauthorized, response.Error!.Code);
    }

    [Fact]
    public async Task WrongKeyIsUnauthorized()
    {
        _vault.Data.BrowserAssociations.Add(new BrowserAssociation
        {
            ClientId = _client.ClientId,
            Key = Convert.ToBase64String(new byte[32]),
        });
        var response = await _handler.HandleAsync(_client.Signed("get-logins", new { url = "https://github.com" }));
        Assert.Equal(ErrorCodes.Unauthorized, response.Error!.Code);
    }

    [Fact]
    public async Task ReplayedNonceIsRejected()
    {
        Pair();
        var request = _client.Signed("get-logins", new { url = "https://github.com" }, nonce: "aynı");
        Assert.True((await _handler.HandleAsync(request)).Ok);

        var replay = _client.Signed("get-logins", new { url = "https://github.com" }, nonce: "aynı");
        var response = await _handler.HandleAsync(replay);
        Assert.Equal(ErrorCodes.Unauthorized, response.Error!.Code);
        Assert.Contains("tekrar", response.Error.Message);
    }

    [Theory]
    [InlineData(-61)]
    [InlineData(61)]
    public async Task StaleOrFutureTimestampsAreRejected(int offsetSeconds)
    {
        Pair();
        var ts = _time.GetUtcNow().AddSeconds(offsetSeconds).ToUnixTimeSeconds();
        var response = await _handler.HandleAsync(_client.Signed("get-logins", new { url = "https://github.com" }, ts: ts));
        Assert.Equal(ErrorCodes.Unauthorized, response.Error!.Code);
    }

    [Fact]
    public async Task LockedVaultRejectsAndOnlyInteractiveRequestsShowWindow()
    {
        Pair();
        _vault.IsUnlocked = false;

        var passive = await _handler.HandleAsync(_client.Signed("get-logins", new { url = "https://github.com", interactive = false }));
        Assert.Equal(ErrorCodes.Locked, passive.Error!.Code);
        Assert.Equal(0, _ui.UnlockRequests);

        var interactive = await _handler.HandleAsync(_client.Signed("get-logins", new { url = "https://github.com", interactive = true }));
        Assert.Equal(ErrorCodes.Locked, interactive.Error!.Code);
        Assert.Equal(1, _ui.UnlockRequests);
    }

    // ---- kayıtlar ----

    [Fact]
    public async Task GetLoginsReturnsOnlyMatchingEntriesWithoutPasswords()
    {
        Pair();
        var response = await _handler.HandleAsync(_client.Signed("get-logins", new { url = "https://gist.github.com/x" }));

        _client.AssertSignedResponse(response);
        Assert.DoesNotContain("gh-secret", response.Payload);
        var login = Assert.Single(Payload<LoginList>(response).Logins);
        Assert.Equal(_github.Id.ToString("N"), login.Id);
        Assert.Equal("ahmet", login.Username);
        Assert.True(login.HasPassword);
        Assert.True(login.HasTotp);
    }

    [Fact]
    public async Task PhishingDomainGetsNoLogins()
    {
        Pair();
        var response = await _handler.HandleAsync(_client.Signed("get-logins", new { url = "https://github.com.evil.com/login" }));
        Assert.Empty(Payload<LoginList>(response).Logins);
    }

    [Fact]
    public async Task GetCredentialsReturnsPasswordAndNotifies()
    {
        Pair();
        var response = await _handler.HandleAsync(_client.Signed("get-credentials",
            new { url = "https://github.com/login", entryId = _github.Id.ToString("N") }));

        _client.AssertSignedResponse(response);
        var credentials = Payload<Credentials>(response);
        Assert.Equal("ahmet", credentials.Username);
        Assert.Equal("gh-secret", credentials.Password);
        Assert.Contains("GitHub", Assert.Single(_ui.Notifications));
    }

    [Fact]
    public async Task GetCredentialsRefusesEntryThatDoesNotMatchUrl()
    {
        // Ele geçirilmiş/hatalı bir eklenti, başka sitenin kaydını bu sayfaya isteyemez.
        Pair();
        var response = await _handler.HandleAsync(_client.Signed("get-credentials",
            new { url = "https://github.com/login", entryId = _bank.Id.ToString("N") }));

        Assert.Equal(ErrorCodes.NotFound, response.Error!.Code);
        Assert.Empty(_ui.Notifications);
    }

    [Fact]
    public async Task GetTotpReturnsCurrentCode()
    {
        Pair();
        var response = await _handler.HandleAsync(_client.Signed("get-totp",
            new { url = "https://github.com", entryId = _github.Id.ToString("N") }));

        _client.AssertSignedResponse(response);
        var totp = Payload<TotpResult>(response);
        Assert.Matches("^[0-9]{6}$", totp.Code);
        Assert.Equal(30, totp.Period);

        var noTotp = await _handler.HandleAsync(_client.Signed("get-totp",
            new { url = "https://bank.com.tr", entryId = _bank.Id.ToString("N") }));
        Assert.Equal(ErrorCodes.NotFound, noTotp.Error!.Code);
    }

    [Theory]
    [InlineData("get-credentials", """{"url":"https://github.com","entryId":"guid-değil"}""")]
    [InlineData("get-credentials", """{"entryId":"00000000000000000000000000000000"}""")]
    [InlineData("get-logins", """{"url":""}""")]
    [InlineData("bilinmeyen", null)]
    public async Task MalformedSignedRequestsAreBadRequests(string type, string? payload)
    {
        Pair();
        var request = _client.Signed(type, null);
        request.Payload = payload;
        request.Mac = MessageAuth.ComputeRequestMac(_client.Key, request.Type, request.ClientId!, request.Nonce!, request.Ts!.Value, payload);

        var response = await _handler.HandleAsync(request);
        Assert.Equal(ErrorCodes.BadRequest, response.Error!.Code);
    }

    [Fact]
    public async Task ShowAppDoesNotRequireAuthentication()
    {
        var response = await _handler.HandleAsync(new BridgeRequest { Id = "1", Type = "show-app" });
        Assert.True(response.Ok);
        Assert.Equal(1, _ui.ShowAppRequests);
    }
}
