namespace Vault.Core.Storage;

public static class VaultPaths
{
    public const string PathEnvironmentVariable = "LOCALVAULT_PATH";

    /// <summary>
    /// Kasa ve ayarların klasörü: Windows'ta %APPDATA%\LocalVault, macOS'ta
    /// ~/Library/Application Support/LocalVault, Linux'ta ~/.config/LocalVault.
    /// </summary>
    public static string DataDirectory => OperatingSystem.IsMacOS()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "LocalVault")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LocalVault");

    /// <summary>Varsayılan kasa yolu: LOCALVAULT_PATH ortam değişkeni, yoksa <see cref="DataDirectory"/>/vault.json.</summary>
    public static string DefaultVaultPath
    {
        get
        {
            var fromEnv = Environment.GetEnvironmentVariable(PathEnvironmentVariable);
            if (!string.IsNullOrWhiteSpace(fromEnv))
                return Path.GetFullPath(fromEnv);
            return Path.Combine(DataDirectory, "vault.json");
        }
    }
}
