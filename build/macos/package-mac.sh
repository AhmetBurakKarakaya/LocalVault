#!/usr/bin/env bash
# LocalVault macOS paketi: LocalVault.app içeren DMG üretir → artifacts/LocalVault-<sürüm>-<rid>.dmg
#
#   build/macos/package-mac.sh [osx-arm64|osx-x64] [--skip-tests]
#
# macOS'ta çalışır (sips, iconutil, codesign, hdiutil gerekir). Masaüstü uygulaması, native host ve
# CLI, .NET çalışma zamanı dahil (self-contained) LocalVault.app/Contents/MacOS içine yayımlanır.
# Uygulama Apple Developer ID ile değil ad-hoc imzalanır (Apple Silicon'da çalışabilmesi için şart);
# bu yüzden ilk açılışta Gatekeeper onayı gerekir — bkz. KURULUM-mac.txt.
set -euo pipefail

RID="osx-arm64"
SKIP_TESTS=0
for arg in "$@"; do
  case "$arg" in
    osx-arm64|osx-x64) RID="$arg" ;;
    --skip-tests) SKIP_TESTS=1 ;;
    *) echo "Bilinmeyen argüman: $arg" >&2; exit 2 ;;
  esac
done

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$ROOT"
VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props | head -1)"
NAME="LocalVault-$VERSION-$RID"
ARTIFACTS="$ROOT/artifacts"
STAGE="$ARTIFACTS/$NAME"
APP="$STAGE/LocalVault.app"
DMG="$ARTIFACTS/$NAME.dmg"

step() { printf '\033[36m→ %s\033[0m\n' "$1"; }

if [ "$SKIP_TESTS" -eq 0 ]; then
  step ".NET testleri"
  dotnet test --configuration Release --nologo --verbosity quiet
  step "Eklenti testleri"
  node --test 'extension/test/*.test.mjs'
fi

rm -rf "$STAGE" "$DMG" "$DMG.sha256"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"

step "Tarayıcı eklentisi"
node extension/build.mjs

# Windows paketindeki gibi: native host ve CLI masaüstünden SONRA yayımlanır (bkz. package.ps1).
COMMON=(--configuration Release --runtime "$RID" --self-contained true --output "$APP/Contents/MacOS"
        -p:DebugType=none -p:GenerateDocumentationFile=false --nologo --verbosity quiet)
step "Masaüstü uygulaması"
dotnet publish src/Vault.Desktop "${COMMON[@]}"
step "Native host"
dotnet publish src/Vault.NativeHost "${COMMON[@]}"
step "CLI"
dotnet publish src/Vault.Cli "${COMMON[@]}"

for exe in LocalVault LocalVault.NativeHost localvault-cli; do
  grep -q includedFrameworks "$APP/Contents/MacOS/$exe.runtimeconfig.json" \
    || { echo "$exe self-contained yayımlanmamış." >&2; exit 1; }
  chmod +x "$APP/Contents/MacOS/$exe"
done

step "Simge ve Info.plist"
ICONSET="$(mktemp -d)/LocalVault.iconset"
mkdir -p "$ICONSET"
for size in 16 32 128 256 512; do
  sips -z "$size" "$size" build/macos/icon-1024.png --out "$ICONSET/icon_${size}x${size}.png" >/dev/null
  sips -z $((size * 2)) $((size * 2)) build/macos/icon-1024.png --out "$ICONSET/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/LocalVault.icns"
sed "s/@VERSION@/$VERSION/g" build/macos/Info.plist > "$APP/Contents/Info.plist"
plutil -lint "$APP/Contents/Info.plist" >/dev/null

step "Ad-hoc imza"
codesign --force --deep --sign - "$APP"
codesign --verify --deep --strict "$APP"

step "DMG içeriği"
cp -R extension/dist/chrome "$STAGE/Eklenti - Chrome ve Edge"
cp -R extension/dist/firefox "$STAGE/Eklenti - Firefox"
cp build/macos/KURULUM-mac.txt "$STAGE/KURULUM.txt"
ln -s /Applications "$STAGE/Applications"

step "DMG"
# GitHub'ın macOS makinelerinde hdiutil ara sıra "Resource busy" ile düşer; birkaç kez dene.
for attempt in 1 2 3 4 5; do
  if hdiutil create -volname "LocalVault $VERSION" -srcfolder "$STAGE" -ov -format UDZO "$DMG" >/dev/null; then
    break
  fi
  [ "$attempt" -eq 5 ] && { echo "DMG oluşturulamadı." >&2; exit 1; }
  sleep $((attempt * 3))
done
(cd "$ARTIFACTS" && shasum -a 256 "$NAME.dmg" > "$NAME.dmg.sha256")

SIZE_MB=$(du -m "$DMG" | cut -f1)
printf '\n\033[32mPaket hazır: %s (%s MB)\033[0m\n' "$DMG" "$SIZE_MB"
echo "SHA-256   : $(cut -d' ' -f1 "$DMG.sha256")"
