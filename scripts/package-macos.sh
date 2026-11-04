#!/bin/bash
# Builds an Apple Silicon Zenote.app and a zip of it in build/.
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

ditto -c -k --keepParent "$app" build/Zenote-macos-arm64.zip
echo "Built $app"
