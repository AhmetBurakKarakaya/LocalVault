using CommunityToolkit.Mvvm.ComponentModel;
using Vault.Core.Models;

namespace Vault.Desktop.ViewModels;

/// <summary>Sol gezinme bölmesindeki bir öğe: sabit kategori veya etiket.</summary>
public sealed partial class NavItemViewModel(string key, string label, string iconKey, Func<VaultEntry, bool> filter) : ViewModelBase
{
    public string Key { get; } = key;
    public string Label { get; } = label;
    /// <summary>App.axaml'daki ikon kaynağının adı.</summary>
    public string IconKey { get; } = iconKey;
    public Func<VaultEntry, bool> Filter { get; } = filter;

    [ObservableProperty] public partial int Count { get; set; }

    public static NavItemViewModel ForTag(string tag) =>
        new($"tag:{tag}", tag, "IconTag", e => e.Tags.Contains(tag, StringComparer.CurrentCultureIgnoreCase));
}
