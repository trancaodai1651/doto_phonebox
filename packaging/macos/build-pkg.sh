#!/bin/bash
set -euo pipefail

version=$1
here="$(cd "$(dirname "$0")" && pwd)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
app="$work/PhoneBox.app"
resources="$app/Contents/Resources"

mkdir -p "$app/Contents/MacOS" "$resources" dist
launcher="$app/Contents/MacOS/PhoneBox"
xcrun clang -arch arm64 -arch x86_64 -mmacosx-version-min=14.0 -Wall -Wextra -Werror \
    "$here/launcher.c" -o "$launcher"
lipo "$launcher" -verify_arch arm64 x86_64
for rid in osx-arm64 osx-x64; do
    ditto "payload/$rid" "$resources/$rid"
    rm -f "$resources/$rid/PhoneBoxAgent.pdb"
done
lipo "$resources/osx-arm64/PhoneBoxAgent" -verify_arch arm64
lipo "$resources/osx-x64/PhoneBoxAgent" -verify_arch x86_64

cat > "$app/Contents/Info.plist" <<EOF
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "https://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0">
<dict>
    <key>CFBundleIdentifier</key><string>net.oxog.phonebox.desktop</string>
    <key>CFBundleName</key><string>PhoneBox</string>
    <key>CFBundleExecutable</key><string>PhoneBox</string>
    <key>CFBundlePackageType</key><string>APPL</string>
    <key>CFBundleShortVersionString</key><string>$version</string>
    <key>CFBundleVersion</key><string>$version</string>
    <key>LSMinimumSystemVersion</key><string>14.0</string>
    <key>LSUIElement</key><true/>
</dict>
</plist>
EOF
plutil -lint "$app/Contents/Info.plist"
codesign --force --sign - "$app"
codesign --verify --deep --strict "$app"
pkgbuild --component "$app" --install-location /Applications \
    --identifier net.oxog.phonebox.desktop --version "$version" "$work/PhoneBox-component.pkg"

cat > "$work/distribution.xml" <<EOF
<?xml version="1.0" encoding="utf-8"?>
<installer-gui-script minSpecVersion="2">
    <title>PhoneBox</title>
    <options customize="never" require-scripts="false" hostArchitectures="arm64,x86_64"/>
    <volume-check><allowed-os-versions><os-version min="14.0"/></allowed-os-versions></volume-check>
    <choices-outline><line choice="phonebox"/></choices-outline>
    <choice id="phonebox" visible="false"><pkg-ref id="net.oxog.phonebox.desktop"/></choice>
    <pkg-ref id="net.oxog.phonebox.desktop" version="$version">PhoneBox-component.pkg</pkg-ref>
</installer-gui-script>
EOF
productbuild --distribution "$work/distribution.xml" --package-path "$work" \
    "dist/PhoneBox-$version-macos-universal.pkg"
