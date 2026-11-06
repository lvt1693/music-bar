"""Static checks for everything people upload to the gallery.

Nothing here runs, compiles, decodes or opens an uploaded file with anything but this code: skins are
read as text, presets as JSON (and rewritten from the allowed keys only), preview images by their
headers alone. A verdict is "block" (refused outright), "warn" (kept for review with the reasons
shown) or "clean" (still waits for a person to approve it).
"""

from __future__ import annotations

import json
import re
import struct
import unicodedata
from dataclasses import dataclass, field

SKIN_MAX = 64 * 1024
PRESET_MAX = 8 * 1024
PREVIEW_MAX = 3 * 1024 * 1024

STYLES = {
    "Classic", "Aurora", "Liquid", "Neon", "Spectrum", "Waveform", "Ribbon", "Cinema", "Mosaic", "Mesh",
    "Silk", "Holo", "Bokeh", "Ink", "Equalizer", "Oscilloscope", "Horizon", "Aura", "Fireflies", "Led",
    "LedRainbow", "LedSpectrum", "LedBreathe", "LedRipple", "LedScanner", "LedStarlight", "LedFire",
    "LedMeteor", "LedChase", "LedVu",
}
DIRECTIONS = {"FromStart", "FromEnd", "FromCenter", "FromEnds"}
KINDS = {"progress", "visualizer", "led"}
PRESET_FORMAT = "musicbar-look/1"
PRESET_KEYS = {"format", "name", "kind", "style", "colors", "intensity", "direction", "idleMotion", "description"}

# Words with no business in a shader or a preset: the tools of a dropper, or code for another place.
_DANGER = re.compile(
    r"powershell|cmd\.exe|rundll32|regsvr32|mshta|certutil|wscript|cscript|bitsadmin|schtasks|"
    r"invoke-(?:expression|webrequest)|frombase64string|downloadstring|new-object\s+net\.|"
    r"<\s*script|<\?php|javascript:|vbscript:|\beval\s*\(|document\.cookie|"
    r"\.(?:exe|dll|scr|bat|cmd|ps1|vbs|msi|lnk|hta|jar)\b",
    re.IGNORECASE,
)
_URL = re.compile(r"\b(?:https?|ftp|file)://|\\\\[a-z0-9_.-]+\\", re.IGNORECASE)
_BASE64_RUN = re.compile(r"[A-Za-z0-9+/]{160,}={0,2}")
_HEX_RUN = re.compile(r"(?:0x[0-9A-Fa-f]{2,}\s*,\s*){96,}|[0-9A-Fa-f]{256,}")
_BINARY_MAGIC = [(b"MZ", "a Windows program (MZ header)"), (b"\x7fELF", "a Linux program (ELF header)"),
                 (b"PK\x03\x04", "a ZIP archive"), (b"Rar!", "a RAR archive"), (b"7z\xbc\xaf", "a 7-Zip archive"),
                 (b"%PDF", "a PDF document"), (b"{\\rtf", "an RTF document"), (b"\xd0\xcf\x11\xe0", "an Office document")]
_RESERVED = {"line", "point", "triangle", "sample", "texture", "vector", "matrix", "half", "in", "out", "inline", "string"}


@dataclass
class Report:
    verdict: str = "clean"
    findings: list[dict] = field(default_factory=list)
    clean_bytes: bytes = b""
    meta: dict = field(default_factory=dict)

    def add(self, level: str, message: str, where: str = "") -> None:
        self.findings.append({"level": level, "message": message, "where": where})
        if level == "block":
            self.verdict = "block"
        elif level == "warn" and self.verdict == "clean":
            self.verdict = "warn"

    def to_dict(self) -> dict:
        return {"verdict": self.verdict, "findings": self.findings, "meta": self.meta}


# ------------------------------------------------------------------ text fields

_TEXT_STRIP = re.compile(r"[\u0000-\u001f\u007f-\u009f​-‏ -‮⁠-⁯﻿]")


def clean_text(value: str | None, limit: int) -> str:
    """One line of plain text: control and invisible characters out, spaces collapsed, cut at the limit."""
    value = unicodedata.normalize("NFC", value or "")
    value = _TEXT_STRIP.sub(" ", value)
    value = re.sub(r"\s+", " ", value).strip()
    return value[:limit]


