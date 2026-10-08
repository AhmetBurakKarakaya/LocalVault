namespace Vault.Core.Storage;

internal static class AtomicFile
{
    /// <summary>
    /// Önce geçici dosyaya yazıp diske boşaltır, sonra hedefle yer değiştirir. Böylece yazma
    /// sırasında elektrik kesilse bile ya eski ya yeni dosya sağlam kalır.
    /// </summary>
    public static void Write(string path, ReadOnlySpan<byte> bytes)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);

        var tempPath = path + ".tmp";
        using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }

        if (File.Exists(path))
            File.Replace(tempPath, path, path + ".bak", ignoreMetadataErrors: true);
        else
            File.Move(tempPath, path);
    }
}
