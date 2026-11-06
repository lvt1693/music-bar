"""Server-side commands: python -m server.cli <command> ...

  set-admin <name>             sets the admin's name and asks for the password (or reads MUSICBAR_ADMIN_PASSWORD)
  has-admin                    exits 0 when the admin account has a name and a password
  bans                         who is shut out right now; unban <address> lets one back in
  queue                        what waits for review, with the scan verdicts
  show <id>                    one item: its scan report and its file, as text
  approve <id> [note]          approve; reject <id> [note]; hide <id>; feature <id>
  seed <folder>                imports web/seed (manifest.json, skins/, presets/, previews/) as approved
  add-ad <image> <link> <title> [alt]   adds an ad banner
  sync                         mirrors the GitHub releases now
"""

from __future__ import annotations

import getpass
import json
import os
import sys
import time
from pathlib import Path

from . import auth, releases, scanner, store


def main(argv: list[str]) -> int:
    if not argv:
        print(__doc__)
        return 1
    cmd, args = argv[0], argv[1:]
    if cmd == "set-admin":
        if not args or len(args[0].strip()) < 3:
            print("set-admin <name>: a name of 3 characters or more")
            return 1
        password = os.environ.get("MUSICBAR_ADMIN_PASSWORD") or getpass.getpass("Admin password: ")
        if len(password) < 16:
            print("At least 16 characters")
            return 1
        auth.set_admin(args[0], password)
        print(f"admin {args[0].strip().lower()!r} set")
    elif cmd == "has-admin":
        return 0 if auth.has_admin() else 1
    elif cmd == "bans":
        for b in store.ban_rows():
            print(f"{b['ip']:<40} until {time.strftime('%Y-%m-%d %H:%M', time.localtime(b['until']))}  {b['reason']}  ({b['blocked']} turned away)")
    elif cmd == "unban":
        store.lift_ban(args[0])
        print(f"{args[0]} let back in (the site picks it up at its next restart)")
    elif cmd == "queue":
        rows, total = store.list_items(status="pending", sort="old", limit=200)
        print(f"{total} waiting")
        for r in rows:
            report = json.loads(r["scan_report"])
            print(f"#{r['id']:<5} {r['scan_verdict']:<6} {r['type']:<6} {r['category']:<10} {r['name']!r} by {r['author']!r} "
                  f"({r['source']}, {time.strftime('%Y-%m-%d %H:%M', time.localtime(r['created_at']))})")
            for f in report.get("findings", []):
                print(f"         {f['level']}: {f['message']} {f.get('where', '')}")
    elif cmd == "show":
        row = store.get_item(int(args[0]), approved_only=False)
        if row is None:
            print("no such item")
            return 1
        d = store.item_dict(row, admin=True)
        print(json.dumps({k: v for k, v in d.items() if k != "scan_report"}, ensure_ascii=False, indent=1))
        print("scan:", json.dumps(d["scan_report"], ensure_ascii=False, indent=1))
        path = store.blob_path(row["file_sha"])
        print("----- file -----")
        print(path.read_text(encoding="utf-8", errors="replace") if path else "(missing)")
    elif cmd in ("approve", "reject", "hide"):
        status = {"approve": "approved", "reject": "rejected", "hide": "hidden"}[cmd]
        store.update_item(int(args[0]), status=status, reviewed_at=time.time(), note=" ".join(args[1:])[:300])
        print(f"#{args[0]} {status}")
    elif cmd == "feature":
        store.update_item(int(args[0]), featured=1)
        print(f"#{args[0]} featured")
    elif cmd == "seed":
        return seed(Path(args[0]))
    elif cmd == "add-ad":
        image, link, title = Path(args[0]), args[1], args[2]
        alt = args[3] if len(args) > 3 else title
        pic = scanner.scan_preview(image.read_bytes())
        if pic.verdict == "block" or not link.startswith("https://"):
            print("refused:", pic.findings)
            return 1
        ad_id = store.insert_ad(title=title, image_sha=store.put_blob(pic.clean_bytes), image_type=pic.meta["type"],
                                width=pic.meta["width"], height=pic.meta["height"], link=link, alt=alt, weight=1)
        print(f"ad #{ad_id} added")
    elif cmd == "seed-ads":
        return seed_ads(Path(args[0]))
    elif cmd == "sync":
        print(releases.sync())
    else:
        print(__doc__)
        return 1
    return 0


def seed(folder: Path) -> int:
    """Our own first items, through the same scanner as everyone's, approved and featured."""
    manifest = json.loads((folder / "manifest.json").read_text(encoding="utf-8"))
    added = 0
    for entry in manifest:
        data = (folder / entry["file"]).read_bytes()
        report = scanner.scan_preset(data) if entry["type"] == "preset" else scanner.scan_skin(data)
        if report.verdict == "block":
            print(f"{entry['slug']}: blocked {report.findings}")
            continue
        existing = store.get_item(entry["slug"], approved_only=False)
        fields = dict(type=entry["type"], category=entry["category"], name=entry["name"],
                      description=entry.get("description_vi") or entry.get("description_en", ""),
                      description_en=entry.get("description_en", ""),
                      author="MusicBar", source="seed", status="approved", file_sha=store.put_blob(report.clean_bytes),
                      file_size=len(report.clean_bytes), accent=entry.get("accent", "#FF5F6D"),
                      tags=entry.get("tags", []), colors=report.meta.get("colors") or entry.get("colors", []),
                      style=report.meta.get("style", ""),
                      scan_verdict=report.verdict, scan_report=report.to_dict(), featured=1, reviewed_at=time.time())
        for kind, key in (("png", "preview_sha"), ("webp", "preview_sha"), ("jpeg", "preview_sha")):
            pic_path = folder / "previews" / f"{entry['slug']}.{kind if kind != 'jpeg' else 'jpg'}"
            if pic_path.exists():
                pic = scanner.scan_preview(pic_path.read_bytes())
                if pic.verdict != "block":
                    fields[key] = store.put_blob(pic.clean_bytes)
                    fields["preview_type"] = pic.meta["type"]
                break
        for ext in ("mp4", "webm"):
            clip = folder / "previews" / f"{entry['slug']}.{ext}"
            if clip.exists():
                fields["video_sha"] = store.put_blob(clip.read_bytes())
                break
        if existing:
            fields["scan_report"] = json.dumps(fields["scan_report"], ensure_ascii=False)
            fields["tags"] = json.dumps(fields["tags"], ensure_ascii=False)
            fields["colors"] = json.dumps(fields["colors"], ensure_ascii=False)
            store.update_item(existing["id"], **fields)
        else:
            store.insert_item(slug=entry["slug"], **fields)
        added += 1
        print(f"{entry['slug']}: {report.verdict}")
    print(f"{added} seeded")
    return 0


def seed_ads(folder: Path) -> int:
    """The ads in ads.json, each added once (by its title): later changes are made on the admin page."""
    known = {a["title"] for a in store.all_ads()}
    for ad in json.loads((folder / "ads.json").read_text(encoding="utf-8")):
        if ad["title"] in known:
            continue
        pic = scanner.scan_preview((folder / ad["image"]).read_bytes())
        if pic.verdict == "block" or not ad["link"].startswith("https://"):
            print(f"{ad['title']}: refused {pic.findings}")
            continue
        ad_id = store.insert_ad(title=ad["title"], image_sha=store.put_blob(pic.clean_bytes), image_type=pic.meta["type"],
                                width=pic.meta["width"], height=pic.meta["height"], link=ad["link"],
                                alt=ad.get("alt", ad["title"]), weight=ad.get("weight", 1))
        print(f"ad #{ad_id}: {ad['title']}")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
