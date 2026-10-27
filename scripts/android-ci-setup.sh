#!/usr/bin/env bash
set -euo pipefail
# Use the current CLI: older sdkmanager releases do not consistently handle API 37.0 packages.
sdk_root="${RUNNER_TEMP:?}/snnu-android-sdk"
tools_unpack="${RUNNER_TEMP}/snnu-cmdline-tools"
mkdir -p "$sdk_root/cmdline-tools" "$tools_unpack"
curl --fail --location --retry 3 --silent --show-error \
  https://dl.google.com/android/repository/commandlinetools-linux-16111833_latest.zip \
  --output "$tools_unpack/tools.zip"
printf '%s  %s\n' e025545c62a8e64c7559119566a569fb1dec5f60 "$tools_unpack/tools.zip" | sha1sum --check
unzip -q "$tools_unpack/tools.zip" -d "$tools_unpack"
mv "$tools_unpack/cmdline-tools" "$sdk_root/cmdline-tools/latest"
android_cli="$sdk_root/cmdline-tools/latest/bin/android"
for package in platforms/android-37.0 build-tools/36.0.0 platform-tools; do
  "$android_cli" --no-metrics --sdk "$sdk_root" sdk install "$package"
done
printf 'ANDROID_HOME=%s\nANDROID_SDK_ROOT=%s\n' "$sdk_root" "$sdk_root" >> "$GITHUB_ENV"
printf '%s\n%s\n' "$sdk_root/cmdline-tools/latest/bin" "$sdk_root/platform-tools" >> "$GITHUB_PATH"
