#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Pure-python TrueType advance-width reader (no PIL / no GUI).

Parses the 'head', 'hhea', 'maxp', 'hmtx' and 'cmap' tables of a font and
returns the advance width of a string in *pixels* at a given point size and
96 DPI. Used to quantify whether Clothesline's fixed-width (296px) tray
header strip can hold the localised strings without overlapping the count
badge, and whether the longest menu labels push the menu wide.

Limitations: variable fonts are measured at their default instance; the
'Bold' measurements use Segoe UI Bold as an approximation of the bold axis.
CJK glyph advances come from the same font's cmap/hmtx.
"""
import struct
import sys

DEFAULT_FONT = r"C:\Windows\Fonts\SegUIVar.ttf"
BOLD_FONT = r"C:\Windows\Fonts\segoeuib.ttf"


def read_tables(data):
    num = struct.unpack(">H", data[4:6])[0]
    tables, off = {}, 12
    for _ in range(num):
        tag = data[off:off + 4].decode("latin1")
        _, offset, length = struct.unpack(">III", data[off + 4:off + 16])
        tables[tag] = (offset, length)
        off += 16
    return tables


def load(path):
    with open(path, "rb") as fh:
        data = fh.read()
    t = read_tables(data)
    upem = struct.unpack(">H", data[t["head"][0] + 18: t["head"][0] + 20])[0]
    num_h = struct.unpack(">H", data[t["hhea"][0] + 34: t["hhea"][0] + 36])[0]
    num_glyphs = struct.unpack(">H", data[t["maxp"][0] + 4: t["maxp"][0] + 6])[0]

    # hmtx advance widths
    ho = t["hmtx"][0]
    adv = [struct.unpack(">H", data[ho + i * 4: ho + i * 4 + 2])[0] for i in range(num_h)]
    if adv:
        adv += [adv[-1]] * (num_glyphs - num_h)
    else:
        adv = [0] * num_glyphs

    # cmap -> pick best subtable
    co = t["cmap"][0]
    n = struct.unpack(">H", data[co + 2: co + 4])[0]
    best = None
    for i in range(n):
        pid, eid, suboff = struct.unpack(">HHI", data[co + 4 + i * 8: co + 4 + i * 8 + 8])
        sub = co + suboff
        fmt = struct.unpack(">H", data[sub: sub + 2])[0]
        score = 0
        if fmt == 12 and pid == 3 and eid == 10:
            score = 3
        elif fmt == 4 and pid == 3 and eid == 1:
            score = 2
        elif fmt == 4:
            score = 1
        if best is None or score > best[0]:
            best = (score, sub, fmt)
    _, sub, fmt = best
    cmap = {}
    if fmt == 4:
        segx2 = struct.unpack(">H", data[sub + 6: sub + 8])[0]
        seg = segx2 // 2
        endo = sub + 14
        starto = endo + segx2 + 2
        deltao = starto + segx2
        rangeo = deltao + segx2
        for i in range(seg):
            end = struct.unpack(">H", data[endo + i * 2: endo + i * 2 + 2])[0]
            start = struct.unpack(">H", data[starto + i * 2: starto + i * 2 + 2])[0]
            delta = struct.unpack(">h", data[deltao + i * 2: deltao + i * 2 + 2])[0]
            ro = struct.unpack(">H", data[rangeo + i * 2: rangeo + i * 2 + 2])[0]
            for c in range(start, min(end, 0xFFFF) + 1):
                if ro == 0:
                    cmap[c] = (c + delta) & 0xFFFF
                else:
                    gio = rangeo + i * 2 + ro + (c - start) * 2
                    g = struct.unpack(">H", data[gio: gio + 2])[0]
                    cmap[c] = (g + delta) & 0xFFFF if g else 0
    elif fmt == 12:
        ngroups = struct.unpack(">I", data[sub + 12: sub + 16])[0]
        go = sub + 16
        for i in range(ngroups):
            sc, ec, sg = struct.unpack(">III", data[go + i * 12: go + i * 12 + 12])
            for c in range(sc, ec + 1):
                cmap[c] = sg + (c - sc)
    return {"upem": upem, "adv": adv, "cmap": cmap}


def width_px(font, text, pt, dpi=96.0):
    upem, adv, cmap = font["upem"], font["adv"], font["cmap"]
    total = 0
    for ch in text:
        g = cmap.get(ord(ch), 0)
        if g < len(adv):
            total += adv[g]
    return total * (pt * dpi / 72.0) / upem


def main():
    reg = load(DEFAULT_FONT)
    bold = load(BOLD_FONT)
    print(f"font unitsPerEm(regular)={reg['upem']}  glyphs={len(reg['adv'])}")
    print()

    W = 296
    subX = 59
    prefix = "v0.1.9 \u00b7 "

    locales = {
        "en": ("Watching the clipboard", "Clipboard watch is off", "8 on the line"),
        "es": ("Vigilando el portapapeles", "Vigilancia desactivada", "8 en la l\u00ednea"),
        "zh": ("\u6b63\u5728\u76d1\u89c6\u526a\u8d34\u677f", "\u526a\u8d34\u677f\u76d1\u89c6\u5df2\u5173", "\u7ebf\u4e0a 8 \u5f20"),
        "ja": ("\u30af\u30ea\u30c3\u30d7\u30dc\u30fc\u30c9\u3092\u76e3\u8996\u4e2d", "\u76e3\u8996\u306f\u30aa\u30d5\u3067\u3059", "\u30e9\u30a4\u30f3\u4e0a 8 \u679a"),
        "fr": ("Surveillance du presse-papiers", "Surveillance d\u00e9sactiv\u00e9e", "8 sur la corde"),
    }

    print("== TRAY HEADER STRIP (fixed 296px): subtitle vs right-aligned badge ==")
    hdr = f"{'loc':<4}{'state':<10}{'subW':>7}{'subEnd':>8}{'badgeW':>8}{'badgeX':>8}   verdict"
    print(hdr)
    for loc, (watch, off, badge) in locales.items():
        for state, s in (("watching", watch), ("off", off)):
            text = prefix + s
            sw = width_px(reg, text, 8.0)
            send = subX + sw
            bw = width_px(bold, badge, 8.5) + 18
            bx = W - bw - 14
            verdict = f"OVERLAP {send - bx:.1f}px" if send > bx else "ok"
            print(f"{loc:<4}{state:<10}{sw:>7.1f}{send:>8.1f}{bw:>8.1f}{bx:>8.1f}   {verdict}")

    print()
    print("== EmptyHint (thumbnail strip, x=14, no bound, strip width 296) ==")
    empt = {
        "en": "Press Win+Shift+S to take a screenshot",
        "es": "Pulsa Win+Shift+S para hacer una captura",
        "zh": "\u6309 Win+Shift+S \u622a\u56fe",
        "ja": "Win+Shift+S \u3067\u30b9\u30af\u30ea\u30fc\u30f3\u30b7\u30e7\u30c3\u30c8",
        "fr": "Appuyez sur Win+Shift+S pour capturer",
    }
    for loc, s in empt.items():
        w = width_px(reg, s, 8.0)
        end = 14 + w
        v = f"OVERFLOW {end - 296:.1f}px" if end > 296 else "ok"
        print(f"{loc:<4}{w:>7.1f} end={end:>7.1f}   {v}")

    print()
    print("== Longest MENU row labels @10pt (WinForms auto-sizes; baseline zh menu=357px) ==")
    rows = [
        ("en", "When the pointer rests at the top edge"),
        ("en", "Take down everything to the right"),
        ("fr", "Quand le pointeur se pose au bord sup\u00e9rieur"),
        ("fr", "Descendre tout ce qui est \u00e0 droite"),
        ("fr", "Descendre tout ce qui est \u00e0 gauche"),
        ("fr", "Valeur syst\u00e8me par d\u00e9faut"),
        ("es", "Cuando el puntero reposa en el borde superior"),
        ("es", "Quitar todo lo de la izquierda"),
        ("ja", "\u30dd\u30a4\u30f3\u30bf\u304c\u4e0a\u7aef\u306b\u7559\u307e\u3063\u305f\u6642"),
        ("zh", "\u5f53\u6307\u9488\u505c\u5728\u5c4f\u5e55\u9876\u90e8"),
    ]
    for loc, s in rows:
        w = width_px(reg, s, 10.0)
        print(f"{loc:<4}{w:>7.1f}px  {s}")


if __name__ == "__main__":
    main()
