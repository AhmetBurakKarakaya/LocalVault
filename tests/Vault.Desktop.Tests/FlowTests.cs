using Avalonia.Headless.XUnit;
using Vault.Core.Models;
using Vault.Core.Storage;
using Vault.Desktop.ViewModels;

namespace Vault.Desktop.Tests;

public sealed class FlowTests : IDisposable
{
    private readonly TestEnvironment _env = new();

    public void Dispose() => _env.Dispose();

    private MainWindowViewModel NewMain() => new(_env.Services);

    private async Task<VaultViewModel> UnlockAsync(MainWindowViewModel main)
    {
        var unlock = Assert.IsType<UnlockViewModel>(main.CurrentPage);
        unlock.Password = TestEnvironment.MasterPassword;
        await unlock.UnlockCommand.ExecuteAsync(null);
        return Assert.IsType<VaultViewModel>(main.CurrentPage);
    }

    [AvaloniaFact]
    public async Task CreatingVaultValidatesAndOpensEmptyVault()
    {
        var main = NewMain();
        var create = Assert.IsType<CreateVaultViewModel>(main.CurrentPage);
        Assert.False(create.CanGoBack);

        create.Password = "kısa";
        create.ConfirmPassword = "kısa";
        await create.CreateCommand.ExecuteAsync(null);
        Assert.Contains("en az 8", create.ErrorMessage);

        create.Password = "Uzun-Bir-Parola-1";
        create.ConfirmPassword = "Farklı-Parola-1";
        await create.CreateCommand.ExecuteAsync(null);
        Assert.Equal("Parolalar eşleşmiyor.", create.ErrorMessage);

        create.ConfirmPassword = "Uzun-Bir-Parola-1";
        await create.CreateCommand.ExecuteAsync(null);

        var vault = Assert.IsType<VaultViewModel>(main.CurrentPage);
        Assert.True(vault.IsEmpty);
        Assert.True(File.Exists(_env.VaultPath));
        Assert.Equal("", create.Password);   // parola bellekte tutulmuyor
    }

    [AvaloniaFact]
    public async Task AddingEntryFromOtpAuthUriFillsFieldsAndPersistsEncrypted()
    {
        _env.SeedVault();
        var vault = await UnlockAsync(NewMain());

        vault.NewEntryCommand.Execute(null);
        var editor = Assert.IsType<EntryEditorViewModel>(vault.Editor);
        Assert.True(editor.IsNew);
        Assert.False(vault.NewEntryCommand.CanExecute(null));   // düzenlerken yeni kayıt açılamaz

        editor.TotpSecret = "otpauth://totp/GitLab:ahmet@example.com?secret=JBSWY3DPEHPK3PXP&issuer=GitLab&digits=8";
        Assert.Equal("GitLab", editor.Title);
        Assert.Equal("ahmet@example.com", editor.Username);
        Assert.Equal("JBSWY3DPEHPK3PXP", editor.TotpSecret);
        Assert.Equal(8, editor.TotpDigits);
        Assert.Null(editor.TotpError);
        Assert.NotEmpty(editor.TotpPreview);

        editor.Password = "Çok-Gizli-Parola!";
        editor.UrlsText = "https://gitlab.com\n\n  https://about.gitlab.com  ";
        editor.TagsText = "iş, geliştirme, iş";
        editor.SaveCommand.Execute(null);

        Assert.Null(vault.Editor);
        var item = Assert.Single(vault.Items);
        Assert.Same(item, vault.SelectedItem);
        Assert.NotNull(vault.Detail);
        Assert.Equal(9, vault.Detail!.TotpCode.Length);   // "1234 5678"
        Assert.Contains("eklendi", vault.StatusMessage);

        Assert.DoesNotContain("Çok-Gizli-Parola", File.ReadAllText(_env.VaultPath));
        using var session = VaultSession.Open(_env.VaultPath, TestEnvironment.MasterPassword);
        var saved = Assert.Single(session.Data.Entries);
        Assert.Equal(["https://gitlab.com", "https://about.gitlab.com"], saved.Urls);
        Assert.Equal(["iş", "geliştirme"], saved.Tags);
        Assert.Equal(8, saved.Totp!.Digits);
    }

    [AvaloniaFact]
    public async Task EditorValidatesInputAndCancelDiscards()
    {
        _env.SeedVault();
        var vault = await UnlockAsync(NewMain());
        vault.NewEntryCommand.Execute(null);
        var editor = vault.Editor!;

        editor.SaveCommand.Execute(null);
        Assert.Equal("Başlık boş olamaz.", editor.ErrorMessage);

        editor.Title = "Test";
        editor.UrlsText = "ftp://example.com";
        editor.SaveCommand.Execute(null);
        Assert.Contains("Geçersiz web adresi", editor.ErrorMessage);

        editor.UrlsText = "example.com";
        editor.TotpSecret = "geçersiz!";
        Assert.NotNull(editor.TotpError);
        editor.SaveCommand.Execute(null);
        Assert.Contains("İki adımlı doğrulama", editor.ErrorMessage);

        editor.CancelCommand.Execute(null);
        Assert.Null(vault.Editor);
        Assert.True(vault.IsEmpty);
    }

