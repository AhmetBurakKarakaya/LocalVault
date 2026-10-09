<#
.SYNOPSIS
    LocalVault kurulum paketini üretir: artifacts\LocalVault-<sürüm>-win-x64.zip

.DESCRIPTION
    Masaüstü uygulaması, native host ve CLI aynı klasöre, .NET çalışma zamanı dahil (self-contained)
    yayımlanır; kullanıcının .NET kurmasına gerek yoktur. Tarayıcı eklentisi (Chrome/Edge ve Firefox)
    ve kurulum betikleri de pakete eklenir.

.PARAMETER Runtime   Hedef platform (win-x64, win-arm64).
.PARAMETER SkipTests Testleri çalıştırmadan paketle.

.EXAMPLE
    pwsh build/package.ps1
#>
[CmdletBinding()]
param(
    [string]$Runtime = 'win-x64',
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$version = ([xml](Get-Content (Join-Path $root 'Directory.Build.props'))).Project.PropertyGroup.Version
$name = "LocalVault-$version-$Runtime"
$artifacts = Join-Path $root 'artifacts'
$stage = Join-Path $artifacts $name
$zip = Join-Path $artifacts "$name.zip"

function Invoke-Checked([string]$what, [scriptblock]$command) {
    Write-Host "→ $what" -ForegroundColor Cyan
    & $command
    if ($LASTEXITCODE -ne 0) { throw "$what başarısız (çıkış kodu $LASTEXITCODE)." }
}

Push-Location $root
try {
    if (-not $SkipTests) {
        Invoke-Checked '.NET testleri' { dotnet test --configuration Release --nologo --verbosity quiet }
        Invoke-Checked 'Eklenti testleri' { node --test 'extension/test/*.test.mjs' }
    }

    Remove-Item $stage, $zip -Recurse -Force -ErrorAction SilentlyContinue
    $app = Join-Path $stage 'app'

    Invoke-Checked 'Tarayıcı eklentisi' { node extension/build.mjs }

    # Native host ve CLI, masaüstü uygulamasından SONRA yayımlanır: masaüstü projesi host'u kendi
    # çıktısına çalışma zamanına bağımlı olarak kopyalar; ardından gelen self-contained yayın bunun üzerine yazar.
    $common = @('--configuration', 'Release', '--runtime', $Runtime, '--self-contained', 'true', '--output', $app,
                '-p:DebugType=none', '-p:GenerateDocumentationFile=false', '--nologo', '--verbosity', 'quiet')
    Invoke-Checked 'Masaüstü uygulaması' { dotnet publish src/Vault.Desktop @common }
    Invoke-Checked 'Native host' { dotnet publish src/Vault.NativeHost @common }
    Invoke-Checked 'CLI' { dotnet publish src/Vault.Cli @common }

    Write-Host '→ Paket klasörü' -ForegroundColor Cyan
    New-Item -ItemType Directory -Force (Join-Path $stage 'extension') | Out-Null
    Copy-Item (Join-Path $root 'extension/dist/chrome') (Join-Path $stage 'extension/chrome') -Recurse
    Copy-Item (Join-Path $root 'extension/dist/firefox') (Join-Path $stage 'extension/firefox') -Recurse
    Copy-Item (Join-Path $PSScriptRoot 'installer/*') $stage
    Set-Content (Join-Path $stage 'VERSION') $version -NoNewline -Encoding ascii

    # Paketin bütünlüğünü kontrol et: üç exe de self-contained olmalı.
    foreach ($exe in 'LocalVault', 'LocalVault.NativeHost', 'localvault-cli') {
        $config = Get-Content (Join-Path $app "$exe.runtimeconfig.json") -Raw | ConvertFrom-Json
        if (-not $config.runtimeOptions.includedFrameworks) { throw "$exe self-contained yayımlanmamış." }
    }

    Write-Host '→ Zip' -ForegroundColor Cyan
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal
    $hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
    Set-Content "$zip.sha256" "$hash  $name.zip" -Encoding ascii

    $sizeMb = [math]::Round((Get-Item $zip).Length / 1MB, 1)
    Write-Host ""
    Write-Host "Paket hazır: $zip ($sizeMb MB)" -ForegroundColor Green
    Write-Host "SHA-256   : $hash"
}
finally {
    Pop-Location
}