def check_text(report: Report, label: str, value: str, low: int, high: int, nickname: bool = False) -> str:
    text = clean_text(value, high + 1)
    if len(text) < low:
        report.add("block", f"{label} is too short (at least {low} characters)", label)
    if len(text) > high:
        report.add("block", f"{label} is too long (at most {high} characters)", label)
    if re.search(r"[<>`\"{}\\]", text):
        report.add("block", f"{label} has characters that aren't allowed (< > ` \" {{ }} \\)", label)
    if _URL.search(text) or re.search(r"\bwww\.", text, re.I):
        report.add("block", f"{label} can't contain links", label)
    if _DANGER.search(text):
        report.add("block", f"{label} contains something that looks like a command or a script", label)
    if nickname and not re.fullmatch(r"[\w .\-]+", text):
        report.add("block", "Nickname: letters, digits, spaces, . _ - only", label)
    return text[:high]


# ------------------------------------------------------------------ skins

def scan_skin(data: bytes) -> Report:
    report = Report()
    if len(data) > SKIN_MAX:
        report.add("block", f"Skin file is larger than {SKIN_MAX // 1024} KB")
        return report
    for magic, what in _BINARY_MAGIC:
        if data.startswith(magic):
            report.add("block", f"This is {what}, not a skin")
            return report
    for magic, what in ((b"PK\x03\x04", "a ZIP archive"), (b"MZ\x90\x00", "a Windows program"), (b"\x7fELF", "a Linux program")):
        if magic in data:
            report.add("block", f"{what.capitalize()} hidden inside the file")
            return report
    if b"\x00" in data:
        report.add("block", "Binary data (NUL bytes) in a text file")
        return report
    try:
        text = data.decode("utf-8")
    except UnicodeDecodeError as error:
        report.add("block", f"Not UTF-8 text (byte {error.start})")
        return report
    text = text.lstrip("﻿").replace("\r\n", "\n").replace("\r", "\n")
    bad = [c for c in text if (ord(c) < 32 and c not in "\t\n") or 0x7f <= ord(c) <= 0x9f]
    if bad:
        report.add("block", f"{len(bad)} control characters in the text")
        return report
    lines = text.split("\n")
    if len(lines) > 2000:
        report.add("block", "More than 2000 lines")
    for number, line in enumerate(lines, 1):
        if len(line) > 1000:
            report.add("block", "A line longer than 1000 characters", f"line {number}")
            break

    code = _strip_comments(text)
    for number, line in enumerate(lines, 1):
        stripped = line.strip()
        if stripped.startswith("#"):
            directive = stripped[1:].strip().split(" ")[0].lower()
            if directive in ("include", "import", "using"):
                report.add("block", f"#{directive} is not allowed: a skin can only use what MusicBar gives it", f"line {number}")
            elif directive in ("pragma", "line", "error"):
                report.add("warn", f"#{directive} is unusual in a skin", f"line {number}")
            elif directive not in ("define", "undef", "if", "ifdef", "ifndef", "elif", "else", "endif"):
                report.add("block", f"Unknown preprocessor directive #{directive}", f"line {number}")

    for pattern, label, level in ((_DANGER, "Text that looks like a command, a script or a program name", "block"),
                                  (_BASE64_RUN, "A long encoded blob (base64): could hide a file", "block"),
                                  (_HEX_RUN, "A long run of raw bytes (hex): could hide a file", "block"),
                                  (_URL, "A web or network address", "warn")):
        match = pattern.search(text)
        if match:
            report.add(level, label, f"line {text.count(chr(10), 0, match.start()) + 1}: {match.group(0)[:60]}")

    if not re.search(r"\bfloat4\s+main\s*\(", code):
        report.add("block", "No entry point: float4 main(float2 uv : TEXCOORD) : COLOR")
    if re.search(r"\b(?:Texture[123]D|TextureCube|SamplerState|sampler(?:1D|2D|3D|CUBE)?)\b", code):
        report.add("warn", "Declares its own texture or sampler: skins get theirs from MusicBar")
    if re.search(r'"[^"\n]*"', code):
        report.add("warn", "String literals in the code")
    if re.search(r"\bwhile\s*\(|\bdo\s*\{", code):
        report.add("warn", "while/do loop: the GPU needs loops with a fixed count")
    for match in re.finditer(r"\bfor\s*\(([^;]*);([^;]*);", code):
        bound = re.search(r"<=?\s*(\d+(?:\.\d*)?)", match.group(2))
        if bound and float(bound.group(1)) > 256:
            level = "block" if float(bound.group(1)) > 4096 else "warn"
            report.add(level, f"Loop up to {bound.group(1)}: may stall the graphics card", f"line {code.count(chr(10), 0, match.start()) + 1}")
    for match in re.finditer(r"\b(?:float[1-4]?|int[1-4]?|bool|half[1-4]?)\s+([A-Za-z_]\w*)", code):
        if match.group(1) in _RESERVED:
            report.add("warn", f"Variable named '{match.group(1)}', a reserved word in HLSL: it won't compile")

    header = _skin_header(text)
    report.meta = header
    if header.get("kind") and header["kind"] not in ("progress", "visualizer"):
        report.add("warn", f"Unknown kind '{header['kind']}' (progress or visualizer)")
    for key in ("name", "author", "description"):
        if header.get(key) and (_URL.search(header[key]) or re.search(r"[<>`]", header[key])):
            report.add("block", f"The //! {key} line has links or markup")
    report.clean_bytes = text.encode("utf-8")
    return report


