namespace Vault.Desktop.Services;

/// <summary>
/// Uygulamanın kullanıcı başına tek örnek çalışmasını sağlar. İkinci örnek açılırsa mevcut
/// pencereye "göster" sinyali gönderip kapanır (ör. tepside gizliyken masaüstü kısayoluna tıklanınca).
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private static readonly string BaseName = $"LocalVault.Desktop.{Environment.UserName}";

    private readonly Mutex _mutex;
    private readonly EventWaitHandle? _showSignal;
    private volatile bool _disposed;

    private SingleInstance(Mutex mutex, EventWaitHandle? showSignal)
    {
        _mutex = mutex;
        _showSignal = showSignal;
    }

    public static SingleInstance? TryAcquire()
    {
        var mutex = new Mutex(initiallyOwned: true, BaseName + ".Mutex", out var createdNew);
        if (!createdNew)
        {
            mutex.Dispose();
            return null;
        }

        EventWaitHandle? signal = null;
        if (OperatingSystem.IsWindows())
            signal = new EventWaitHandle(false, EventResetMode.AutoReset, BaseName + ".Show");
        return new SingleInstance(mutex, signal);
    }

    /// <summary>Çalışan örneğe pencereyi göstermesi için sinyal gönderir.</summary>
    public static void SignalExistingInstance()
    {
        if (OperatingSystem.IsWindows() && EventWaitHandle.TryOpenExisting(BaseName + ".Show", out var handle))
        {
            using (handle)
                handle.Set();
        }
    }

    public void ListenForShowRequests(Action onShow)
    {
        if (_showSignal is null)
            return;

        var thread = new Thread(() =>
        {
            while (!_disposed)
            {
                _showSignal.WaitOne();
                if (!_disposed)
                    onShow();
            }
        })
        {
            IsBackground = true,
            Name = "LocalVault.SingleInstance",
        };
        thread.Start();
    }

    public void Dispose()
    {
        _disposed = true;
        _showSignal?.Set();
        _showSignal?.Dispose();
        _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
