using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Vault.Desktop.ViewModels;
using Vault.Desktop.Views;

namespace Vault.Desktop.Tests;

/// <summary>
/// Kasa ekranının kısayolları gerçek tuş basımlarıyla. Avalonia 12'de KeyBinding'ler odaktaki metin kutusundan
/// önce çalıştığı için arama kutusunda Delete kaydı silmeye, Ctrl+C parolayı kopyalamaya gidiyordu.
/// </summary>
public sealed class KeyboardShortcutTests : IDisposable
{
    private readonly TestEnvironment _env = new();
    private Window? _window;

    public void Dispose()
    {
        _window?.Close();
        _env.Dispose();
    }

    private async Task<VaultViewModel> OpenAsync()
    {
        _env.SeedVault(TestEnvironment.SampleEntries());
        var main = new MainWindowViewModel(_env.Services);
        _window = new MainWindow { DataContext = main, Width = 1120, Height = 720 };
        _window.Show();
        var unlock = Assert.IsType<UnlockViewModel>(main.CurrentPage);
        unlock.Password = TestEnvironment.MasterPassword;
        await unlock.UnlockCommand.ExecuteAsync(null);
        var vault = Assert.IsType<VaultViewModel>(main.CurrentPage);
        vault.SelectedItem = vault.Items.First(i => i.Title == "GitHub");
        Dispatcher.UIThread.RunJobs();
        return vault;
    }

    private T Find<T>(string name) where T : Control =>
        _window!.GetVisualDescendants().OfType<T>().Single(c => c.Name == name);

    private void Press(Key key, RawInputModifiers modifiers = RawInputModifiers.None, PhysicalKey physical = PhysicalKey.None, string? symbol = null)
    {
        _window!.KeyPress(key, modifiers, physical, symbol);
        _window!.KeyRelease(key, modifiers, physical, symbol);
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>Kısayolların ana değiştiricisi: macOS'ta Cmd, diğerlerinde Ctrl.</summary>
    private static RawInputModifiers Primary => OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;

    private bool PasswordCopied => _env.Clipboard.Copies.Any(c => c.Sensitive);

    [AvaloniaFact]
    public async Task DeleteInSearchBoxEditsTextInsteadOfDeletingEntry()
    {
        var vault = await OpenAsync();
        var search = Find<TextBox>("SearchBox");
        search.Focus();
        search.Text = "xgit";
        search.CaretIndex = 0;

        Press(Key.Delete, physical: PhysicalKey.Delete);

        Assert.Equal("git", search.Text);
        Assert.Null(vault.Overlay);
    }

    [AvaloniaFact]
    public async Task CtrlCInSearchBoxDoesNotCopyPassword()
    {
        var vault = await OpenAsync();
        var search = Find<TextBox>("SearchBox");
        search.Focus();
        search.Text = "git";
        search.SelectAll();

        Press(Key.C, Primary, PhysicalKey.C, "c");

        Assert.False(PasswordCopied);
        Assert.Null(vault.StatusMessage);
    }

    [AvaloniaFact]
    public async Task ShortcutsStillWorkOutsideTextBoxes()
    {
        var vault = await OpenAsync();
        var list = _window!.GetVisualDescendants().OfType<ListBox>().Single(l => l.Classes.Contains("entries"));
        Assert.True(list.ContainerFromIndex(list.SelectedIndex)!.Focus());

        Press(Key.C, Primary, PhysicalKey.C, "c");
        Assert.True(PasswordCopied);

        Press(Key.Delete, physical: PhysicalKey.Delete);
        Assert.IsType<ConfirmViewModel>(vault.Overlay);

        Press(Key.Escape, physical: PhysicalKey.Escape);
        Assert.Null(vault.Overlay);

        Press(Key.L, Primary, PhysicalKey.L, "l");
        Assert.False(_env.Services.Vault.IsUnlocked);
    }

    [AvaloniaFact]
    public async Task CtrlSSavesEditorFromTextBox()
    {
        var vault = await OpenAsync();
        vault.EditEntryCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        vault.Editor!.Title = "GitHub (iş)";
        Find<TextBox>("TitleBox").Focus();

        Press(Key.S, Primary, PhysicalKey.S, "s");

        Assert.Null(vault.Editor);
        Assert.Contains(vault.Items, i => i.Title == "GitHub (iş)");
    }
}
