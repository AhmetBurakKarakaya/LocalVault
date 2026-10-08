using Avalonia.Input.Platform;
using Vault.Desktop.Platform;

namespace Vault.Desktop.Services;

public interface IClipboardService
{
    /// <summary>
    /// Metni panoya kopyalar. <paramref name="sensitive"/> ise Windows pano geçmişine/bulut eşitlemesine
    /// girmez ve ayarlanan süre sonunda (pano hâlâ bu değeri tutuyorsa) temizlenir.
    /// </summary>
    Task CopyAsync(string text, bool sensitive = true);

    /// <summary>Panoda hâlâ bizim kopyaladığımız hassas değer varsa siler (kilitlemede/çıkışta).</summary>
    Task ClearIfOwnedAsync();
}

public sealed class ClipboardService(Func<IClipboard?> avaloniaClipboard, Func<int> clearAfterSeconds) : IClipboardService
{
    private string? _sensitiveValue;
    private CancellationTokenSource? _clearCts;

    public async Task CopyAsync(string text, bool sensitive = true)
    {
        await SetTextAsync(text, sensitive);

        _clearCts?.Cancel();
        _clearCts = null;
        _sensitiveValue = sensitive ? text : null;

        var seconds = clearAfterSeconds();
        if (sensitive && seconds > 0)
        {
            var cts = _clearCts = new CancellationTokenSource();
            _ = ClearLaterAsync(TimeSpan.FromSeconds(seconds), cts.Token);
        }
    }

    public async Task ClearIfOwnedAsync()
    {
        var value = _sensitiveValue;
        _sensitiveValue = null;
        if (value is null)
            return;

        try
        {
            // Kullanıcı bu arada başka bir şey kopyaladıysa ona dokunma.
            if (await GetTextAsync() == value)
                await ClearAsync();
        }
        catch (IOException)
        {
            // Pano başka bir uygulama tarafından kilitli; bir sonraki denemede temizlenir.
        }
    }

    private async Task ClearLaterAsync(TimeSpan delay, CancellationToken token)
    {
        try
        {
            await Task.Delay(delay, token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        await ClearIfOwnedAsync();
    }

    private async Task SetTextAsync(string text, bool sensitive)
    {
        if (OperatingSystem.IsWindows())
        {
            WindowsClipboard.SetText(text, excludeFromHistory: sensitive);
            return;
        }
        if (avaloniaClipboard() is { } clipboard)
            await clipboard.SetTextAsync(text);
    }

    private async Task<string?> GetTextAsync()
    {
        if (OperatingSystem.IsWindows())
            return WindowsClipboard.GetText();
        return avaloniaClipboard() is { } clipboard ? await clipboard.TryGetTextAsync() : null;
    }

    private async Task ClearAsync()
    {
        if (OperatingSystem.IsWindows())
        {
            WindowsClipboard.Clear();
            return;
        }
        if (avaloniaClipboard() is { } clipboard)
            await clipboard.ClearAsync();
    }
}
