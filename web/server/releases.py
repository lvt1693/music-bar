"""A mirror of MusicBar's GitHub releases: the installer and the portable zip of the latest one and
the three before it (in case the newest one misbehaves), each checked against the SHA-256 GitHub
publishes before it is offered."""

from __future__ import annotations

import hashlib
import json
import shutil
import threading
import time
import urllib.request

from . import store

REPO = "lvt1693/music-bar"
API = f"https://api.github.com/repos/{REPO}/releases?per_page=100"
KEEP = 4  # the latest and the three before it, in case the newest one misbehaves
WANTED = ("-Setup.exe", "-portable.zip")

_lock = threading.Lock()
_last_error = ""


def _get(url: str, timeout: float = 30):
    req = urllib.request.Request(url, headers={"User-Agent": "musicbar-web", "Accept": "application/vnd.github+json"})
    return urllib.request.urlopen(req, timeout=timeout)


def sync() -> str:
    """Fetches what's missing; returns a line for the log."""
    global _last_error
    if not _lock.acquire(blocking=False):
        return "sync already running"
    try:
        with _get(API) as r:
            published = [x for x in json.load(r) if not x.get("draft") and not x.get("prerelease")]
        releases = published[:KEEP]
        store.RELEASES.mkdir(parents=True, exist_ok=True)
        # GitHub's own download counts, every release since the first: the site adds its mirror's on top.
        counts = {x["tag_name"]: sum(a.get("download_count", 0) for a in x.get("assets", []) if a["name"].endswith(WANTED))
                  for x in published}
        (store.RELEASES / "counts.json").write_text(json.dumps({"at": time.time(), "github": counts}), encoding="utf-8")
        done = []
        for rel in releases:
            tag = rel["tag_name"]
            folder = store.RELEASES / tag.replace("/", "_")
            folder.mkdir(exist_ok=True)
            assets = []
            for a in rel.get("assets", []):
                if not a["name"].endswith(WANTED):
                    continue
                digest = (a.get("digest") or "").removeprefix("sha256:")
                target = folder / a["name"]
                if not (target.exists() and target.stat().st_size == a["size"]):
                    part = target.with_suffix(target.suffix + ".part")
                    sha = hashlib.sha256()
                    with _get(a["browser_download_url"], timeout=120) as r, open(part, "wb") as f:
                        while chunk := r.read(1 << 20):
                            sha.update(chunk)
                            f.write(chunk)
                    if digest and sha.hexdigest() != digest:
                        part.unlink(missing_ok=True)
                        raise RuntimeError(f"{a['name']}: SHA-256 does not match GitHub's")
                    part.replace(target)
                    digest = digest or sha.hexdigest()
                elif not digest:
                    digest = hashlib.sha256(target.read_bytes()).hexdigest()
                assets.append({"name": a["name"], "size": a["size"], "sha256": digest,
                               "github_url": a["browser_download_url"]})
            meta = {"tag": tag, "version": tag.lstrip("v"), "name": rel.get("name") or tag,
                    "published_at": rel.get("published_at"), "notes": rel.get("body") or "",
                    "url": rel.get("html_url"), "assets": assets}
            (folder / "release.json").write_text(json.dumps(meta, ensure_ascii=False, indent=1), encoding="utf-8")
            done.append(tag)
        for old in store.RELEASES.iterdir():
            if old.is_dir() and old.name not in [t.replace("/", "_") for t in done]:
                shutil.rmtree(old, ignore_errors=True)
        _last_error = ""
        return "mirrored " + ", ".join(done)
    except Exception as error:  # noqa: BLE001 - the site keeps the last good mirror
        _last_error = str(error)[:200]
        return "sync failed: " + _last_error
    finally:
        _lock.release()


def releases() -> list[dict]:
    if not store.RELEASES.exists():
        return []
    found = []
    for folder in store.RELEASES.iterdir():
        meta = folder / "release.json"
        if meta.is_file():
            data = json.loads(meta.read_text(encoding="utf-8"))
            data["assets"] = [a for a in data["assets"] if (folder / a["name"]).is_file()]
            found.append(data)
    found.sort(key=lambda r: r.get("published_at") or "", reverse=True)
    return found


def latest() -> dict | None:
    rel = releases()
    return rel[0] if rel else None


def file_path(tag: str, name: str):
    for rel in releases():
        if rel["tag"] == tag:
            for a in rel["assets"]:
                if a["name"] == name:
                    return store.RELEASES / tag.replace("/", "_") / name, a
    return None, None


SPONSORS = "https://github.com/sponsors/lvt1693"


class _NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, *args, **kwargs):
        return None


def check_sponsors() -> str:
    """GitHub answers the Sponsors page itself (200) only once the profile is approved and public; until
    then it redirects. The site shows its Sponsor button from that moment on, by itself."""
    try:
        opener = urllib.request.build_opener(_NoRedirect)
        req = urllib.request.Request(SPONSORS, method="HEAD", headers={"User-Agent": "musicbar-web"})
        live = opener.open(req, timeout=20).status == 200
    except Exception:  # noqa: BLE001 - a redirect (not live yet) or no answer: keep what we knew
        live = False
    (store.DATA / "sponsors.json").write_text(json.dumps({"at": time.time(), "live": live}), encoding="utf-8")
    return "sponsors " + ("live" if live else "not live yet")


def sponsor_url() -> str | None:
    try:
        return SPONSORS if json.loads((store.DATA / "sponsors.json").read_text(encoding="utf-8"))["live"] else None
    except (OSError, ValueError, KeyError):
        return None


def downloads() -> int:
    """Every download of the app: GitHub's count plus this server's mirror."""
    github = 0
    try:
        github = sum(json.loads((store.RELEASES / "counts.json").read_text(encoding="utf-8"))["github"].values())
    except (OSError, ValueError, KeyError):
        pass
    return github + store.counter_sum("app:")


def last_error() -> str:
    return _last_error


def start_background(every: float = 1800) -> None:
    def loop():
        while True:
            print("[releases]", sync(), flush=True)
            print("[releases]", check_sponsors(), flush=True)
            time.sleep(every)
    threading.Thread(target=loop, name="release-mirror", daemon=True).start()
