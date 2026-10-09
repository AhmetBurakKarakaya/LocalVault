using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Vault.Desktop.Services;
using Vault.Desktop.ViewModels;

namespace Vault.Desktop.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        // Kayıt sırasında tuşlar (Esc dahil) önce burada yakalanır; kasa ekranının kısayollarına ulaşmaz.
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
    }

    private SettingsViewModel? ViewModel => DataContext as SettingsViewModel;

    private void OnHotkeyBoxClick(object? sender, RoutedEventArgs e)
    {
        ViewModel?.BeginHotkeyRecording();
        HotkeyBox.Focus();
    }

    private void OnHotkeyBoxLostFocus(object? sender, RoutedEventArgs e) => ViewModel?.CancelHotkeyRecording();

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (ViewModel is not { IsRecordingHotkey: true } vm)
            return;
        e.Handled = true;
        if (e.Key == Key.Escape && e.KeyModifiers == KeyModifiers.None)
        {
            vm.CancelHotkeyRecording();
            return;
        }
        vm.RecordHotkeyKey(ToModifiers(e.KeyModifiers), e.Key.ToString());
    }

    private static HotkeyModifiers ToModifiers(KeyModifiers modifiers) =>
        (modifiers.HasFlag(KeyModifiers.Control) ? HotkeyModifiers.Ctrl : 0) |
        (modifiers.HasFlag(KeyModifiers.Alt) ? HotkeyModifiers.Alt : 0) |
        (modifiers.HasFlag(KeyModifiers.Shift) ? HotkeyModifiers.Shift : 0) |
        (modifiers.HasFlag(KeyModifiers.Meta) ? HotkeyModifiers.Win : 0);
}
