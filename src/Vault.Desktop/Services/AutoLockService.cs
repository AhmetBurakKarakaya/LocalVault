using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia.Threading;
using Microsoft.Win32;

namespace Vault.Desktop.Services;

/// <summary>Kullanıcının bilgisayarda ne kadar süredir hareketsiz olduğunu bildirir.</summary>
public interface IIdleTimeSource
{
    TimeSpan GetIdleTime();
    /// <summary>Uygulama içi etkinlik (Windows dışı platformlarda boşta süre buna göre hesaplanır).</summary>
    void ReportActivity();
}

public sealed partial class IdleTimeSource : IIdleTimeSource
{
    private long _lastActivity = Environment.TickCount64;

    public void ReportActivity() => Interlocked.Exchange(ref _lastActivity, Environment.TickCount64);

    public TimeSpan GetIdleTime()
    {
        // Windows'ta sistem genelindeki son girdi kullanılır: kullanıcı başka bir uygulamada
        // çalışıyorsa (ör. tarayıcıda otomatik doldurma) kasa kilitlenmez; masadan kalkınca kilitlenir.
        if (OperatingSystem.IsWindows() && TryGetSystemIdle(out var idle))
            return idle;
        return TimeSpan.FromMilliseconds(Environment.TickCount64 - Interlocked.Read(ref _lastActivity));
    }

    [SupportedOSPlatform("windows")]
    private static bool TryGetSystemIdle(out TimeSpan idle)
    {
        var info = new LastInputInfo { cbSize = (uint)Marshal.SizeOf<LastInputInfo>() };
        if (!GetLastInputInfo(ref info))
        {
            idle = default;
            return false;
        }
        idle = TimeSpan.FromMilliseconds(unchecked((uint)Environment.TickCount - info.dwTime));
        return true;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        public uint cbSize;
        public uint dwTime;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetLastInputInfo(ref LastInputInfo plii);
}

/// <summary>Boşta kalma, Windows oturum kilidi ve uyku durumunda kasayı kilitler.</summary>
public sealed class AutoLockService : IDisposable
{
    private readonly VaultService _vault;
    private readonly Func<AppSettings> _settings;
    private readonly IIdleTimeSource _idle;
    private readonly DispatcherTimer _timer;

    public AutoLockService(VaultService vault, Func<AppSettings> settings, IIdleTimeSource idle)
    {
        _vault = vault;
        _settings = settings;
        _idle = idle;
        _timer = new DispatcherTimer(TimeSpan.FromSeconds(5), DispatcherPriority.Background, (_, _) => CheckIdle());
        _timer.Start();

        if (OperatingSystem.IsWindows())
        {
            // SystemEvents kendi gizli penceresini oluşturur; arka plan iş parçacığından abone olunca
            // olayları dinleyen ayrı bir iş parçacığı açar ve UI döngüsüne bağımlı kalmaz.
            Task.Run(SubscribeSystemEvents);
        }
    }

    [SupportedOSPlatform("windows")]
    private void SubscribeSystemEvents()
    {
        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    private void CheckIdle()
    {
        var minutes = _settings().AutoLockMinutes;
        if (_vault.IsUnlocked && minutes > 0 && _idle.GetIdleTime() >= TimeSpan.FromMinutes(minutes))
            _vault.Lock();
    }

    [SupportedOSPlatform("windows")]
    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (!_settings().LockOnSessionLock)
            return;
        if (e.Reason is SessionSwitchReason.SessionLock or SessionSwitchReason.ConsoleDisconnect
            or SessionSwitchReason.RemoteDisconnect or SessionSwitchReason.SessionLogoff)
            Dispatcher.UIThread.Post(_vault.Lock);
    }

    [SupportedOSPlatform("windows")]
    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Suspend)
            Dispatcher.UIThread.Post(_vault.Lock);
    }

    public void Dispose()
    {
        _timer.Stop();
        if (OperatingSystem.IsWindows())
        {
            SystemEvents.SessionSwitch -= OnSessionSwitch;
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        }
    }
}
