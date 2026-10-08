<#
.SYNOPSIS
    LocalVault'u geçerli kullanıcı için kurar (yönetici yetkisi gerekmez).

.DESCRIPTION
    - Uygulamayı %LOCALAPPDATA%\Programs\LocalVault altına kopyalar (güncellemede eski dosyaları değiştirir)
    - Başlat menüsü kısayolu ve "Uygulamalar ve özellikler" kaydı oluşturur
    - Tarayıcı eklentisinin bağlanabilmesi için native host'u Chrome, Edge ve Firefox'a kaydeder
    Kasa dosyanıza (%APPDATA%\LocalVault) dokunulmaz.

.PARAMETER InstallDir      Kurulum klasörü.
.PARAMETER DesktopShortcut Masaüstüne kısayol ekler.
.PARAMETER AutoStart       Windows açılışında LocalVault'u başlatır.
.PARAMETER AddToPath       localvault-cli komutunu kullanıcı PATH'ine ekler.
.PARAMETER NoLaunch        Kurulumdan sonra uygulamayı başlatmaz.
.PARAMETER FilesOnly       Yalnızca dosyaları kopyalar (kısayol, kayıt defteri, tarayıcı kaydı yok; test için).
#>
[CmdletBinding()]
param(
    [string]$InstallDir = (Join-Path $env:LOCALAPPDATA 'Programs\LocalVault'),
    [switch]$DesktopShortcut,
    [switch]$AutoStart,
    [switch]$AddToPath,
    [switch]$NoLaunch,
    [switch]$FilesOnly
)

$ErrorActionPreference = 'Stop'
$source = $PSScriptRoot
$version = (Get-Content (Join-Path $source 'VERSION') -Raw).Trim()

function Step($text) { Write-Host "  • $text" }

