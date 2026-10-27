"""Generate bundle metadata and include licenses of the target's actual dependencies."""
import json
import pathlib
import plistlib
import subprocess
import sys

target, app_name, version, stage_name = sys.argv[1:]
app, stage = pathlib.Path(app_name), pathlib.Path(stage_name)
reported_version = subprocess.check_output(
    [str(app / "Contents/MacOS/snnulogin"), "--version"], text=True
).strip()
if reported_version != f"SNNU-AutoLogin v{version}":
    raise RuntimeError("macOS application and package versions do not match")
info = {
    "CFBundleIdentifier": "io.github.shixiang-liu.SNNU-AutoLogin",
    "CFBundleName": "SNNU-AutoLogin",
    "CFBundleDisplayName": "校园网自动登录",
    "CFBundleExecutable": "snnulogin",
    "CFBundlePackageType": "APPL",
    "CFBundleShortVersionString": version,
    "CFBundleVersion": version,
    "CFBundleIconFile": "AppIcon",
    "LSMinimumSystemVersion": "13.0",
    "LSUIElement": True,
    "NSHighResolutionCapable": True,
    "NSSupportsAutomaticTermination": False,
    "NSLocalNetworkUsageDescription": "连接学校校园网认证服务器，以登录、检测连接和掉线重连。",
    "NSLocationUsageDescription": "仅用于显示当前 Wi-Fi 名称；不授权也能使用校园网登录。",
    "NSLocationWhenInUseUsageDescription": "仅用于显示当前 Wi-Fi 名称；不授权也能使用校园网登录。",
}
with (app / "Contents/Info.plist").open("wb") as file:
    plistlib.dump(info, file)
metadata = json.loads(subprocess.check_output([
    "cargo", "metadata", "--format-version", "1", "--locked", "--filter-platform", target
]))
nodes = {node["id"]: node for node in metadata["resolve"]["nodes"]}
visited = set()

def visit(package_id):
    if package_id in visited:
        return
    visited.add(package_id)
    for dependency in nodes[package_id]["dependencies"]:
        visit(dependency)

visit(metadata["resolve"]["root"])
report = [f"Third-party notices for SNNU-AutoLogin ({target})\n"]
for package in sorted(metadata["packages"], key=lambda item: item["name"]):
    if package["id"] not in visited or package["id"] == metadata["resolve"]["root"]:
        continue
    report.append(f"\n{'=' * 60}\n{package['name']} {package['version']} | {package.get('license')}\n")
    if package.get("repository"):
        report.append(package["repository"] + "\n")
    folder = pathlib.Path(package["manifest_path"]).parent
    licenses = [file for file in folder.iterdir() if file.is_file() and
                file.name.lower().startswith(("license", "licence", "copying", "copyright", "notice"))]
    if not licenses and package.get("repository", "").rstrip("/") == "https://github.com/madsmtm/objc2":
        # Upstream's crates omit their workspace-root license files. These exact
        # version copies are vendored from each crate's .cargo_vcs_info commit.
        license_file = pathlib.Path("scripts/licenses") / f"{package['name']}-{package['version']}-LICENSE.md"
        if license_file.is_file():
            licenses = [license_file, pathlib.Path("scripts/licenses/objc2-MIT.txt")]
            vcs = json.loads((folder / ".cargo_vcs_info.json").read_text())
            report.append(f"License declaration: https://github.com/madsmtm/objc2/blob/{vcs['git']['sha1']}/LICENSE.md\n")
    if not licenses:
        raise RuntimeError(f"Missing license text: {package['name']}")
    for file in sorted(licenses):
        report.append(f"\n{file.name}\n{file.read_text(encoding='utf-8')}\n")
(stage / "THIRD-PARTY-NOTICES.txt").write_text("".join(report), encoding="utf-8")
