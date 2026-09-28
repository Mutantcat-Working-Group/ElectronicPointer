#!/usr/bin/env bash
# Builds the Linux artifacts for one runtime from artifacts/out/<rid>:
#
#   electronicpointer_<version>_<arch>.deb        installable package
#   ElectronicPointer-<version>-<arch>.AppImage   one file, unpack and run
#   electronicpointer-<version>-<rid>.tar.gz      portable tree with no install step
#
# The deb, the AppImage and the portable tarball all get the same install layout, so the
# autostart entry the application writes for itself after installation points at the same
# executable no matter which of the three is used.
# appimagetool is downloaded because FUSE is unavailable in a container: it is extracted and
# run from its own copy instead of being mounted.
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]:-$0}")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"

RID="${1:-linux-x64}"
OUT="$ROOT/artifacts/out/$RID"

if [ ! -f "$OUT/ElectronicPointer" ]; then
    echo "error: $OUT/ElectronicPointer is missing." >&2
    echo "run pwsh packaging/publish.ps1 -Rid $RID first" >&2
    exit 1
fi

VERSION="$(sed -n '1p' "$OUT/buildstamp.txt")"

APPID=org.mutantcat.electronicpointer
PKGNAME=electronicpointer
PAYLOAD="usr/lib/$PKGNAME"
LAUNCHER="usr/bin/$PKGNAME"

case "$RID" in
    linux-x64)   ARCH=amd64;  APPIMAGEARCH=x86_64 ;;
    linux-arm64) ARCH=arm64;  APPIMAGEARCH=aarch64 ;;
    *) echo "error: $RID is not a Linux runtime" >&2; exit 1 ;;
esac

DIST="$ROOT/artifacts/dist"
STAGE="$ROOT/artifacts/stage"
mkdir -p "$DIST"

stage_tree() {
    # $1 : destination root. Everything under it is laid out the way the filesystem
    # hierarchy standard expects, which is also the layout dpkg-deb consumes.
    local dest="$1"

    mkdir -p "$dest/$PAYLOAD"
    cp -a "$OUT/." "$dest/$PAYLOAD/"
    rm -f "$dest/$PAYLOAD/buildstamp.txt"

    mkdir -p "$dest/usr/bin"
    ln -sfn "../lib/$PKGNAME/ElectronicPointer" "$dest/$LAUNCHER"

    install -Dm644 "$ROOT/packaging/linux/$APPID.desktop" \
        "$dest/usr/share/applications/$APPID.desktop"
    install -Dm644 "$ROOT/packaging/linux/$APPID.metainfo.xml" \
        "$dest/usr/share/metainfo/$APPID.metainfo.xml"
    install -Dm644 "$ROOT/LICENSE" "$dest/usr/share/doc/$PKGNAME/copyright"

    local size
    for size in 16 24 32 48 64 128 256; do
        install -Dm644 "$ROOT/packaging/assets/icons/hicolor/${size}x${size}/apps/$APPID.png" \
            "$dest/usr/share/icons/hicolor/${size}x${size}/apps/$APPID.png"
    done
}

echo "==> portable tarball"
PORTABLE="$STAGE/portable/$PKGNAME-$VERSION-$RID"
rm -rf "$STAGE/portable"
mkdir -p "$PORTABLE"
cp -a "$OUT/." "$PORTABLE/"
rm -f "$PORTABLE/buildstamp.txt"
tar -C "$STAGE/portable" -czf "$DIST/$PKGNAME-$VERSION-$RID.tar.gz" "$PKGNAME-$VERSION-$RID"

echo "==> debian package ($ARCH)"
DEBROOT="$STAGE/deb"
rm -rf "$DEBROOT"
stage_tree "$DEBROOT"

mkdir -p "$DEBROOT/DEBIAN"
chmod 755 "$DEBROOT/DEBIAN"
INSTALLED_KB="$(du -sk "$DEBROOT/usr" | awk '{print $1}')"
sed -e "s/@ARCH@/$ARCH/" -e "s/@SIZE@/$INSTALLED_KB/" \
    "$ROOT/packaging/linux/debian/control" > "$DEBROOT/DEBIAN/control"
install -Dm755 "$ROOT/packaging/linux/debian/postinst" "$DEBROOT/DEBIAN/postinst"
install -Dm755 "$ROOT/packaging/linux/debian/prerm" "$DEBROOT/DEBIAN/prerm"

dpkg-deb --build --root-owner-group "$DEBROOT" \
    "$DIST/${PKGNAME}_${VERSION}_${ARCH}.deb"

echo "==> AppImage ($APPIMAGEARCH)"
APPDIR="$STAGE/appimage/AppDir"
rm -rf "$APPDIR" "$STAGE/appimage-tool/squashfs-root"
mkdir -p "$APPDIR/$PAYLOAD"
cp -a "$OUT/." "$APPDIR/$PAYLOAD/"
rm -f "$APPDIR/$PAYLOAD/buildstamp.txt"

mkdir -p "$APPDIR/usr/bin"
ln -sfn "../lib/$PKGNAME/ElectronicPointer" "$APPDIR/$LAUNCHER"

install -Dm644 "$ROOT/packaging/linux/$APPID.desktop" \
    "$APPDIR/usr/share/applications/$APPID.desktop"
for size in 16 24 32 48 64 128 256; do
    install -Dm644 "$ROOT/packaging/assets/icons/hicolor/${size}x${size}/apps/$APPID.png" \
        "$APPDIR/usr/share/icons/hicolor/${size}x${size}/apps/$APPID.png"
done

install -m644 "$ROOT/icon.png" "$APPDIR/.DirIcon"
install -Dm755 "$ROOT/packaging/linux/appimage/AppRun" "$APPDIR/AppRun"

mkdir -p "$STAGE/appimage-tool"
(
    cd "$STAGE/appimage-tool" || exit 1
    if [ ! -f appimagetool ]; then
        curl -fsSL -o appimagetool \
            "https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-${APPIMAGEARCH}.AppImage"
    fi
    chmod +x appimagetool
    rm -rf squashfs-root
    ./appimagetool --appimage-extract >/dev/null
    chmod +x squashfs-root/AppRun
    ARCH="$APPIMAGEARCH" ./squashfs-root/AppRun "$APPDIR" \
        "$DIST/ElectronicPointer-$VERSION-$APPIMAGEARCH.AppImage"
)

echo
echo "Linux artifacts in $DIST:"
ls -1 "$DIST" | sed 's/^/  /'
