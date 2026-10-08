using System.Runtime.Versioning;
using Vault.Desktop.Platform;
using Vault.Desktop.Services;

namespace Vault.Desktop.Tests;

/// <summary>
/// Gerçek Windows panosunu kullanır; bu yüzden yalnızca açıkça istendiğinde çalışır:
/// dotnet test -- --explicit only
/// (Çalıştırmadan önce panodaki içeriği yedekleyin; test panoyu değiştirir.)
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsClipboardTests
{
    private const string ExcludeFormat = "ExcludeClipboardContentFromMonitorProcessing";

    [Fact(Explicit = true)]
    public async Task SensitiveCopyIsExcludedFromHistoryAndClearedAfterDelay()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Yalnızca Windows");
        var service = new ClipboardService(() => null, () => 1);

        await service.CopyAsync("gizli-123", sensitive: true);
        Assert.Equal("gizli-123", WindowsClipboard.GetText());
        Assert.True(WindowsClipboard.HasFormat(ExcludeFormat));
        Assert.True(WindowsClipboard.HasFormat("CanIncludeInClipboardHistory"));

        await Task.Delay(1600, TestContext.Current.CancellationToken);
        Assert.Null(WindowsClipboard.GetText());
    }

    [Fact(Explicit = true)]
    public async Task ClipboardIsNotClearedIfUserCopiedSomethingElse()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Yalnızca Windows");
        var service = new ClipboardService(() => null, () => 1);

        await service.CopyAsync("gizli-456", sensitive: true);
        WindowsClipboard.SetText("kullanıcının kopyaladığı metin", excludeFromHistory: false);

        await Task.Delay(1600, TestContext.Current.CancellationToken);
        Assert.Equal("kullanıcının kopyaladığı metin", WindowsClipboard.GetText());
    }

    [Fact(Explicit = true)]
    public async Task NonSensitiveCopyStaysAndIsNotExcluded()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Yalnızca Windows");
        var service = new ClipboardService(() => null, () => 1);

        await service.CopyAsync("ahmet@example.com", sensitive: false);
        Assert.False(WindowsClipboard.HasFormat(ExcludeFormat));

        await Task.Delay(1600, TestContext.Current.CancellationToken);
        Assert.Equal("ahmet@example.com", WindowsClipboard.GetText());
    }
}
