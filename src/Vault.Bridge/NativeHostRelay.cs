using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;

namespace Vault.Bridge;

/// <summary>
/// Native host'un çekirdeği: tarayıcıdan (stdin) gelen mesajları masaüstü uygulamasının pipe'ına,
/// pipe'tan gelen yanıtları tarayıcıya (stdout) aktarır. İçeriği yorumlamaz; yalnızca uygulama
/// çalışmıyorsa veya bağlantı koparsa yanıtı kendisi üretir.
/// </summary>
public sealed class NativeHostRelay(Stream browserIn, Stream browserOut, string pipeName, string? desktopAppPath)
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromMilliseconds(500);

    private readonly SemaphoreSlim _outLock = new(1, 1);
    private readonly SemaphoreSlim _pipeLock = new(1, 1);
    private readonly ConcurrentDictionary<string, byte> _pending = new();
    private NamedPipeClientStream? _pipe;

    /// <summary>Tarayıcı stdin'i kapatana (eklenti bağlantıyı kapatana) kadar çalışır.</summary>
    public async Task RunAsync(CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested)
        {
            byte[]? message;
            try
            {
                message = await Framing.ReadMessageAsync(browserIn, BridgeProtocol.MaxMessageBytes, ct);
            }
            catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException)
            {
                break;   // Tarayıcıdan bozuk çerçeve: native messaging sözleşmesi bozuldu, çık.
            }
            if (message is null)
                break;

            var request = BridgeProtocol.TryParseRequest(message);
            if (request is null)
            {
                await ReplyAsync(BridgeResponse.Failure("", ErrorCodes.BadRequest, "Geçersiz JSON."), ct);
                continue;
            }

            if (request.Type == RequestTypes.LaunchApp)
            {
                await ReplyAsync(LaunchDesktopApp(request.Id), ct);
                continue;
            }

            await ForwardAsync(request.Id, message, ct);
        }

        await DisconnectAsync();
    }

    private async Task ForwardAsync(string id, byte[] message, CancellationToken ct)
    {
        var pipe = await EnsureConnectedAsync(ct);
        if (pipe is null)
        {
            await ReplyAsync(BridgeResponse.Failure(id, ErrorCodes.AppNotRunning, "LocalVault uygulaması çalışmıyor."), ct);
            return;
        }

        _pending[id] = 0;
        try
        {
            await _pipeLock.WaitAsync(ct);
            try
            {
                await Framing.WriteMessageAsync(pipe, message, ct);
            }
            finally
            {
                _pipeLock.Release();
            }
        }
        catch (IOException)
        {
            await DisconnectAsync();
            if (_pending.TryRemove(id, out _))
                await ReplyAsync(BridgeResponse.Failure(id, ErrorCodes.AppNotRunning, "LocalVault uygulamasıyla bağlantı koptu."), ct);
        }
    }

    private async Task<NamedPipeClientStream?> EnsureConnectedAsync(CancellationToken ct)
    {
        if (_pipe is { IsConnected: true })
            return _pipe;

        await DisconnectAsync();
        var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(ConnectTimeout);
            await pipe.ConnectAsync(timeout.Token);
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException or IOException or UnauthorizedAccessException)
        {
            await pipe.DisposeAsync();
            return null;
        }

        _pipe = pipe;
        _ = Task.Run(() => PumpResponsesAsync(pipe, ct), ct);
        return pipe;
    }

    /// <summary>Pipe'tan gelen yanıtları tarayıcıya aktarır; bağlantı koparsa bekleyen isteklere hata döner.</summary>
    private async Task PumpResponsesAsync(NamedPipeClientStream pipe, CancellationToken ct)
    {
        try
        {
            while (true)
            {
                var message = await Framing.ReadMessageAsync(pipe, BridgeProtocol.MaxMessageBytes, ct);
                if (message is null)
                    break;

                if (BridgeProtocol.TryParseResponse(message) is { } response)
                    _pending.TryRemove(response.Id, out _);
                await WriteToBrowserAsync(message, ct);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or OperationCanceledException or ObjectDisposedException)
        {
        }

        foreach (var id in _pending.Keys.ToList())
        {
            if (_pending.TryRemove(id, out _))
                await ReplyAsync(BridgeResponse.Failure(id, ErrorCodes.AppNotRunning, "LocalVault uygulamasıyla bağlantı koptu."), ct);
        }
    }

    private BridgeResponse LaunchDesktopApp(string id)
    {
        if (desktopAppPath is null || !File.Exists(desktopAppPath))
            return BridgeResponse.Failure(id, ErrorCodes.NotFound, "LocalVault uygulaması bulunamadı.");
        try
        {
            Process.Start(new ProcessStartInfo(desktopAppPath) { UseShellExecute = true })?.Dispose();
            return new BridgeResponse { Id = id, Ok = true };
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return BridgeResponse.Failure(id, ErrorCodes.Internal, $"Uygulama başlatılamadı: {ex.Message}");
        }
    }

    private Task ReplyAsync(BridgeResponse response, CancellationToken ct) =>
        WriteToBrowserAsync(BridgeProtocol.Serialize(response), ct);

    private async Task WriteToBrowserAsync(byte[] message, CancellationToken ct)
    {
        await _outLock.WaitAsync(ct);
        try
        {
            await Framing.WriteMessageAsync(browserOut, message, ct);
        }
        finally
        {
            _outLock.Release();
        }
    }

    private async Task DisconnectAsync()
    {
        var pipe = Interlocked.Exchange(ref _pipe, null);
        if (pipe is not null)
            await pipe.DisposeAsync();
    }
}
