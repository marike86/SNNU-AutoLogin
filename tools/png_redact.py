"""最小依赖 PNG 读写 + 打码（仅用标准库：zlib / struct）。

用途：给 README 截图里出现的真实学号与出口 IP 做马赛克处理，避免上传 GitHub 泄露个人信息。
--probe 模式只打印墨迹包围盒，方便校准坐标。
"""
from __future__ import annotations

import struct
import sys
import zlib


def read_png(path: str):
    with open(path, "rb") as f:
        data = f.read()
    assert data[:8] == b"\x89PNG\r\n\x1a\n", "不是 PNG"
    pos, idat, w = 8, bytearray(), 0
    h = bitdepth = colortype = 0
    while pos < len(data):
        (length,) = struct.unpack(">I", data[pos:pos + 4])
        ctype = data[pos + 4:pos + 8]
        chunk = data[pos + 8:pos + 8 + length]
        pos += 12 + length
        if ctype == b"IHDR":
            w, h, bitdepth, colortype, _comp, _filt, interlace = struct.unpack(">IIBBBBB", chunk)
            assert bitdepth == 8 and interlace == 0, f"暂不支持 bitdepth={bitdepth} interlace={interlace}"
        elif ctype == b"IDAT":
            idat += chunk
        elif ctype == b"IEND":
            break
    channels = {0: 1, 2: 3, 3: 1, 4: 2, 6: 4}[colortype]
    raw = zlib.decompress(bytes(idat))
    stride = w * channels
    rows, prev = [], bytearray(stride)
    p = 0
    for _ in range(h):
        ft = raw[p]
        p += 1
        line = bytearray(raw[p:p + stride])
        p += stride
        if ft == 0:
            pass
        elif ft == 1:
            for i in range(channels, stride):
                line[i] = (line[i] + line[i - channels]) & 0xFF
        elif ft == 2:
            for i in range(stride):
                line[i] = (line[i] + prev[i]) & 0xFF
        elif ft == 3:
            for i in range(stride):
                a = line[i - channels] if i >= channels else 0
                line[i] = (line[i] + ((a + prev[i]) >> 1)) & 0xFF
        elif ft == 4:
            for i in range(stride):
                a = line[i - channels] if i >= channels else 0
                c = prev[i - channels] if i >= channels else 0
                b = prev[i]
                pa, pb, pc = abs(b - c), abs(a - c), abs(a + b - 2 * c)
                pr = a if (pa <= pb and pa <= pc) else (b if pb <= pc else c)
                line[i] = (line[i] + pr) & 0xFF
        else:
            raise ValueError(f"未知滤波类型 {ft}")
        rows.append(line)
        prev = line
    return w, h, channels, rows


def write_png(path: str, w: int, h: int, channels: int, rows):
    out = bytearray()
    for line in rows:
        out.append(0)
        out += line

    def chunk(tag: bytes, payload: bytes) -> bytes:
        return (struct.pack(">I", len(payload)) + tag + payload
                + struct.pack(">I", zlib.crc32(tag + payload) & 0xFFFFFFFF))

    colortype = {1: 0, 3: 2, 4: 6}[channels]
    png = b"\x89PNG\r\n\x1a\n"
    png += chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, colortype, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress(bytes(out), 9))
    png += chunk(b"IEND", b"")
    with open(path, "wb") as f:
        f.write(png)


def luma(line, x, channels):
    o = x * channels
    if channels >= 3:
        r, g, b = line[o], line[o + 1], line[o + 2]
        return (r * 299 + g * 587 + b * 114) // 1000
    return line[o]


def probe(w, h, channels, rows, x0, x1, y0, y1, thresh=110, gap=6):
    """打印区域内深色像素的列区间（用于定位文字实际占位）。"""
    runs, cur = [], None
    for x in range(x0, min(x1, w)):
        dark = any(luma(rows[y], x, channels) < thresh for y in range(y0, min(y1, h)))
        if dark and cur is None:
            cur = x
        elif not dark and cur is not None:
            if x - cur >= 1:
                runs.append((cur, x - 1))
            cur = None
    if cur is not None:
        runs.append((cur, min(x1, w) - 1))
    merged = []
    for a, b in runs:
        if merged and a - merged[-1][1] <= gap:
            merged[-1] = (merged[-1][0], b)
        else:
            merged.append((a, b))
    for a, b in merged:
        ys = [y for y in range(y0, min(y1, h))
              if any(luma(rows[y], x, channels) < thresh for x in range(a, b + 1))]
        print(f"  x={a}..{b}  y={min(ys)}..{max(ys)}  w={b - a + 1} h={max(ys) - min(ys) + 1}")


def mosaic(rows, channels, x0, y0, x1, y1, block=5):
    """按 block 像素做块平均，形成马赛克。"""
    for by in range(y0, y1, block):
        for bx in range(x0, x1, block):
            ex, ey = min(bx + block, x1), min(by + block, y1)
            n = (ex - bx) * (ey - by)
            sums = [0] * channels
            for y in range(by, ey):
                line = rows[y]
                for x in range(bx, ex):
                    o = x * channels
                    for c in range(channels):
                        sums[c] += line[o + c]
            avg = bytes([s // n for s in sums])
            for y in range(by, ey):
                line = rows[y]
                for x in range(bx, ex):
                    o = x * channels
                    line[o:o + channels] = avg


def boxblur(rows, channels, x0, y0, x1, y1, radius=4):
    """方框模糊：让马赛克块的硬边变柔和，看起来像刻意的隐私模糊而不是糊了一块。"""
    src = [bytes(rows[y][x0 * channels:x1 * channels]) for y in range(y0, y1)]
    width = x1 - x0
    for y in range(y0, y1):
        out = bytearray(width * channels)
        for x in range(width):
            for c in range(channels):
                total = count = 0
                for dy in range(-radius, radius + 1):
                    yy = y - y0 + dy
                    if 0 <= yy < len(src):
                        for dx in range(-radius, radius + 1):
                            xx = x + dx
                            if 0 <= xx < width:
                                total += src[yy][xx * channels + c]
                                count += 1
                out[x * channels + c] = total // count
        rows[y][x0 * channels:x1 * channels] = out


if __name__ == "__main__":
    mode = sys.argv[1]
    src = sys.argv[2]
    w, h, channels, rows = read_png(src)

    if mode == "probe":
        print(f"{src}  {w}x{h}  channels={channels}")
        for spec in sys.argv[3:]:
            x0, y0, x1, y1 = (int(v) for v in spec.split(","))
            print(f"区域 {spec}:")
            probe(w, h, channels, rows, x0, x1, y0, y1)
    else:
        dst = sys.argv[3]
        # 每个参数：x0,y0,x1,y1[,block]
        for spec in sys.argv[4:]:
            parts = [int(v) for v in spec.split(",")]
            block = parts[4] if len(parts) > 4 else 5
            mosaic(rows, channels, *parts[:4], block=block)
            if mode == "smudge":
                boxblur(rows, channels, *parts[:4], radius=4)
            print(f"{'模糊' if mode == 'smudge' else '打码'} {parts[:4]}")
        write_png(dst, w, h, channels, rows)
        print(f"写出 {dst}")
