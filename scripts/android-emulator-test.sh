#!/usr/bin/env bash
set -euo pipefail
project_root="$(cd "$(dirname "$0")/.." && pwd)"
api="${1:?API level required}"
if [[ "$api" == 37 ]]; then api=37.0; fi
case "$api" in 26|31|33|34|35|36|37.0) ;; *) echo "Unsupported test API: $api" >&2; exit 1 ;; esac
android_cli="$ANDROID_HOME/cmdline-tools/latest/bin/android"
"$android_cli" --no-metrics --sdk "$ANDROID_HOME" sdk install emulator
"$android_cli" --no-metrics --sdk "$ANDROID_HOME" sdk install "system-images/android-$api/default/x86_64"
printf 'no\n' | "$ANDROID_HOME/cmdline-tools/latest/bin/avdmanager" create avd \
  --name snnu-ci --package "system-images;android-$api;default;x86_64" --device pixel --force
output="$project_root/android/app/build/device"
mkdir -p "$output"
"$ANDROID_HOME/emulator/emulator" -avd snnu-ci -port 5554 -no-window -no-audio \
  -no-boot-anim -no-snapshot -gpu swiftshader_indirect -cores 2 -memory 2048 > "$output/emulator.log" 2>&1 &
emulator_pid=$!
trap 'adb -s emulator-5554 emu kill >/dev/null 2>&1 || true; wait "$emulator_pid" || true' EXIT
booted=false
for ((attempt=0; attempt<150; attempt++)); do
  if [[ "$(adb -s emulator-5554 shell getprop sys.boot_completed 2>/dev/null | tr -d '\r')" == 1 ]]; then
    booted=true
    break
  fi
  sleep 2
done
if [[ "$booted" != true ]]; then tail -80 "$output/emulator.log"; echo 'Emulator boot timeout' >&2; exit 1; fi
adb -s emulator-5554 shell input keyevent KEYCODE_WAKEUP
adb -s emulator-5554 shell wm dismiss-keyguard
test_status=0
ANDROID_SERIAL=emulator-5554 bash "$project_root/android/gradlew" -p "$project_root/android" connectedDebugAndroidTest assembleBenchmark --no-daemon || test_status=$?
# The Gradle runner uninstalls its debug app (including external files) after tests.
# Capture a clean launch of the actual optimized APK as a separate installation check.
if [[ "$test_status" == 0 ]]; then
  adb -s emulator-5554 install -r "$project_root/android/app/build/outputs/apk/benchmark/app-benchmark.apk"
  adb -s emulator-5554 shell am start -n io.github.shixiangliu.snnulogin.benchmark/io.github.shixiangliu.snnulogin.MainActivity
  sleep 2
  adb -s emulator-5554 shell pidof io.github.shixiangliu.snnulogin.benchmark
  adb -s emulator-5554 shell screencap -p /sdcard/snnu-optimized.png
  adb -s emulator-5554 pull /sdcard/snnu-optimized.png "$output/home-optimized.png"
fi
adb -s emulator-5554 logcat -d -b crash > "$output/crash.log" || true
exit "$test_status"
