#!/usr/bin/env bash
# Builds the macOS artifacts for one runtime from artifacts/out/<rid>:
#
#   ElectronicPointer-<version>-<rid>.app   the bundle a user drags to /Applications
#   ElectronicPointer-<version>-<rid>.dmg   disk image holding the bundle
#
# The bundle keeps the publish output as-is under Contents/MacOS. .NET resolves the native
# Skia and Avalonia libraries from that directory, so relocating or rewriting links would
# break something that already works.
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]:-$0}")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"

RID="${1:-osx-arm64}"
OUT="$ROOT/artifacts/out/$RID"

if [ ! -x "$OUT/ElectronicPointer" ]; then
    echo "error: $OUT/ElectronicPointer is missing." >&2
    echo "run pwsh packaging/publish.ps1 -Rid $RID first" >&2
    exit 1
fi

VERSION="$(sed -n '1p' "$OUT/buildstamp.txt")"
APPID=org.mutantcat.electronicpointer

APPNAME=ElectronicPointer
DIST="$ROOT/artifacts/dist"
STAGE="$ROOT/artifacts/stage/macos"
mkdir -p "$DIST" "$STAGE"

echo "==> .app bundle"
BUNDLE="$STAGE/$APPNAME.app"
rm -rf "$BUNDLE"
mkdir -p "$BUNDLE/Contents/MacOS" "$BUNDLE/Contents/Resources"

cp -a "$OUT/." "$BUNDLE/Contents/MacOS/"
rm -f "$BUNDLE/Contents/MacOS/buildstamp.txt"

sed -e "s/@VERSION@/$VERSION/" \
    -e "s/@APPID@/$APPID/" \
    "$ROOT/packaging/macos/Info.plist" > "$BUNDLE/Contents/Info.plist"

echo "==> icon"
ICONSET="$STAGE/$APPNAME.iconset"
rm -rf "$ICONSET"
mkdir -p "$ICONSET"
# The root icon.png is the only icon source; every other icon asset in the repository is
# generated from it by packaging/tools/generate-icons.ps1.
SRC="$ROOT/icon.png"

make_icon() {
    # $1: pixels, $2: iconset name. @2x entries hold twice the pixels the name claims.
    sips -z "$1" "$1" "$SRC" --out "$ICONSET/icon_$2.png" >/dev/null
}

make_icon 16   '16x16'
make_icon 32   '16x16@2x'
make_icon 32   '32x32'
make_icon 64   '32x32@2x'
make_icon 128  '128x128'
make_icon 256  '128x128@2x'
make_icon 256  '256x256'
make_icon 512  '256x256@2x'
make_icon 512  '512x512'
make_icon 1024 '512x512@2x'

# The lower case name is not a typo: CFBundleIconFile in Info.plist looks for exactly
# this file, and a mismatch leaves the bundle with the generic default icon.
iconutil -c icns "$ICONSET" -o "$BUNDLE/Contents/Resources/electronicpointer.icns"

SIGNING_IDENTITY="${APPLE_SIGNING_IDENTITY:-}"
if [ -n "$SIGNING_IDENTITY" ]; then
    echo "==> code signing as $SIGNING_IDENTITY"
    # --deep signs the bundled native libraries too. The entitlements carry the hardened
    # runtime exceptions a self-contained .NET payload needs: libSkiaSharp and the
    # libSystem.* libraries in the tree are not signed by the same identity.
    codesign --deep --force --options runtime \
        --entitlements "$ROOT/packaging/macos/entitlements.mac.plist" \
        --sign "$SIGNING_IDENTITY" "$BUNDLE"
    codesign --verify --deep --strict --verbose=1 "$BUNDLE"
else
    # No identity means a fork, a workstation without a developer certificate, or a runner
    # whose secrets were never configured. The image still ships a signature: the ad-hoc
    # identity ("-") seals the bundle without a team ID, which is what every other repo in
    # the group does on hosted runners. The hardened runtime is left off because an ad-hoc
    # seal cannot be notarized and buys nothing here.
    echo "==> ad-hoc code signing (set APPLE_SIGNING_IDENTITY for a real identity)"
    codesign --deep --force \
        --entitlements "$ROOT/packaging/macos/entitlements.mac.plist" \
        --sign - "$BUNDLE"
    codesign --verify --deep --strict --verbose=1 "$BUNDLE"
    codesign -dv --verbose=1 "$BUNDLE" 2>&1 | grep -q 'Signature=adhoc'
fi

echo "==> dmg"
DMG="$DIST/$APPNAME-$VERSION-$RID.dmg"
rm -f "$DMG"
# The image root is staged so the mounted volume carries the drop link macOS users expect:
# a shortcut to /Applications next to the bundle, dragged onto it to "install" the app.
# Without it the image only offers a bare bundle and no way to place it.
DMG_ROOT="$STAGE/dmg-root"
rm -rf "$DMG_ROOT"
mkdir -p "$DMG_ROOT"
cp -a "$BUNDLE" "$DMG_ROOT/"
ln -s /Applications "$DMG_ROOT/Applications"
hdiutil create -volname "$APPNAME $VERSION" -srcfolder "$DMG_ROOT" -ov -format UDZO "$DMG"
# Seal the image itself too. A signature inside the bundle is invisible to gatekeeper
# until the volume is mounted, and this keeps the downloaded file signed on its own.
codesign --force --sign - --timestamp=none "$DMG"
codesign --verify --verbose=1 "$DMG"

if [ -n "${APPLE_ID:-}" ] && [ -n "${APPLE_TEAM_ID:-}" ]; then
    echo "==> notarizing"
    ZIP="$STAGE/$APPNAME-notarize.zip"
    rm -f "$ZIP"
    ditto -c -k --sequesterRsrc --keepParent "$BUNDLE" "$ZIP"
    xcrun notarytool submit "$ZIP" \
        --apple-id "$APPLE_ID" \
        --team-id "$APPLE_TEAM_ID" \
        --password "${APPLE_APP_PASSWORD:-}" \
        --wait
    xcrun stapler staple "$DMG"
fi

echo
echo "macOS artifacts in $DIST:"
ls -1 "$DIST" | grep -F "$RID" | sed 's/^/  /'
