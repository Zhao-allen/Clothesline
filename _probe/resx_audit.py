#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Resx audit for Clothesline i18n.
Outputs:
  1. Key-set diff across the five resx files
  2. Placeholder ({0}/{1}) diff table for parameterised keys
  3. Untranslated-suspect table (value == English)
  4. Key length ranking (longest first) with per-locale length
Run:  python _probe/resx_audit.py
"""
import os
import re
import sys
import xml.etree.ElementTree as ET

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
RES = os.path.join(ROOT, "Resources")

FILES = {
    "en": "Strings.resx",
    "es": "Strings.es.resx",
    "zh": "Strings.zh.resx",
    "ja": "Strings.ja.resx",
    "fr": "Strings.fr.resx",
}
ORDER = ["en", "es", "zh", "ja", "fr"]

# Proper nouns / shortcuts / pure-symbol values that legitimately match English.
WHITELIST_EXACT = {
    "AppName",            # product name
    "SectionCapture",     # "CAPTURE" is used verbatim in several locales
    "SectionApp",         # "APP"
}


def load(path):
    tree = ET.parse(path)
    root = tree.getroot()
    out = {}
    for d in root.findall("data"):
        name = d.get("name")
        v = d.find("value")
        out[name] = (v.text if v is not None and v.text is not None else "")
    return out


def placeholders(value):
    """Return list of ints found in {n} slots, in order of appearance."""
    return [int(m) for m in re.findall(r"\{(\d+)\}", value)]


def main():
    data = {loc: load(os.path.join(RES, fn)) for loc, fn in FILES.items()}
    en = data["en"]
    en_keys = set(en.keys())

    print("=" * 78)
    print("SECTION 1  KEY-SET DIFF  (baseline = English neutral Strings.resx)")
    print("=" * 78)
    print(f"English key count: {len(en_keys)}")
    problems = 0
    for loc in ORDER:
        if loc == "en":
            continue
        keys = set(data[loc].keys())
        missing = sorted(en_keys - keys)
        extra = sorted(keys - en_keys)
        status = "OK" if not missing and not extra else "*** MISMATCH ***"
        if missing or extra:
            problems += 1
        print(f"\n[{loc}]  keys={len(keys)}  {status}")
        if missing:
            print(f"   MISSING (in en, not in {loc}): {missing}")
        if extra:
            print(f"   EXTRA   (in {loc}, not in en): {extra}")
    if problems == 0:
        print("\n>> All five locales have identical key sets.")

    print()
    print("=" * 78)
    print("SECTION 2  PLACEHOLDER DIFF  (parameterised keys only)")
    print("=" * 78)
    param_keys = sorted(k for k, v in en.items() if placeholders(v))
    # also include any key that has placeholders in ANY locale
    for loc in ORDER:
        for k, v in data[loc].items():
            if placeholders(v) and k not in param_keys:
                param_keys.append(k)
    param_keys = sorted(set(param_keys))
    print(f"Parameterised keys: {param_keys}\n")
    hdr = f"{'key':<22} " + " ".join(f"{l:>14}" for l in ORDER)
    print(hdr)
    print("-" * len(hdr))
    ph_problems = []
    for k in param_keys:
        cells = []
        ref = None
        bad = False
        for loc in ORDER:
            ph = placeholders(data[loc].get(k, ""))
            cells.append(str(ph))
            if ref is None:
                ref = ph
            elif ph != ref:
                bad = True
        mark = "  <-- MISMATCH" if bad else ""
        if bad:
            ph_problems.append(k)
        print(f"{k:<22} " + " ".join(f"{c:>14}" for c in cells) + mark)
    if not ph_problems:
        print("\n>> All parameterised keys have identical {n} slot sets/order.")
    else:
        print(f"\n>> MISMATCHED placeholder keys: {ph_problems}")

    print()
    print("=" * 78)
    print("SECTION 3  UNTRANSLATED SUSPECTS  (value identical to English)")
    print("=" * 78)
    for loc in ORDER:
        if loc == "en":
            continue
        suspects = []
        for k in sorted(en_keys):
            v = data[loc].get(k)
            if v is None:
                continue
            if v == en[k]:
                if k in WHITELIST_EXACT:
                    continue
                # pure number / symbol / shortcut-only values are fine
                if re.fullmatch(r"[\W\d_]+", v):
                    continue
                suspects.append((k, v))
        print(f"\n[{loc}] {len(suspects)} suspect(s):")
        for k, v in suspects:
            print(f"   {k:<22} = {v!r}")
    print("\nNote: WHITELIST_EXACT (proper nouns) excluded: " + ", ".join(sorted(WHITELIST_EXACT)))

    print()
    print("=" * 78)
    print("SECTION 4  KEY LENGTH RANKING  (by max char length across locales)")
    print("=" * 78)
    rows = []
    for k in en_keys:
        lens = {loc: len(data[loc].get(k, "")) for loc in ORDER}
        rows.append((max(lens.values()), k, lens))
    rows.sort(reverse=True)
    hdr = f"{'key':<22} " + " ".join(f"{l:>5}" for l in ORDER) + "   max"
    print(hdr)
    print("-" * len(hdr))
    for mx, k, lens in rows[:30]:
        print(f"{k:<22} " + " ".join(f"{lens[l]:>5}" for l in ORDER) + f"   {mx:>3}")

    print()
    print("=" * 78)
    print("SECTION 5  TRUNCATION RISK  (values >= 26 chars, likely to overflow)")
    print("=" * 78)
    for loc in ORDER:
        long_vals = sorted(
            ((len(v), k, v) for k, v in data[loc].items() if len(v) >= 26),
            reverse=True,
        )
        print(f"\n[{loc}] {len(long_vals)} value(s) >= 26 chars:")
        for n, k, v in long_vals:
            print(f"   {n:>3}  {k:<22} {v!r}")


if __name__ == "__main__":
    main()
