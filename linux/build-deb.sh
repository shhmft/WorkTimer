#!/bin/bash
# Сборка deb-пакета WorkTimer. Запускать из папки linux/:  ./build-deb.sh
set -e

VERSION="1.1.1"
PKG="worktimer"
ARCH="all"
HERE="$(cd "$(dirname "$0")" && pwd)"
DEST="$HERE/build"
# Собираем во временной папке в файловой системе Linux: на смонтированных
# дисках Windows права всегда 777, а dpkg-deb такое отвергает.
OUT="$(mktemp -d)"
ROOT="$OUT/${PKG}_${VERSION}_${ARCH}"

trap 'rm -rf "$OUT"' EXIT
mkdir -p "$DEST"
mkdir -p "$ROOT/DEBIAN"
mkdir -p "$ROOT/usr/bin"
mkdir -p "$ROOT/usr/share/applications"
mkdir -p "$ROOT/usr/share/icons/hicolor/scalable/apps"
mkdir -p "$ROOT/usr/share/doc/$PKG"

install -m 0755 "$HERE/worktimer"          "$ROOT/usr/bin/worktimer"
install -m 0644 "$HERE/worktimer.desktop"  "$ROOT/usr/share/applications/worktimer.desktop"
install -m 0644 "$HERE/worktimer.svg"      "$ROOT/usr/share/icons/hicolor/scalable/apps/worktimer.svg"

# документация
if [ -f "$HERE/README.md" ]; then
  install -m 0644 "$HERE/README.md" "$ROOT/usr/share/doc/$PKG/README.md"
fi

cat > "$ROOT/usr/share/doc/$PKG/copyright" <<'EOF'
Format: https://www.debian.org/doc/packaging-manuals/copyright-format/1.0/
Upstream-Name: worktimer

Files: *
Copyright: 2026 shhmft
License: MIT
 Permission is hereby granted, free of charge, to any person obtaining a copy
 of this software and associated documentation files (the "Software"), to deal
 in the Software without restriction, including without limitation the rights
 to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 copies of the Software, and to permit persons to whom the Software is
 furnished to do so, subject to the following conditions:
 .
 The above copyright notice and this permission notice shall be included in
 all copies or substantial portions of the Software.
 .
 THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
 THE SOFTWARE.
EOF

printf '%s (%s) unstable; urgency=low\n\n  * Первая сборка порта под Linux.\n\n -- shhmft <lutcenko0710@gmail.com>  %s\n' \
  "$PKG" "$VERSION" "$(date -R)" > "$OUT/changelog"
gzip -9 -n -c "$OUT/changelog" > "$ROOT/usr/share/doc/$PKG/changelog.Debian.gz"
chmod 0644 "$ROOT/usr/share/doc/$PKG/changelog.Debian.gz"

cat > "$ROOT/DEBIAN/control" <<EOF
Package: $PKG
Version: $VERSION
Section: utils
Priority: optional
Architecture: $ARCH
Depends: python3 (>= 3.8), python3-gi, python3-gi-cairo, gir1.2-gtk-3.0
Recommends: gir1.2-ayatanaappindicator3-0.1
Maintainer: shhmft <lutcenko0710@gmail.com>
Installed-Size: $(du -ks "$ROOT/usr" | cut -f1)
Description: Учёт рабочих часов в системном лотке
 Небольшая утилита: считает отработанное время, показывает итоги по дням
 и за месяц, умеет полупрозрачный виджет поверх окон и выгрузку CSV
 за произвольный период.
 .
 Данные лежат в ~/.local/share/worktimer обычным текстом.
EOF

cat > "$ROOT/DEBIAN/postinst" <<'EOF'
#!/bin/sh
set -e
if [ "$1" = "configure" ]; then
    if command -v update-desktop-database >/dev/null 2>&1; then
        update-desktop-database -q /usr/share/applications || true
    fi
    if command -v gtk-update-icon-cache >/dev/null 2>&1; then
        gtk-update-icon-cache -q -f /usr/share/icons/hicolor || true
    fi
fi
exit 0
EOF
chmod 0755 "$ROOT/DEBIAN/postinst"
chmod 0755 "$ROOT/DEBIAN"

cat > "$ROOT/DEBIAN/postrm" <<'EOF'
#!/bin/sh
set -e
if command -v update-desktop-database >/dev/null 2>&1; then
    update-desktop-database -q /usr/share/applications || true
fi
exit 0
EOF
chmod 0755 "$ROOT/DEBIAN/postrm"

# права как положено в пакете
find "$ROOT/usr" -type d -exec chmod 0755 {} +

if command -v fakeroot >/dev/null 2>&1; then
    fakeroot dpkg-deb --build "$ROOT" "$OUT/${PKG}_${VERSION}_${ARCH}.deb" >/dev/null
else
    dpkg-deb --build "$ROOT" "$OUT/${PKG}_${VERSION}_${ARCH}.deb" >/dev/null
fi

cp "$OUT/${PKG}_${VERSION}_${ARCH}.deb" "$DEST/"
echo "готово: $DEST/${PKG}_${VERSION}_${ARCH}.deb"
ls -lh "$DEST/${PKG}_${VERSION}_${ARCH}.deb" | awk '{print $5, $9}'