    [AvaloniaFact]
    public async Task EditingExistingEntryUpdatesIt()
    {
        _env.SeedVault(TestEnvironment.SampleEntries());
        var vault = await UnlockAsync(NewMain());
        vault.SelectedItem = vault.Items.First(i => i.Title == "GitHub");

        vault.EditEntryCommand.Execute(null);
        var editor = vault.Editor!;
        Assert.False(editor.IsNew);
        Assert.Equal("ahmet@example.com", editor.Username);
        Assert.Equal("JBSWY3DPEHPK3PXP", editor.TotpSecret);

        editor.Title = "GitHub (iş)";
        editor.ClearTotpCommand.Execute(null);
        editor.SaveCommand.Execute(null);

        Assert.Equal("GitHub (iş)", vault.SelectedItem!.Title);
        Assert.False(vault.Detail!.HasTotp);
        Assert.Equal(6, vault.Items.Count);
    }

    [AvaloniaFact]
    public async Task CopyCommandsMarkSecretsAsSensitive()
    {
        _env.SeedVault(TestEnvironment.SampleEntries());
        var vault = await UnlockAsync(NewMain());
        vault.SelectedItem = vault.Items.First(i => i.Title == "GitHub");

        await vault.CopyUsernameCommand.ExecuteAsync(null);
        await vault.CopyPasswordCommand.ExecuteAsync(null);
        await vault.CopyTotpCommand.ExecuteAsync(null);

        Assert.Equal(("ahmet@example.com", false), _env.Clipboard.Copies[0]);
        Assert.Equal(("Gh!7xQ2#pLm9vR4t", true), _env.Clipboard.Copies[1]);
        Assert.Matches("^[0-9]{6}$", _env.Clipboard.Copies[2].Text);
        Assert.True(_env.Clipboard.Copies[2].Sensitive);
        Assert.Contains("20 sn sonra", vault.StatusMessage);

        await vault.Detail!.UrlItems[0].OpenCommand.ExecuteAsync(null);
        Assert.Equal(["https://github.com"], _env.Platform.OpenedUrls);
    }

    [AvaloniaFact]
    public async Task LockClearsClipboardAndUnlockRejectsWrongPassword()
    {
        _env.SeedVault(TestEnvironment.SampleEntries());
        var main = NewMain();
        var vault = await UnlockAsync(main);

        await vault.LockCommand.ExecuteAsync(null);
        Assert.False(_env.Services.Vault.IsUnlocked);
        Assert.Equal(1, _env.Clipboard.ClearCalls);
        var unlock = Assert.IsType<UnlockViewModel>(main.CurrentPage);

        unlock.Password = "yanlış-parola";
        await unlock.UnlockCommand.ExecuteAsync(null);
        Assert.Equal("Ana parola yanlış.", unlock.ErrorMessage);
        Assert.Equal("", unlock.Password);
        Assert.IsType<UnlockViewModel>(main.CurrentPage);

        Assert.False(unlock.UnlockCommand.CanExecute(null));   // boş parolayla denenemez
        await UnlockAsync(main);
    }

    [AvaloniaFact]
    public async Task SearchFiltersByTitleUsernameUrlAndTag()
    {
        _env.SeedVault(TestEnvironment.SampleEntries());
        var vault = await UnlockAsync(NewMain());
        Assert.Equal("6 kayıt", vault.CountText);

        vault.SearchText = "bank";
        Assert.Equal(["Şirket Bankası"], vault.Items.Select(i => i.Title));
        Assert.Equal("1 / 6 kayıt", vault.CountText);

        vault.SearchText = "iş";
        Assert.Equal(["AWS Konsol", "GitHub"], vault.Items.Select(i => i.Title));

        vault.SearchText = "yok-böyle-bir-şey";
        Assert.True(vault.HasNoResults);

        vault.EscapeCommand.Execute(null);
        Assert.Equal(6, vault.Items.Count);
    }

    [AvaloniaFact]
    public async Task DeleteRequiresConfirmation()
    {
        _env.SeedVault(TestEnvironment.SampleEntries());
        var vault = await UnlockAsync(NewMain());
        vault.SelectedItem = vault.Items.First(i => i.Title == "Netflix");

        vault.DeleteEntryCommand.Execute(null);
        var confirm = Assert.IsType<ConfirmViewModel>(vault.Overlay);
        Assert.True(confirm.IsDestructive);

        confirm.CancelCommand.Execute(null);
        Assert.Null(vault.Overlay);
        Assert.Equal(6, vault.Items.Count);

        vault.DeleteEntryCommand.Execute(null);
        ((ConfirmViewModel)vault.Overlay!).ConfirmCommand.Execute(null);
        Assert.Equal(5, vault.Items.Count);
        Assert.Null(vault.SelectedItem);

        using var session = VaultSession.Open(_env.VaultPath, TestEnvironment.MasterPassword);
        Assert.DoesNotContain(session.Data.Entries, e => e.Title == "Netflix");
    }