def _strip_comments(text: str) -> str:
    text = re.sub(r"/\*.*?\*/", lambda m: "\n" * m.group(0).count("\n"), text, flags=re.S)
    return re.sub(r"//[^\n]*", "", text)


def _skin_header(text: str) -> dict:
    header = {}
    for line in text.split("\n")[:40]:
        line = line.strip()
        if not line.startswith("//!") or ":" not in line:
            continue
        key, _, value = line[3:].partition(":")
        key = key.strip().lower()
        if key in ("name", "author", "kind", "description"):
            header[key] = clean_text(value, 200)
    if "kind" in header:
        header["kind"] = header["kind"].lower()
    return header


# ------------------------------------------------------------------ presets

def scan_preset(data: bytes) -> Report:
    """A look preset: only the known keys, each checked, then written out again from those alone."""
    report = Report()
    if len(data) > PRESET_MAX:
        report.add("block", f"Preset is larger than {PRESET_MAX // 1024} KB")
        return report
    try:
        text = data.decode("utf-8").lstrip("﻿")
        value = json.loads(text, object_pairs_hook=_no_duplicates, parse_constant=_no_constants)
    except (UnicodeDecodeError, ValueError) as error:
        report.add("block", f"Not valid JSON: {str(error)[:120]}")
        return report
    if not isinstance(value, dict):
        report.add("block", "A preset is a JSON object")
        return report
    extra = set(value) - PRESET_KEYS
    if extra:
        report.add("block", f"Unknown keys: {', '.join(sorted(extra))[:120]}")
    if value.get("format") != PRESET_FORMAT:
        report.add("block", f'"format" must be "{PRESET_FORMAT}"')
    style = value.get("style")
    if style not in STYLES:
        report.add("block", f'"style" must be one of MusicBar\'s styles, not {str(style)[:40]!r}')
    kind = value.get("kind", "led" if isinstance(style, str) and style.startswith("Led") else "visualizer")
    if kind not in KINDS:
        report.add("block", '"kind" must be progress, visualizer or led')
    colors = value.get("colors")
    if not (isinstance(colors, list) and len(colors) == 3 and all(isinstance(c, str) and re.fullmatch(r"#[0-9A-Fa-f]{6}", c) for c in colors)):
        report.add("block", '"colors" must be three colors like "#FF2BD6"')
        colors = []
    intensity = value.get("intensity", 0.8)
    if isinstance(intensity, bool) or not isinstance(intensity, (int, float)) or not 0.05 <= intensity <= 1:
        report.add("block", '"intensity" must be a number from 0.05 to 1')
        intensity = 0.8
    direction = value.get("direction", "FromStart")
    if direction not in DIRECTIONS:
        report.add("block", '"direction" must be FromStart, FromEnd, FromCenter or FromEnds')
    idle = value.get("idleMotion", True)
    if not isinstance(idle, bool):
        report.add("block", '"idleMotion" must be true or false')
    name = value.get("name", "")
    description = value.get("description", "")
    if not isinstance(name, str) or not isinstance(description, str):
        report.add("block", '"name" and "description" must be text')
        name, description = "", ""
    name = check_text(report, "Preset name", name, 2, 40)
    description = check_text(report, "Preset description", description, 0, 200) if description else ""
    if report.verdict == "block":
        return report
    clean = {"format": PRESET_FORMAT, "name": name, "kind": kind, "style": style,
             "colors": [c.upper() for c in colors], "intensity": round(float(intensity), 3),
             "direction": direction, "idleMotion": idle}
    if description:
        clean["description"] = description
    report.meta = {"name": name, "kind": kind, "style": style, "colors": clean["colors"]}
    report.clean_bytes = (json.dumps(clean, ensure_ascii=False, indent=2) + "\n").encode("utf-8")
    return report