# Kısa (8.3) yolları uzun biçime çevirir: "C:\Users\KULLAN~1" → "C:\Users\kullanici.adi".
# PATH girdisi ve çalışan süreç eşleştirmesi bu iki biçim karışınca başarısız olur.
function Get-LongPath([string]$path) {
    if (-not (Test-Path -LiteralPath $path)) { return $path }
    if (-not ('LocalVaultSetup.NativePath' -as [type])) {
        Add-Type -Namespace LocalVaultSetup -Name NativePath -MemberDefinition @'
[DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
public static extern uint GetLongPathName(string shortPath, System.Text.StringBuilder longPath, uint size);
'@
    }
    $buffer = New-Object System.Text.StringBuilder 1024
    if ([LocalVaultSetup.NativePath]::GetLongPathName($path, $buffer, 1024) -gt 0) { return $buffer.ToString().TrimEnd('\') }
    return $path
}


Write-Host ""
Write-Host "LocalVault $version kuruluyor → $InstallDir" -ForegroundColor Cyan

if (-not (Test-Path (Join-Path $source 'app\LocalVault.exe'))) {
    throw "Paket eksik: app\LocalVault.exe bulunamadı. Zip dosyasını tamamen çıkardığınızdan emin olun."
}

$InstallDir = Get-LongPath $InstallDir

# 1) Çalışan kopyaları kapat (dosyalar kilitli olmasın). Kasa her değişiklikte diske yazıldığı için veri kaybı olmaz.
$running = Get-Process -Name 'LocalVault', 'LocalVault.NativeHost' -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and (Get-LongPath $_.Path).StartsWith($InstallDir + '\', [StringComparison]::OrdinalIgnoreCase) }
if ($running) {
    Step "Çalışan LocalVault kapatılıyor"
    $running | Stop-Process -Force
    Start-Sleep -Milliseconds 500
}

# 2) Dosyalar: uygulama klasörünü yansıt (eski sürümden kalan dosyaları da temizler)
Step "Dosyalar kopyalanıyor"
New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
$InstallDir = Get-LongPath $InstallDir
robocopy (Join-Path $source 'app') $InstallDir /MIR /XD extension /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "Dosyalar kopyalanamadı (robocopy kodu $LASTEXITCODE)." }
robocopy (Join-Path $source 'extension') (Join-Path $InstallDir 'extension') /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "Eklenti dosyaları kopyalanamadı (robocopy kodu $LASTEXITCODE)." }
Copy-Item (Join-Path $source 'uninstall.ps1'), (Join-Path $source 'VERSION') $InstallDir -Force

$exe = Join-Path $InstallDir 'LocalVault.exe'
$hostExe = Join-Path $InstallDir 'LocalVault.NativeHost.exe'

if (-not $FilesOnly) {
    # 3) Kısayollar
    $shell = New-Object -ComObject WScript.Shell
    function New-Shortcut($path) {
        $link = $shell.CreateShortcut($path)
        $link.TargetPath = $exe
        $link.WorkingDirectory = $InstallDir
        $link.IconLocation = "$exe,0"
        $link.Description = 'LocalVault parola yöneticisi'
        $link.Save()
    }
    Step "Başlat menüsü kısayolu oluşturuluyor"
    New-Shortcut (Join-Path ([Environment]::GetFolderPath('Programs')) 'LocalVault.lnk')
    if ($DesktopShortcut) {
        Step "Masaüstü kısayolu oluşturuluyor"
        New-Shortcut (Join-Path ([Environment]::GetFolderPath('Desktop')) 'LocalVault.lnk')
    }
    if ($AutoStart) {
        Step "Windows açılışında başlatma ekleniyor"
        New-Shortcut (Join-Path ([Environment]::GetFolderPath('Startup')) 'LocalVault.lnk')
    }

    # 4) Tarayıcı kaydı (masaüstü uygulaması her açılışta da doğrular)
    Step "Tarayıcı eklentisi için native host kaydediliyor (Chrome, Edge, Firefox)"
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'   # PowerShell 5.1 stderr çıktısını hata sayıp betiği durdurmasın
    & $hostExe --register 2>&1 | Out-Null
    $exitCode = $LASTEXITCODE
    $ErrorActionPreference = $previous
    if ($exitCode -ne 0) { Write-Warning "Native host kaydedilemedi; uygulama ilk açılışta yeniden deneyecek." }

    # 5) Uygulamalar ve özellikler kaydı
    Step "Kaldırma bilgisi ekleniyor"
    $key = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\LocalVault'
    New-Item -Path $key -Force | Out-Null
    $size = [int]((Get-ChildItem $InstallDir -Recurse -File | Measure-Object Length -Sum).Sum / 1KB)
    $values = @{
        DisplayName     = 'LocalVault'
        DisplayVersion  = $version
        Publisher       = 'LocalVault'
        InstallLocation = $InstallDir
        DisplayIcon     = "$exe,0"
        UninstallString = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$InstallDir\uninstall.ps1`""
        QuietUninstallString = "powershell.exe -NoProfile -ExecutionPolicy Bypass -File `"$InstallDir\uninstall.ps1`" -Quiet"
    }
    foreach ($name in $values.Keys) { Set-ItemProperty -Path $key -Name $name -Value $values[$name] }
    Set-ItemProperty -Path $key -Name EstimatedSize -Value $size -Type DWord
    Set-ItemProperty -Path $key -Name NoModify -Value 1 -Type DWord
    Set-ItemProperty -Path $key -Name NoRepair -Value 1 -Type DWord

    # 6) İsteğe bağlı: CLI'yi PATH'e ekle
    if ($AddToPath) {
        $path = [Environment]::GetEnvironmentVariable('Path', 'User')
        if (($path -split ';') -notcontains $InstallDir) {
            Step "localvault-cli PATH'e ekleniyor"
            [Environment]::SetEnvironmentVariable('Path', ($path.TrimEnd(';') + ";$InstallDir"), 'User')
        }
    }
}

Write-Host ""
Write-Host "Kurulum tamamlandı." -ForegroundColor Green
Write-Host ""
Write-Host "Tarayıcı eklentisini yüklemek için:" -ForegroundColor Cyan
Write-Host "  Chrome / Edge : chrome://extensions (edge://extensions) → Geliştirici modu → Paketlenmemiş öğe yükle"
Write-Host "                  klasör: $InstallDir\extension\chrome"
Write-Host "  Firefox       : about:debugging#/runtime/this-firefox → Geçici eklenti yükle"
Write-Host "                  dosya : $InstallDir\extension\firefox\manifest.json"
Write-Host "  Ardından eklenti simgesi → Bağlan → LocalVault penceresinde kodu karşılaştırıp İzin ver."
Write-Host ""
Write-Host "Kaldırmak için: Ayarlar → Uygulamalar → LocalVault → Kaldır"
Write-Host ""

if (-not $NoLaunch -and -not $FilesOnly) {
    Start-Process $exe
}
