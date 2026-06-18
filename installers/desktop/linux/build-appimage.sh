#!/usr/bin/env bash
set -euo pipefail
PUBLISH_DIR="$1"   # publish/desktop-linux-x64 (self-contained, single-file ok)
VERSION="$2"
HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
APPDIR=AppDir
rm -rf "$APPDIR"; mkdir -p "$APPDIR/usr/bin"
cp -R "$PUBLISH_DIR"/* "$APPDIR/usr/bin/"
chmod +x "$APPDIR/usr/bin/nhibernaut-app"
install -m755 "$HERE/AppRun" "$APPDIR/AppRun"
install -m644 "$HERE/nhibernaut-app.desktop" "$APPDIR/nhibernaut-app.desktop"
cp "$HERE/../macos/AppIcon.png" "$APPDIR/nhibernaut-app.png"
wget -qO appimagetool "https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage"
chmod +x appimagetool
# --appimage-extract-and-run lets appimagetool run without FUSE/libfuse2 on the CI runner
# (ubuntu-24.04 dropped libfuse2). The produced AppImage still needs FUSE on the end-user machine.
ARCH=x86_64 ./appimagetool --appimage-extract-and-run "$APPDIR" "nhibernaut-app-${VERSION}-linux-x64.AppImage"
echo "Built nhibernaut-app-${VERSION}-linux-x64.AppImage"