    [AvaloniaFact]
    public async Task ExternalChangeOffersReloadAndEditCanBeSavedAfterwards()
    {
        _env.SeedVault(TestEnvironment.SampleEntries());
        var vault = await UnlockAsync(NewMain());

        // Başka bir uygulama (ör. CLI) kasaya kayıt ekliyor.
        using (var other = VaultSession.Open(_env.VaultPath, TestEnvironment.MasterPassword))
        {
            other.Data.Entries.Add(new VaultEntry { Title = "CLI'dan eklendi" });
            other.Save();
        }

        vault.NewEntryCommand.Execute(null);
        vault.Editor!.Title = "Uygulamadan eklendi";
        vault.Editor.SaveCommand.Execute(null);

        var prompt = Assert.IsType<ConfirmViewModel>(vault.Overlay);
        Assert.NotNull(vault.Editor);   // form kaybolmadı
        prompt.ConfirmCommand.Execute(null);
        Assert.Contains(vault.Items, i => i.Title == "CLI'dan eklendi");

        vault.Editor!.SaveCommand.Execute(null);
        Assert.Null(vault.Editor);

        using var session = VaultSession.Open(_env.VaultPath, TestEnvironment.MasterPassword);
        Assert.Contains(session.Data.Entries, e => e.Title == "CLI'dan eklendi");
        Assert.Contains(session.Data.Entries, e => e.Title == "Uygulamadan eklendi");
    }

    [AvaloniaFact]
    public async Task ChangeMasterPasswordVerifiesCurrentPassword()
    {
        _env.SeedVault(TestEnvironment.SampleEntries());
        var vault = await UnlockAsync(NewMain());

        vault.ChangeMasterPasswordCommand.Execute(null);
        var dialog = Assert.IsType<ChangeMasterPasswordViewModel>(vault.Overlay);
        dialog.CurrentPassword = "yanlış";
        dialog.NewPassword = dialog.ConfirmPassword = "Yeni-Ana-Parola-2";
        await dialog.ChangeCommand.ExecuteAsync(null);
        Assert.Equal("Mevcut ana parola yanlış.", dialog.ErrorMessage);

        dialog.CurrentPassword = TestEnvironment.MasterPassword;
        await dialog.ChangeCommand.ExecuteAsync(null);
        Assert.Null(vault.Overlay);

        using var session = VaultSession.Open(_env.VaultPath, "Yeni-Ana-Parola-2");
        Assert.Equal(6, session.Data.Entries.Count);
    }

    [AvaloniaFact]
    public async Task GeneratorFillsEditorPassword()
    {
        _env.SeedVault();
        var vault = await UnlockAsync(NewMain());
        vault.NewEntryCommand.Execute(null);
        vault.Editor!.GeneratePasswordCommand.Execute(null);

        var generator = Assert.IsType<PasswordGeneratorViewModel>(vault.Overlay);
        Assert.True(generator.CanUse);
        generator.Length = 32;
        generator.Symbols = false;
        Assert.Equal(32, generator.Generated.Length);
        Assert.All(generator.Generated, c => Assert.True(char.IsAsciiLetterOrDigit(c)));

        generator.UseCommand.Execute(null);
        Assert.Null(vault.Overlay);
        Assert.Equal(generator.Generated, vault.Editor.Password);
        Assert.Equal(5, vault.Editor.StrengthValue);
    }

    [AvaloniaFact]
    public async Task SettingsArePersisted()
    {
        _env.SeedVault();
        var vault = await UnlockAsync(NewMain());
        vault.OpenSettingsCommand.Execute(null);

        var settings = Assert.IsType<SettingsViewModel>(vault.Overlay);
        settings.AutoLock = SettingsViewModel.AutoLockOptions.First(o => o.Value == 15);
        settings.ClipboardClear = SettingsViewModel.ClipboardOptions.First(o => o.Value == 30);
        settings.LockOnMinimize = true;
        settings.SaveCommand.Execute(null);

        var reloaded = Services.SettingsStore.Load(_env.Services.SettingsStore.FilePath).Current;
        Assert.Equal(15, reloaded.AutoLockMinutes);
        Assert.Equal(30, reloaded.ClipboardClearSeconds);
        Assert.True(reloaded.LockOnMinimize);
        Assert.Equal(_env.VaultPath, reloaded.VaultPath);
    }
}
