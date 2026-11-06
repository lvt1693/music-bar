"""The scanner against good files and the tricks it must catch. Run: python -m tests.test_scanner (from web/)."""

import base64
import json
import struct
import zlib
from pathlib import Path

from server import scanner

ROOT = Path(__file__).resolve().parents[2]


def png(width=320, height=40, tail=b""):
    def chunk(kind, body):
        return struct.pack(">I", len(body)) + kind + body + struct.pack(">I", zlib.crc32(kind + body))
    raw = b"".join(b"\x00" + b"\x00" * width * 3 for _ in range(height))
    return (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", width, height, 8, 2, 0, 0, 0))
            + chunk(b"IDAT", zlib.compress(raw)) + chunk(b"IEND", b"") + tail)


GOOD_SKIN = b"""//! name: Neon Fill
//! kind: progress
//! description: test
float4 main(float2 uv : TEXCOORD) : COLOR
{
    float played = step(uv.x, progress);
    [unroll] for (int i = 0; i < 8; i++) { played += 0; }
    return paint(lerp(colorB.rgb, colorA.rgb, uv.x), played);
}
"""


def verdict(report):
    return report.verdict, [f["message"] for f in report.findings]


def check(name, condition, detail=""):
    print(("ok   " if condition else "FAIL ") + name + (f"  {detail}" if detail and not condition else ""))
    return condition


def main():
    results = []
    # Real skins that ship with MusicBar must pass clean.
    for path in sorted((ROOT / "skins").glob("*.hlsl")):
        r = scanner.scan_skin(path.read_bytes())
        results.append(check(f"sample skin {path.name} is not blocked", r.verdict != "block", verdict(r)))
    r = scanner.scan_skin(GOOD_SKIN)
    results.append(check("good skin is clean", r.verdict == "clean", verdict(r)))
    results.append(check("good skin header read", r.meta.get("name") == "Neon Fill" and r.meta.get("kind") == "progress"))
    results.append(check("CRLF normalised", b"\r" not in scanner.scan_skin(GOOD_SKIN.replace(b"\n", b"\r\n")).clean_bytes))

    bad_skins = {
        "include": GOOD_SKIN + b'\n#include "c:/windows/win.ini"\n',
        "powershell in comment": GOOD_SKIN + b"\n// powershell -enc AAAA\n",
        "exe name": GOOD_SKIN + b"\n// run payload.exe\n",
        "base64 blob": GOOD_SKIN + b"\n// " + base64.b64encode(b"x" * 300) + b"\n",
        "hex blob": GOOD_SKIN + b"\nstatic const int d[] = {" + b", ".join(b"0x4D" for _ in range(200)) + b"};\n",
        "PE file": b"MZ\x90\x00" + b"\x00" * 64,
        "PE inside": GOOD_SKIN + b"\n//MZ\x90\x00 \n",
        "zip inside": GOOD_SKIN + b"PK\x03\x04",
        "NUL bytes": GOOD_SKIN + b"\x00\x00",
        "not utf8": GOOD_SKIN + b"\xff\xfe\xfa",
        "no main": b"//! name: x\nfloat4 other(float2 uv : TEXCOORD) : COLOR { return 0; }\n",
        "huge loop": GOOD_SKIN.replace(b"i < 8", b"i < 100000"),
        "script tag": GOOD_SKIN + b"\n// <script>alert(1)</script>\n",
        "too big": GOOD_SKIN + b"//" + b"a" * 70000,
        "markup in name": GOOD_SKIN.replace(b"Neon Fill", b"<img src=x onerror=alert(1)>"),
    }
    for label, data in bad_skins.items():
        r = scanner.scan_skin(data)
        results.append(check(f"skin blocked: {label}", r.verdict == "block", verdict(r)))
    r = scanner.scan_skin(GOOD_SKIN + b"\n// made by https://example.com\n")
    results.append(check("url in skin is a warning", r.verdict == "warn", verdict(r)))

    good_preset = {"format": "musicbar-look/1", "name": "Brat Summer", "kind": "visualizer", "style": "Equalizer",
                   "colors": ["#8ace00", "#000000", "#FFFFFF"], "intensity": 0.9, "direction": "FromCenter", "idleMotion": True}
    r = scanner.scan_preset(json.dumps(good_preset).encode())
    results.append(check("good preset is clean", r.verdict == "clean", verdict(r)))
    results.append(check("preset rewritten with upper-case colors", b"#8ACE00" in r.clean_bytes))
    bad_presets = {
        "unknown key": dict(good_preset, payload="x"),
        "bad style": dict(good_preset, style="Evil"),
        "bad color": dict(good_preset, colors=["red", "#000000", "#FFFFFF"]),
        "script name": dict(good_preset, name="<script>x</script>"),
        "url name": dict(good_preset, name="go https://evil.example"),
        "intensity out of range": dict(good_preset, intensity=7),
        "nested object": dict(good_preset, colors={"a": 1}),
    }
    for label, value in bad_presets.items():
        r = scanner.scan_preset(json.dumps(value).encode())
        results.append(check(f"preset blocked: {label}", r.verdict == "block", verdict(r)))
    r = scanner.scan_preset(b'{"format":"musicbar-look/1","format":"x"}')
    results.append(check("preset blocked: duplicate keys", r.verdict == "block", verdict(r)))
    r = scanner.scan_preset(b'{"intensity": NaN}')
    results.append(check("preset blocked: NaN", r.verdict == "block", verdict(r)))

    r = scanner.scan_preview(png())
    results.append(check("good png is clean", r.verdict == "clean", verdict(r)))
    r = scanner.scan_preview(png(tail=b"MZ\x90\x00 hidden program"))
    results.append(check("png tail cut off", r.verdict == "warn" and b"hidden" not in r.clean_bytes, verdict(r)))
    r = scanner.scan_preview(b"GIF89a" + b"\x00" * 100)
    results.append(check("gif refused", r.verdict == "block"))
    r = scanner.scan_preview(png(width=10, height=10))
    results.append(check("tiny image refused", r.verdict == "block"))
    r = scanner.scan_preview(b"<svg onload=alert(1)>")
    results.append(check("svg refused", r.verdict == "block"))

    report = scanner.Report()
    scanner.check_text(report, "Nickname", "Trúc Lê_99", 2, 24, nickname=True)
    results.append(check("vietnamese nickname allowed", report.verdict == "clean", report.findings))
    report = scanner.Report()
    scanner.check_text(report, "Nickname", "<b>x</b>", 2, 24, nickname=True)
    results.append(check("markup nickname refused", report.verdict == "block"))

    print(f"\n{sum(results)}/{len(results)} passed")
    return 0 if all(results) else 1


if __name__ == "__main__":
    raise SystemExit(main())
