#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Build a multi-resolution .ico for clipboard-tool.
Source: D:/download/生成液态玻璃风格icon.png (2048x2048, RGB)
Output: clipboard-tool/clipboard-exe/Assets/app.ico

Sizes: 16/24/32/48/64/128/256 — modern Windows picks the best match for
taskbar / Start / tray / Alt-Tab. 256 uses PNG-in-ICO encoding (Vista+).
"""
from PIL import Image
from pathlib import Path

SRC = Path(r"D:\download\生成液态玻璃风格icon.png")
DST = Path(r"D:\workbuddy\2026-09-04-13-26-37\clipboard-tool\clipboard-exe\Assets\app.ico")
SIZES = [16, 24, 32, 48, 64, 128, 256]

def main():
    img = Image.open(SRC)
    assert img.size == (2048, 2048), f"unexpected src size {img.size}"
    assert img.mode == "RGB", f"unexpected src mode {img.mode}"
    DST.parent.mkdir(parents=True, exist_ok=True)

    # Build base images. We do plain high-quality downscaling (LANCZOS).
    # ICO format auto-includes RGBA; we'll keep RGB here (no alpha channel in
    # source) — Windows treats RGB as fully opaque on icon surfaces.
    layers = [img.resize((s, s), Image.LANCZOS) for s in SIZES]

    # Pillow's ICO writer: pass `sizes=[(w,h)...]` to force which sizes to
    # include, and `append_images=[...]` to provide the larger frames. The
    # primary image is the smallest; Pillow reads `sizes` to drive which
    # entry in append_images corresponds to which requested size.
    # Use the 256x256 frame as primary and append the smaller ones.
    DST.unlink(missing_ok=True)
    largest = layers[-1]               # 256
    smaller = layers[:-1]              # 16/24/32/48/64/128
    largest.save(
        DST,
        format="ICO",
        sizes=[(s, s) for s in SIZES],
        append_images=smaller,
    )
    print(f"wrote {DST} ({DST.stat().st_size} bytes), sizes={SIZES}")

    # Sanity: reopen and dump directory
    chk = Image.open(DST)
    sizes_seen = chk.ico.sizes() if hasattr(chk, "ico") else set()
    print(f"  reopened: size={chk.size}, format={chk.format}, sizes={sorted(sizes_seen)}")

if __name__ == "__main__":
    main()