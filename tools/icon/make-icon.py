#!/usr/bin/env python3
"""Builds LiteSynapse's icon from the round logo (tools/icon/logo-original.png).

    python3 -I tools/icon/make-icon.py

- 128 and 256 px: the original artwork, cut out along its circle (the black square around it becomes transparent).
- 16..64 px: vector frames in the same style from small-icons.mjs (the original's thin rings and text vanish there).
Writes src/LiteSynapse/icon/icon-<size>.png (embedded in the app for its windows), src/LiteSynapse/app.ico (exe,
tray and installer icon) and docs/logo.png (the logo at 512 px with a transparent background).
Needs Pillow and node with tools/ui-preview's Playwright.
"""
import io
import os
import struct
import subprocess
import sys
import tempfile

from PIL import Image, ImageDraw

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, '..', '..'))
SOURCE = os.path.join(HERE, 'logo-original.png')
ICON_DIR = os.path.join(REPO, 'src', 'LiteSynapse', 'icon')
ICO = os.path.join(REPO, 'src', 'LiteSynapse', 'app.ico')
LOGO = os.path.join(REPO, 'docs', 'logo.png')
SMALL = [16, 20, 24, 32, 40, 48, 64]
LARGE = [128, 256]

# The disc in logo-original.png (measured): center and radius in pixels. The mask stays a little inside the edge
# so none of the black background bleeds into the rim.
CENTER = (513.5, 447.0)
RADIUS = 347.5
MASK_RADIUS = 346.0


def cut_disc():
    src = Image.open(SOURCE).convert('RGB')
    cx, cy = CENTER
    half = int(RADIUS) + 2
    box = (int(cx) - half, int(cy) - half, int(cx) + half, int(cy) + half)
    crop = src.crop(box)
    size = crop.size[0]
    # Anti-aliased circular mask, drawn at 4x and reduced.
    k = 4
    mask = Image.new('L', (size * k, size * k), 0)
    mx, my = (cx - box[0]) * k, (cy - box[1]) * k
    r = MASK_RADIUS * k
    ImageDraw.Draw(mask).ellipse((mx - r, my - r, mx + r, my + r), fill=255)
    mask = mask.resize((size, size), Image.LANCZOS)
    out = crop.convert('RGBA')
    out.putalpha(mask)
    # Trim to the disc so it fills the frame edge to edge.
    side = int(round(2 * MASK_RADIUS)) + 2
    left = int(round(cx - box[0] - side / 2))
    top = int(round(cy - box[1] - side / 2))
    return out.crop((left, top, left + side, top + side))


def png_bytes(im):
    b = io.BytesIO()
    im.save(b, format='PNG', optimize=True)
    return b.getvalue()


def write_ico(path, frames):
    # ICONDIR + one ICONDIRENTRY per frame, PNG-compressed images (Windows Vista and later).
    count = len(frames)
    offset = 6 + 16 * count
    head = struct.pack('<HHH', 0, 1, count)
    entries, blobs = b'', b''
    for size, data in frames:
        dim = 0 if size >= 256 else size
        entries += struct.pack('<BBBBHHII', dim, dim, 0, 0, 1, 32, len(data), offset)
        offset += len(data)
        blobs += data
    with open(path, 'wb') as f:
        f.write(head + entries + blobs)


def main():
    os.makedirs(ICON_DIR, exist_ok=True)
    with tempfile.TemporaryDirectory() as tmp:
        subprocess.run(['node', os.path.join(HERE, 'small-icons.mjs'), tmp], check=True)
        small = {s: Image.open(os.path.join(tmp, f'icon-{s}.png')).convert('RGBA') for s in SMALL}
        for im in small.values():
            im.load()
    disc = cut_disc()
    frames = []
    for s in SMALL + LARGE:
        im = small[s] if s in small else disc.resize((s, s), Image.LANCZOS)
        data = png_bytes(im)
        with open(os.path.join(ICON_DIR, f'icon-{s}.png'), 'wb') as f:
            f.write(data)
        frames.append((s, data))
    write_ico(ICO, frames)
    disc.resize((512, 512), Image.LANCZOS).save(LOGO, format='PNG', optimize=True)
    print(f'{len(frames)} frames -> {ICO}')
    print(f'pngs -> {ICON_DIR}')
    print(f'logo -> {LOGO}')


if __name__ == '__main__':
    sys.exit(main())
