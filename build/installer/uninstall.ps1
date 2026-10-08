<#
.SYNOPSIS
    LocalVault'u kaldırır. Kasa dosyanız (%APPDATA%\LocalVault\vault.json) SİLİNMEZ.

.PARAMETER InstallDir Kurulum klasörü (varsayılan: bu betiğin bulunduğu klasör).
.PARAMETER Quiet      Onay sormadan kaldırır.
.PARAMETER FilesOnly  Yalnızca dosyaları siler (kısayol/kayıt defteri/tarayıcı kaydına dokunmaz; test için).
#>
[CmdletBinding()]
param(
    [string]$InstallDir,
    [switch]$Quiet,
    [switch]$FilesOnly
)

$ErrorActionPreference = 'Stop'
# Windows PowerShell 5.1'de $PSScriptRoot param() varsayılanlarında boş gelir; burada atanmalı.
if (-not $InstallDir) { $InstallDir = $PSScriptRoot }

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

$InstallDir = Get-LongPath $InstallDir

if (-not (Test-Path (Join-Path $InstallDir 'LocalVault.exe'))) {
    throw "LocalVault bu klasörde kurulu görünmüyor: $InstallDir"
}

if (-not $Quiet) {
    $answer = Read-Host "LocalVault kaldırılsın mı? Kasa dosyanız silinmez. [e/H]"
    if ($answer -notmatch '^(e|evet|y|yes)$') { Write-Host "İptal edildi."; exit 1 }
}

# Çalışan kopyaları kapat
Get-Process -Name 'LocalVault', 'LocalVault.NativeHost' -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -and (Get-LongPath $_.Path).StartsWith($InstallDir + '\', [StringComparison]::OrdinalIgnoreCase) } |
    Stop-Process -Force
Start-Sleep -Milliseconds 500

if (-not $FilesOnly) {
    # Tarayıcı kaydını kaldır
    $hostExe = Join-Path $InstallDir 'LocalVault.NativeHost.exe'
    if (Test-Path $hostExe) {
        $previous = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'   # PowerShell 5.1 stderr çıktısını hata sayıp betiği durdurmasın
        & $hostExe --unregister 2>&1 | Out-Null
        $exitCode = $LASTEXITCODE
        $ErrorActionPreference = $previous
    }

    # Kısayollar
    foreach ($folder in 'Programs', 'Desktop', 'Startup') {
        $link = Join-Path ([Environment]::GetFolderPath($folder)) 'LocalVault.lnk'
        if (Test-Path $link) { Remove-Item $link -Force }
    }

    # PATH
    $path = [Environment]::GetEnvironmentVariable('Path', 'User')
    if ($path) {
        # Girdiler kısa veya uzun biçimde eklenmiş olabilir; ikisini de karşılaştır.
        $entries = @($path -split ';')
        $ours = @($entries | Where-Object { $_ -and (Get-LongPath $_) -eq $InstallDir })
        if ($ours.Count -gt 0) {
            [Environment]::SetEnvironmentVariable('Path', (($entries | Where-Object { $ours -notcontains $_ }) -join ';'), 'User')
        }
    }

    Remove-Item 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\LocalVault' -Recurse -Force -ErrorAction SilentlyContinue
}

# Dosyalar (bu betik de bu klasörde; PowerShell betiği belleğe yüklediği için silinebilir)
Set-Location $env:TEMP
Remove-Item $InstallDir -Recurse -Force -ErrorAction SilentlyContinue
if (Test-Path $InstallDir) {
    # Kilitli dosya kaldıysa kısa bir gecikmeyle tekrar dene.
    Start-Process cmd.exe -ArgumentList "/c timeout /t 2 >nul & rmdir /s /q `"$InstallDir`"" -WindowStyle Hidden
}

Write-Host "LocalVault kaldırıldı. Kasa dosyanız ve ayarlarınız %APPDATA%\LocalVault klasöründe duruyor." -ForegroundColor Green
Write-Host "Tarayıcı eklentisini tarayıcının uzantılar sayfasından ayrıca kaldırın."
