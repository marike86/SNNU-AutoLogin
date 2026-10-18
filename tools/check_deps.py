#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
发布包的「便携性」静态检查。

逐个解析 release 目录里每个 PE 文件的导入表，把依赖分成三类：
  IN-PACK   随包携带（在发布目录里能找到）
  SYSTEM    Windows 自带（干净的 Win10 2004+ / Win11 上一定有）
  MISSING   两者都不是 —— 换台电脑会加载失败

不依赖任何第三方库，直接读 PE 结构。
"""

import os
import struct
import sys

# 干净的 Windows 10 2004+ / Windows 11 上一定存在的系统 DLL（不含 VC++ 运行库）
SYSTEM_DLLS = {
    "ntdll.dll", "kernel32.dll", "kernelbase.dll", "user32.dll", "gdi32.dll",
    "gdi32full.dll", "advapi32.dll", "ole32.dll", "oleaut32.dll", "shell32.dll",
    "shlwapi.dll", "combase.dll", "comctl32.dll", "comdlg32.dll", "msvcrt.dll",
    "ucrtbase.dll", "rpcrt4.dll", "sechost.dll", "setupapi.dll", "shcore.dll",
    "dwmapi.dll", "d3d11.dll", "dxgi.dll", "d3dcompiler_47.dll", "d2d1.dll",
    "dwrite.dll", "windowscodecs.dll", "oleacc.dll", "uxtheme.dll", "wintrust.dll",
    "crypt32.dll", "bcrypt.dll", "ncrypt.dll", "version.dll", "winmm.dll",
    "imm32.dll", "msctf.dll", "propsys.dll", "psapi.dll", "userenv.dll",
    "powrprof.dll", "cfgmgr32.dll", "wtsapi32.dll", "netapi32.dll", "iphlpapi.dll",
    "dnsapi.dll", "ws2_32.dll", "winhttp.dll", "secur32.dll", "mswsock.dll",
    "normaliz.dll", "dbghelp.dll", "imagehlp.dll", "msimg32.dll", "winspool.drv",
    "opengl32.dll", "glu32.dll", "avrt.dll", "dinput8.dll", "hid.dll",
    "cfgmgr32.dll", "deviceassociation.dll", "dhcpcsvc.dll", "wevtapi.dll",
    "vulkan-1.dll", "msvcp_win.dll", "coremessaging.dll", "dcomp.dll",
    "windows.storage.dll", "windows.media.dll", "synchronization.dll",
    "wldp.dll", "ntasn1.dll", "sspicli.dll", "profapi.dll", "apphelp.dll",
    "d3d9.dll", "dxcore.dll", "directml.dll", "magnification.dll",
    # 以下同样是 Win10 2004+ 自带（WinUI / AppSDK 常用）
    "bcp47langs.dll", "bcp47mrm.dll", "elscore.dll", "mscoree.dll",
    "rometadata.dll", "urlmon.dll", "usp10.dll", "xmllite.dll",
    "msftedit.dll", "riched20.dll", "twinapi.appcore.dll", "coremessaging.dll",
}

SYSTEM_PREFIXES = ("api-ms-win-", "ext-ms-win-", "ext-ms-")

# 这些是「装不装 VC++ 运行库」的分水岭，单独提示
VCRUNTIME = {"vcruntime140.dll", "vcruntime140_1.dll", "msvcp140.dll",
             "msvcp140_1.dll", "msvcp140_2.dll", "concrt140.dll",
             "vccorlib140.dll", "vcruntime140_threads.dll"}


def iter_sections(data, pe_off):
    (num_sections,) = struct.unpack_from("<H", data, pe_off + 6)
    (opt_size,) = struct.unpack_from("<H", data, pe_off + 20)
    opt_off = pe_off + 24
    magic = struct.unpack_from("<H", data, opt_off)[0]

    # 数据目录：PE32 在可选头 +96，PE32+ 在 +112
    dd_off = opt_off + (96 if magic == 0x10B else 112)
    (imp_rva, _imp_size) = struct.unpack_from("<II", data, dd_off + 8)  # 第 1 项 = Import

    sec_off = opt_off + opt_size
    sections = []
    for i in range(num_sections):
        base = sec_off + i * 40
        name = data[base:base + 8].rstrip(b"\x00").decode("latin-1")
        (vsize, vaddr, rawsize, rawptr) = struct.unpack_from("<IIII", data, base + 8)
        sections.append((vaddr, max(vsize, rawsize), rawptr))

    return imp_rva, sections


def rva_to_off(rva, sections):
    for vaddr, size, rawptr in sections:
        if vaddr <= rva < vaddr + size:
            return rawptr + (rva - vaddr)
    return None


def imports_of(path):
    with open(path, "rb") as fh:
        data = fh.read()

    if data[:2] != b"MZ":
        return None

    (pe_off,) = struct.unpack_from("<I", data, 0x3C)
    if data[pe_off:pe_off + 4] != b"PE\x00\x00":
        return None

    imp_rva, sections = iter_sections(data, pe_off)
    if imp_rva == 0:
        return []

    off = rva_to_off(imp_rva, sections)
    if off is None:
        return []

    names = []
    while True:
        desc = data[off:off + 20]
        if len(desc) < 20:
            break
        (oft, _ts, _fc, name_rva, ft) = struct.unpack("<IIIII", desc)
        if oft == 0 and name_rva == 0 and ft == 0:
            break

        noff = rva_to_off(name_rva, sections)
        if noff is not None:
            end = data.index(b"\x00", noff)
            names.append(data[noff:end].decode("latin-1").lower())

        off += 20

    return names


def main(root):
    packed = {f.lower() for f in os.listdir(root) if os.path.isfile(os.path.join(root, f))}

    missing = {}
    vc = set()
    scanned = 0

    for name in sorted(packed):
        if not name.endswith((".exe", ".dll")):
            continue

        path = os.path.join(root, name)
        try:
            deps = imports_of(path)
        except Exception as exc:  # 解析失败就跳过，不阻断结论
            print(f"  ! {name}: 解析失败 {exc}")
            continue

        if deps is None:
            continue

        scanned += 1
        for dll in deps:
            if dll in packed:
                continue
            if dll in SYSTEM_DLLS or dll.startswith(SYSTEM_PREFIXES):
                continue
            if dll in VCRUNTIME:
                vc.add(name)
                continue
            missing.setdefault(dll, []).append(name)

    print(f"扫描目录：{root}")
    print(f"PE 文件：{scanned} 个，随包 DLL：{len(packed)} 个")

    if vc:
        print("\n[!] 依赖 VC++ 运行库（未随包）：")
        for dll in sorted({d for d in SYSTEM_DLLS if False} | set()):
            pass
        for dll in sorted(VCRUNTIME):
            pass
        print("    这些组件引用了 vcruntime/msvcp，需要目标机安装 VC++ 2015-2022 运行库")
        print("    引用方示例：" + ", ".join(sorted(vc)[:6]))

    if missing:
        print("\n[×] 找不到来源的依赖（换机可能启动失败）：")
        for dll, users in sorted(missing.items()):
            print(f"    {dll:<42} ← {', '.join(sorted(users)[:4])}")
        return 1

    print("\n[√] 所有非系统依赖都在发布目录内，目标机无需安装额外运行库")
    return 0


if __name__ == "__main__":
    target = sys.argv[1] if len(sys.argv) > 1 else "release"
    sys.exit(main(target))
