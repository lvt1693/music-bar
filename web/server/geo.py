"""Is a visitor in Vietnam? Only to pick the page's language for someone who hasn't picked one.

Vietnam's address blocks come from APNIC's public delegation file (refreshed weekly) and are looked up
by binary search. Nothing about the visitor is stored or sent anywhere."""

from __future__ import annotations

import bisect
import ipaddress
import json
import threading
import time
import urllib.request

from . import store

URL = "https://ftp.apnic.net/stats/apnic/delegated-apnic-latest"
FILE = store.DATA / "geo-vn.json"
WEEK = 7 * 86400

_ranges: dict[int, tuple[list[int], list[int]]] = {4: ([], []), 6: ([], [])}
_loaded = False


def _load() -> None:
    global _ranges, _loaded
    _loaded = True
    try:
        data = json.loads(FILE.read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return
    ranges = {}
    for version in (4, 6):
        pairs = sorted((int(a), int(b)) for a, b in data.get(str(version), []))
        ranges[version] = ([a for a, _ in pairs], [b for _, b in pairs])
    _ranges = ranges


def is_vietnam(ip: str) -> bool:
    if not _loaded:
        _load()
    try:
        addr = ipaddress.ip_address(ip)
    except ValueError:
        return False
    if addr.version == 6 and addr.ipv4_mapped:
        addr = addr.ipv4_mapped
    starts, ends = _ranges.get(addr.version, ([], []))
    n = int(addr)
    i = bisect.bisect_right(starts, n) - 1
    return i >= 0 and n <= ends[i]


def sync() -> str:
    """Fetches APNIC's file and keeps Vietnam's blocks; returns a line for the log."""
    v4, v6 = [], []
    req = urllib.request.Request(URL, headers={"User-Agent": "musicbar-web"})
    with urllib.request.urlopen(req, timeout=90) as r:
        for raw in r:
            parts = raw.decode("ascii", "replace").strip().split("|")
            if len(parts) < 7 or parts[1] != "VN" or parts[6] not in ("allocated", "assigned"):
                continue
            if parts[2] == "ipv4":
                start = int(ipaddress.IPv4Address(parts[3]))
                v4.append((start, start + int(parts[4]) - 1))
            elif parts[2] == "ipv6":
                net = ipaddress.IPv6Network(f"{parts[3]}/{parts[4]}")
                v6.append((int(net.network_address), int(net.broadcast_address)))
    if len(v4) < 100:
        raise RuntimeError(f"only {len(v4)} Vietnamese IPv4 blocks: the file looks wrong, keeping the old one")
    store.DATA.mkdir(parents=True, exist_ok=True)
    tmp = FILE.with_suffix(".part")
    tmp.write_text(json.dumps({"at": time.time(), "4": v4, "6": v6}), encoding="utf-8")
    tmp.replace(FILE)
    _load()
    return f"{len(v4)} IPv4 and {len(v6)} IPv6 blocks"


def start_background() -> None:
    def loop():
        while True:
            try:
                stale = not FILE.exists() or time.time() - FILE.stat().st_mtime > WEEK
                if stale:
                    print("[geo]", sync(), flush=True)
            except Exception as error:  # noqa: BLE001 - the site keeps the last good list
                print("[geo] sync failed:", str(error)[:200], flush=True)
            time.sleep(6 * 3600)
    threading.Thread(target=loop, name="geo-vn", daemon=True).start()