def _no_duplicates(pairs):
    keys = [k for k, _ in pairs]
    if len(keys) != len(set(keys)):
        raise ValueError("duplicate keys")
    return dict(pairs)


def _no_constants(name):
    raise ValueError(f"{name} is not allowed")


# ------------------------------------------------------------------ preview images

def scan_preview(data: bytes) -> Report:
    """A PNG, JPEG or WebP, judged by its header; anything after the image's own end is cut off."""
    report = Report()
    if len(data) > PREVIEW_MAX:
        report.add("block", f"Preview is larger than {PREVIEW_MAX // (1024 * 1024)} MB")
        return report
    kind, width, height, end = _image_header(data)
    if kind is None:
        report.add("block", "The preview must be a PNG, JPEG or WebP image")
        return report
    if not (64 <= width <= 4096 and 16 <= height <= 2400):
        report.add("block", f"Preview size {width}x{height} is out of range (64-4096 x 16-2400)")
        return report
    body = data[:end]
    if end < len(data):
        report.add("warn", f"{len(data) - end} bytes after the end of the image were removed")
    for marker, what in ((b"<script", "a script tag"), (b"<?php", "PHP code"), (b"<svg", "SVG markup"),
                         (b"<html", "HTML"), (b"MZ\x90\x00", "a Windows program"), (b"PK\x03\x04", "a ZIP archive")):
        if marker in body.lower() if marker.startswith(b"<") else marker in body:
            report.add("block", f"The image contains {what}")
    report.meta = {"type": kind, "width": width, "height": height}
    report.clean_bytes = body
    return report


def _image_header(data: bytes):
    if data.startswith(b"\x89PNG\r\n\x1a\n") and len(data) >= 33 and data[12:16] == b"IHDR":
        width, height = struct.unpack(">II", data[16:24])
        end = _png_end(data)
        return ("png", width, height, end) if end else (None, 0, 0, 0)
    if data.startswith(b"\xff\xd8\xff"):
        size = _jpeg_size(data)
        if size:
            eoi = data.rfind(b"\xff\xd9")
            return ("jpeg", size[0], size[1], eoi + 2 if eoi > 0 else len(data))
    if len(data) >= 30 and data[:4] == b"RIFF" and data[8:12] == b"WEBP":
        riff_end = min(len(data), 8 + struct.unpack("<I", data[4:8])[0])
        chunk = data[12:16]
        if chunk == b"VP8X":
            width = 1 + int.from_bytes(data[24:27], "little")
            height = 1 + int.from_bytes(data[27:30], "little")
            return "webp", width, height, riff_end
        if chunk == b"VP8 " and data[23:26] == b"\x9d\x01\x2a":
            width, height = struct.unpack("<HH", data[26:30])
            return "webp", width & 0x3FFF, height & 0x3FFF, riff_end
        if chunk == b"VP8L" and data[20] == 0x2F:
            bits = int.from_bytes(data[21:25], "little")
            return "webp", (bits & 0x3FFF) + 1, ((bits >> 14) & 0x3FFF) + 1, riff_end
    return None, 0, 0, 0


def _png_end(data: bytes) -> int:
    at = 8
    while at + 12 <= len(data):
        length = struct.unpack(">I", data[at:at + 4])[0]
        kind = data[at + 4:at + 8]
        at += 12 + length
        if kind == b"IEND":
            return at
        if length > len(data):
            return 0
    return 0


def _jpeg_size(data: bytes):
    at = 2
    while at + 9 < len(data):
        if data[at] != 0xFF:
            at += 1
            continue
        marker = data[at + 1]
        if marker in (0xD8, 0x01) or 0xD0 <= marker <= 0xD7:
            at += 2
            continue
        length = struct.unpack(">H", data[at + 2:at + 4])[0]
        if marker in (0xC0, 0xC1, 0xC2, 0xC3, 0xC5, 0xC6, 0xC7, 0xC9, 0xCA, 0xCB, 0xCD, 0xCE, 0xCF):
            height, width = struct.unpack(">HH", data[at + 5:at + 9])
            return width, height
        at += 2 + length
    return None
