using Vault.Bridge;

// LocalVault native messaging host.
// Tarayıcı bu programı eklenti bağlandığında başlatır (argümanlar: Chrome → çağıran origin,
// Firefox → manifest yolu + eklenti kimliği) ve stdin/stdout üzerinden konuşur. Bu yüzden
// stdout'a mesaj çerçevesi dışında hiçbir şey yazılmamalı.

var hostPath = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "LocalVault.NativeHost.exe");

if (args is ["--register"] or ["--unregister"])
    Console.OutputEncoding = System.Text.Encoding.UTF8;   // bu modlarda stdout mesajlaşma için kullanılmaz; bilgi mesajı buraya yazılabilir

if (args is ["--register"])
{
    NativeMessagingRegistrar.CreateDefault().Register(hostPath);
    Console.WriteLine("LocalVault native host Chrome, Edge ve Firefox için kaydedildi.");
    return 0;
}

if (args is ["--unregister"])
{
    NativeMessagingRegistrar.CreateDefault().Unregister();
    Console.WriteLine("LocalVault native host kaydı kaldırıldı.");
    return 0;
}

// Masaüstü uygulaması aynı klasörde yer alır (yayın paketinde host ile birlikte dağıtılır).
var desktopExe = Path.Combine(AppContext.BaseDirectory, OperatingSystem.IsWindows() ? "LocalVault.exe" : "LocalVault");

using var stdin = Console.OpenStandardInput();
using var stdout = Console.OpenStandardOutput();
var relay = new NativeHostRelay(stdin, stdout, BridgeProtocol.DefaultPipeName, desktopExe);
await relay.RunAsync();
return 0;
