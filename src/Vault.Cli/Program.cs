using System.Text;
using Vault.Cli;
using Vault.Core;

Console.OutputEncoding = Encoding.UTF8;
Console.InputEncoding = Encoding.UTF8;

const string Help = """
    LocalVault — yerel, şifreli parola kasası

    Kullanım: localvault-cli <komut> [argümanlar] [--vault <yol>] [--password-stdin]

    Komutlar:
      init                         Yeni kasa oluşturur
      info                         Kasa dosyası bilgilerini gösterir (parola gerekmez)
      list [arama]                 Kayıtları listeler
      show <kayıt> [--reveal]      Kaydın ayrıntıları (--reveal parolayı gösterir)
      add --title <ad> [seçenekler]
      edit <kayıt> [seçenekler] [--clear-totp]
      remove <kayıt> [--yes]
      totp <kayıt> [--watch]       Güncel OTP kodu (--watch: canlı geri sayım)
      match <url>                  Bu adreste hangi kayıtların önerileceğini gösterir
      generate [--length N] [--no-symbols] [--no-digits] [--no-upper] [--no-lower] [--exclude-ambiguous]
      passwd                       Ana parolayı değiştirir
      export <dosya> [--yes] [--force]   ŞİFRESİZ JSON'a dışa aktarır
      import <dosya>               Şifresiz JSON'dan içe aktarır (birleştirir)
      import-csv <dosya> [--format auto|keepassxc|bitwarden|1password|generic] [--yes]
                                   Başka parola yöneticisinin CSV dışa aktarımını içe aktarır

    Kayıt seçenekleri (add/edit):
      --title <ad>  --username <ad>  --url <adres> (birden çok kez)  --tag <etiket> (birden çok kez)
      --password <parola> | --generate [--length N]
      --totp <otpauth://... | BASE32>  [--totp-digits 6] [--totp-period 30] [--totp-algorithm SHA1]
      --match domain|host|startswith|exact|never   --notes <metin>

    <kayıt>: ID (ilk 4+ karakter yeterli) veya başlık.
    Varsayılan kasa: %APPDATA%\LocalVault\vault.json (LOCALVAULT_PATH ile değiştirilebilir)
    --password-stdin: ana parolayı stdin'in ilk satırından okur (betikler için).
    """;

try
{
    var cli = CliArgs.Parse(args);
    if (cli.Command is null or "help" || cli.Has("help"))
    {
        Console.WriteLine(Help);
        return 0;
    }

    return cli.Command switch
    {
        "init" => Commands.Init(cli),
        "info" => Commands.Info(cli),
        "list" or "ls" => Commands.List(cli),
        "show" => Commands.Show(cli),
        "add" => Commands.Add(cli),
        "edit" => Commands.Edit(cli),
        "remove" or "rm" => Commands.Remove(cli),
        "totp" or "otp" => Commands.Totp(cli),
        "match" => Commands.Match(cli),
        "generate" or "gen" => Commands.Generate(cli),
        "passwd" => Commands.ChangeMasterPassword(cli),
        "export" => Commands.Export(cli),
        "import" => Commands.Import(cli),
        "import-csv" => Commands.ImportCsv(cli),
        _ => throw new UsageException($"Bilinmeyen komut: '{cli.Command}'. Yardım için: localvault-cli help"),
    };
}
catch (UsageException ex)
{
    ConsoleUi.Error(ex.Message);
    return 2;
}
catch (Exception ex) when (ex is VaultException or ArgumentException or FormatException or IOException or UnauthorizedAccessException)
{
    ConsoleUi.Error(ex.Message);
    return 1;
}
