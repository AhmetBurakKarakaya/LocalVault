namespace Vault.Core;

public class VaultException(string message, Exception? inner = null) : Exception(message, inner);

/// <summary>Ana parola yanlış ya da dosya içeriği değiştirilmiş/bozulmuş.</summary>
public sealed class InvalidMasterPasswordException()
    : VaultException("Ana parola yanlış veya kasa dosyası bozulmuş.");

/// <summary>Dosya biçimi tanınmıyor ya da parametreleri geçersiz.</summary>
public sealed class VaultFormatException(string message, Exception? inner = null)
    : VaultException(message, inner);

/// <summary>Ana parola başka bir yerde değiştirildiği için kasanın parola ile yeniden açılması gerekiyor.</summary>
public sealed class VaultReopenRequiredException()
    : VaultException("Kasanın ana parolası başka bir yerde değiştirilmiş. Kasayı yeni parolayla yeniden açın.");

/// <summary>Kasa açıldıktan sonra dosya başka bir işlem tarafından değiştirilmiş.</summary>
public sealed class VaultConcurrencyException()
    : VaultException("Kasa dosyası açıldıktan sonra başka bir uygulama tarafından değiştirilmiş. Kaydetmeden önce kasayı yeniden açın.");
