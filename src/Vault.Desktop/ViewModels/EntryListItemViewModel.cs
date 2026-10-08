using Vault.Core.Models;

namespace Vault.Desktop.ViewModels;

public sealed class EntryListItemViewModel(VaultEntry entry) : ViewModelBase
{
    public VaultEntry Entry { get; } = entry;
    public string Title => Entry.Title;
    public string Subtitle => !string.IsNullOrEmpty(Entry.Username) ? Entry.Username : Entry.Urls.FirstOrDefault() ?? "";
    public bool HasTotp => Entry.Totp is not null;
    public string Initial => Display.Initial(Entry.Title);
    public int ColorIndex => Display.ColorIndex(Entry.Title);
}
