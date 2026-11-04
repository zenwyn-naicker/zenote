#!/bin/bash
# Builds an Apple Silicon Zenote.app and a Zenote.dmg installer in build/.
set -euo pipefail

cd "$(dirname "$0")/.."

app="build/Zenote.app"
rm -rf build
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources"

dotnet publish src/Zenote.App -c Release -r osx-arm64 --self-contained \
  -p:PublishReadyToRun=true -o "$app/Contents/MacOS"

cp assets/Info.plist "$app/Contents/Info.plist"
cp assets/Zenote.icns "$app/Contents/Resources/Zenote.icns"

# Ad-hoc signing lets the bundled native libraries load on Apple Silicon.
codesign --force --deep --sign - "$app"

# The disk image shows the app beside an Applications shortcut, so installing is one drag.
stage="build/dmg"
mkdir -p "$stage"
cp -R "$app" "$stage/"
ln -s /Applications "$stage/Applications"
hdiutil create -quiet -volname "Zenote" -srcfolder "$stage" -ov -format UDZO build/Zenote.dmg
rm -rf "$stage"

echo "Built $app and build/Zenote.dmg"
