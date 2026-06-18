#!/usr/bin/env bash
set -euo pipefail
PUBLISH_DIR="$1"   # e.g. publish/desktop-osx-arm64 (a FOLDER publish, self-contained)
VERSION="$2"       # e.g. 1.2.3
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
APP="NHibernaut.app"

rm -rf "$APP"
mkdir -p "$APP/Contents/MacOS" "$APP/Contents/Resources"
cp -R "$PUBLISH_DIR"/* "$APP/Contents/MacOS/"
chmod +x "$APP/Contents/MacOS/nhibernaut-app"

# Icon: AppIcon.png (1024x1024) -> AppIcon.icns via the macOS toolchain.
ICONSET="$(mktemp -d)/AppIcon.iconset"; mkdir -p "$ICONSET"
for s in 16 32 128 256 512; do
  sips -z $s $s "$HERE/AppIcon.png" --out "$ICONSET/icon_${s}x${s}.png" >/dev/null
  sips -z $((s*2)) $((s*2)) "$HERE/AppIcon.png" --out "$ICONSET/icon_${s}x${s}@2x.png" >/dev/null
done
iconutil -c icns "$ICONSET" -o "$APP/Contents/Resources/AppIcon.icns"

sed "s/__VERSION__/${VERSION}/g" "$HERE/Info.plist.template" > "$APP/Contents/Info.plist"

# Ad-hoc sign so it launches locally (still unsigned/un-notarized for Gatekeeper — documented).
codesign --force --deep --sign - "$APP"
echo "Built $APP"
