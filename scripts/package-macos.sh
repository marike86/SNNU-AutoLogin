#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")/.."
export MACOSX_DEPLOYMENT_TARGET=13.0
target="${1:-$(rustc -vV | sed -n 's/^host: //p')}"
case "$target" in
  aarch64-apple-darwin) arch=arm64 ;;
  x86_64-apple-darwin) arch=x64 ;;
  *) echo "Unsupported macOS target: $target" >&2; exit 1 ;;
esac
cargo fmt --all -- --check
cargo test --release --locked --target "$target"
cargo build --release --locked --target "$target"
version="$(python3 -c 'import json,subprocess; print(json.loads(subprocess.check_output(["cargo","metadata","--no-deps","--format-version","1","--locked"]))["packages"][0]["version"])')"
stage="$PWD/target/macos-package-$arch"
app="$stage/校园网自动登录.app"
mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources" dist
cp "target/$target/release/snnulogin" "$app/Contents/MacOS/snnulogin"
cp LICENSE "$stage/LICENSE"
cp docs/macos.md "$stage/README.md"
python3 scripts/macos-package.py "$target" "$app" "$version" "$stage"
# Convert the existing project icon using macOS ImageIO; no additional imaging dependency.
iconset="$stage/AppIcon.iconset"
mkdir -p "$iconset"
sips -s format png icon.ico --out "$stage/icon.png" >/dev/null
for size in 16 32 128 256 512; do
  sips -z "$size" "$size" "$stage/icon.png" --out "$iconset/icon_${size}x${size}.png" >/dev/null
  twice=$((size * 2))
  sips -z "$twice" "$twice" "$stage/icon.png" --out "$iconset/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$iconset" -o "$app/Contents/Resources/AppIcon.icns"
codesign --force --sign - --timestamp=none "$app"
codesign --verify --strict --verbose=2 "$app"
mkdir -p "dist/macos-$arch"
ditto "$app" "dist/macos-$arch/校园网自动登录.app"
cp "$stage/README.md" "$stage/LICENSE" "$stage/THIRD-PARTY-NOTICES.txt" "dist/macos-$arch/"
zip="SNNU-AutoLogin-v$version-macos-$arch.zip"
ditto -c -k --keepParent "dist/macos-$arch" "dist/$zip"
(cd dist && shasum -a 256 "$zip") > "dist/SHA256SUMS-macos-$arch.txt"
printf 'Packaged: dist/%s\n' "$zip"
