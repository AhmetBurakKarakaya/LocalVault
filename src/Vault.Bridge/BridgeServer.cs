using System.IO.Pipes;

namespace Vault.Bridge;

/// <summary>
/// Masaüstü uygulamasındaki named pipe sunucusu. Her native host örneği (tarayıcı başına bir tane)
/// ayrı bir bağlantı açar. Bir bağlantı üzerindeki istekler eşzamanlı işlenir; yanıtlar istek
/// kimliğiyle eşleştirildiği için sıraları önemli değildir (ör. onay bekleyen bir eşleştirme
/// isteği, arada gelen ping'i bekletmez).
/// </summary>
public sealed class BridgeServer : IAsyncDisposable
{
    private const int MaxConnections = 16;

    private readonly string _pipeName;
    private readonly Func<BridgeRequest, CancellationToken, Task<BridgeResponse>> _handler;
    private readonly CancellationTokenSource _cts = new();
    private Task? _acceptLoop;

    public BridgeServer(string pipeName, Func<BridgeRequest, CancellationToken, Task<BridgeResponse>> handler)
    {
        _pipeName = pipeName;
        _handler = handler;
    }

    public event Action<Exception>? Error;

    public void Start() => _acceptLoop ??= Task.Run(() => AcceptLoopAsync(_cts.Token));

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                // CurrentUserOnly: yalnızca aynı Windows kullanıcısının işlemleri bağlanabilir.
                pipe = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, MaxConnections,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(ct);
                var connection = pipe;
                pipe = null;
                _ = Task.Run(() => HandleConnectionAsync(connection, ct), ct);
            }
            catch (OperationCanceledException)
            {
                pipe?.Dispose();
                break;
            }
            catch (IOException ex)
            {
                // Ör. aynı adla başka bir sunucu çalışıyor; kısa bekleyip yeniden dene.
                pipe?.Dispose();
                Error?.Invoke(ex);
                await Task.Delay(1000, ct).ContinueWith(_ => { }, TaskScheduler.Default);
            }
        }
    }

    private async Task HandleConnectionAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        var writeLock = new SemaphoreSlim(1, 1);
        var pending = new List<Task>();
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var message = await Framing.ReadMessageAsync(pipe, BridgeProtocol.MaxMessageBytes, ct);
                if (message is null)
                    break;

                pending.RemoveAll(t => t.IsCompleted);
                pending.Add(ProcessAsync(message, pipe, writeLock, ct));
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or OperationCanceledException)
        {
            // Bağlantı koptu veya bozuk veri: bağlantıyı kapat, sunucu çalışmaya devam eder.
        }
        finally
        {
            try
            {
                await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch
            {
                // Bekleyen istekler yarıda kalabilir; bağlantı zaten kapanıyor.
            }
            await pipe.DisposeAsync();
        }
    }

    private async Task ProcessAsync(byte[] message, Stream pipe, SemaphoreSlim writeLock, CancellationToken ct)
    {
        BridgeResponse response;
        var request = BridgeProtocol.TryParseRequest(message);
        if (request is null)
        {
            response = BridgeResponse.Failure("", ErrorCodes.BadRequest, "Geçersiz JSON.");
        }
        else
        {
            try
            {
                response = await _handler(request, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                Error?.Invoke(ex);
                response = BridgeResponse.Failure(request.Id, ErrorCodes.Internal, "Uygulamada beklenmeyen bir hata oluştu.");
            }
        }

        await writeLock.WaitAsync(ct);
        try
        {
            await Framing.WriteMessageAsync(pipe, BridgeProtocol.Serialize(response), ct);
        }
        catch (IOException)
        {
            // İstemci yanıtı beklemeden ayrıldı.
        }
        finally
        {
            writeLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        if (_acceptLoop is not null)
        {
            try
            {
                await _acceptLoop;
            }
            catch (OperationCanceledException)
            {
            }
        }
        _cts.Dispose();
    }
}
