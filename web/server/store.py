"""SQLite for the gallery, the ads and the release mirror's notes; files kept by their SHA-256.

Uploaded files are stored under data/blobs/<sha256> with no extension, outside anything served as a
directory, and only ever read back as bytes for a response with a fixed content type.
"""

from __future__ import annotations

import hashlib
import json
import os
import re
import secrets
import sqlite3
import threading
import time
import unicodedata
from pathlib import Path

DATA = Path(os.environ.get("MUSICBAR_WEB_DATA", Path(__file__).resolve().parent.parent / "data"))
BLOBS = DATA / "blobs"
RELEASES = DATA / "releases"

_local = threading.local()

SCHEMA = """
create table if not exists items (
    id integer primary key autoincrement,
    slug text unique not null,
    type text not null check (type in ('skin', 'preset')),
    category text not null check (category in ('visualizer', 'progress', 'led')),
    name text not null,
    description text not null default '',
    description_en text not null default '',
    author text not null default '',
    device text not null default '',
    source text not null default 'web',
    status text not null default 'pending' check (status in ('pending', 'approved', 'rejected', 'hidden')),
    file_sha text not null,
    file_size integer not null,
    preview_sha text,
    preview_type text,
    video_sha text,
    accent text not null default '#FF5F6D',
    tags text not null default '[]',
    colors text not null default '[]',
    style text not null default '',
    scan_verdict text not null,
    scan_report text not null default '{}',
    note text not null default '',
    featured integer not null default 0,
    downloads integer not null default 0,
    ip_hash text not null default '',
    created_at real not null,
    reviewed_at real
);
create index if not exists items_status on items(status, created_at);
create table if not exists refused (
    id integer primary key autoincrement,
    at real not null,
    ip_hash text not null,
    device text not null default '',
    source text not null,
    name text not null default '',
    report text not null
);
create table if not exists ads (
    id integer primary key autoincrement,
    title text not null,
    image_sha text not null,
    image_type text not null,
    width integer not null,
    height integer not null,
    link text not null,
    alt text not null default '',
    weight integer not null default 1,
    active integer not null default 1,
    starts_at real,
    ends_at real,
    impressions integer not null default 0,
    clicks integer not null default 0,
    created_at real not null
);
create table if not exists hits (
    ip_hash text not null,
    kind text not null,
    at real not null
);
create index if not exists hits_ip on hits(ip_hash, kind, at);
create table if not exists counters (
    key text primary key,
    n integer not null default 0
);
-- What the admin's dashboard reads. Visitors are a daily hash (address + browser + day), never kept as is.
create table if not exists views (
    day text not null,
    at real not null,
    path text not null,
    lang text not null default '',
    vn integer not null default 0,
    bot integer not null default 0,
    ref text not null default '',
    vh text not null default ''
);
create index if not exists views_day on views(day);
create table if not exists dl_log (
    day text not null,
    at real not null,
    kind text not null check (kind in ('app', 'item', 'apply')),
    ref text not null,
    vn integer not null default 0
);
create index if not exists dl_day on dl_log(day, kind);
-- The shield's notes: suspicious requests, bans, sign-ins. These keep the address: the admin needs it.
create table if not exists sec_events (
    id integer primary key autoincrement,
    at real not null,
    ip text not null,
    kind text not null,
    path text not null default '',
    ua text not null default '',
    detail text not null default ''
);
create index if not exists sec_at on sec_events(at);
create index if not exists sec_ip on sec_events(ip, at);
create table if not exists bans (
    ip text primary key,
    since real not null,
    until real not null,
    reason text not null,
    blocked integer not null default 0
);
"""


def db() -> sqlite3.Connection:
    conn = getattr(_local, "conn", None)
    if conn is None:
        DATA.mkdir(parents=True, exist_ok=True)
        conn = sqlite3.connect(DATA / "musicbar.db", timeout=10)
        conn.row_factory = sqlite3.Row
        conn.execute("pragma journal_mode=wal")
        conn.execute("pragma foreign_keys=on")
        conn.executescript(SCHEMA)
        # Columns added after a database was made.
        have = {r[1] for r in conn.execute("pragma table_info(items)")}
        for column, kind in (("description_en", "text not null default ''"),):
            if column not in have:
                conn.execute(f"alter table items add column {column} {kind}")
        conn.commit()
        _local.conn = conn
    return conn


# ------------------------------------------------------------------ blobs

def put_blob(data: bytes) -> str:
    sha = hashlib.sha256(data).hexdigest()
    BLOBS.mkdir(parents=True, exist_ok=True)
    path = BLOBS / sha
    if not path.exists():
        tmp = path.with_name(sha + ".part" + secrets.token_hex(4))
        tmp.write_bytes(data)
        os.chmod(tmp, 0o640)
        tmp.replace(path)
    return sha


def blob_path(sha: str) -> Path | None:
    if not sha or not re.fullmatch(r"[0-9a-f]{64}", sha):
        return None
    path = BLOBS / sha
    return path if path.is_file() else None


# ------------------------------------------------------------------ helpers

_secret_salt = None


def ip_hash(ip: str) -> str:
    """Addresses are kept only as a salted hash: enough to slow down floods, nothing to leak."""
    global _secret_salt
    if _secret_salt is None:
        salt_file = DATA / "salt"
        DATA.mkdir(parents=True, exist_ok=True)
        if not salt_file.exists():
            salt_file.write_text(secrets.token_hex(16))
            os.chmod(salt_file, 0o600)
        _secret_salt = salt_file.read_text().strip()
    return hashlib.sha256((_secret_salt + (ip or "")).encode()).hexdigest()[:24]


def slugify(name: str) -> str:
    text = unicodedata.normalize("NFKD", name.replace("đ", "d").replace("Đ", "D"))
    text = "".join(c for c in text if not unicodedata.combining(c)).lower()
    text = re.sub(r"[^a-z0-9]+", "-", text).strip("-")[:48] or "item"
    slug, n = text, 2
    while db().execute("select 1 from items where slug = ?", (slug,)).fetchone():
        slug = f"{text}-{n}"
        n += 1
    return slug


def count_hits(ip: str, kind: str, seconds: float) -> int:
    return db().execute("select count(*) from hits where ip_hash = ? and kind = ? and at > ?",
                        (ip, kind, time.time() - seconds)).fetchone()[0]


def add_hit(ip: str, kind: str) -> None:
    conn = db()
    conn.execute("insert into hits values (?, ?, ?)", (ip, kind, time.time()))
    conn.execute("delete from hits where at < ?", (time.time() - 7 * 86400,))
    conn.commit()


# ------------------------------------------------------------------ items

FRESH_DAYS = 30


def is_fresh(row: sqlite3.Row) -> bool:
    """A community look approved in the last FRESH_DAYS: the gallery shows it under "Newly added"."""
    return (row["source"] != "seed" and row["status"] == "approved"
            and (row["reviewed_at"] or 0) > time.time() - FRESH_DAYS * 86400)


def item_dict(row: sqlite3.Row, admin: bool = False) -> dict:
    d = {
        "id": row["id"], "slug": row["slug"], "type": row["type"], "category": row["category"],
        "name": row["name"], "description": row["description"], "description_en": row["description_en"] or row["description"],
        "author": row["author"],
        "accent": row["accent"], "tags": json.loads(row["tags"] or "[]"), "colors": json.loads(row["colors"] or "[]"),
        "style": row["style"], "downloads": row["downloads"], "featured": bool(row["featured"]),
        "created_at": row["created_at"], "size": row["file_size"], "sha256": row["file_sha"],
        "has_preview": bool(row["preview_sha"]), "has_video": bool(row["video_sha"]),
        "file_name": row["slug"] + (".hlsl" if row["type"] == "skin" else ".json"),
        "fresh": is_fresh(row),
    }
    if admin:
        d.update({"status": row["status"], "source": row["source"], "device": row["device"],
                  "scan_verdict": row["scan_verdict"], "scan_report": json.loads(row["scan_report"] or "{}"),
                  "note": row["note"], "ip_hash": row["ip_hash"], "reviewed_at": row["reviewed_at"]})
    return d


def list_items(status: str = "approved", type_: str | None = None, category: str | None = None,
               query: str | None = None, sort: str = "new", limit: int = 48, offset: int = 0) -> tuple[list[sqlite3.Row], int]:
    where, args = ["status = ?"], [status]
    if type_ in ("skin", "preset"):
        where.append("type = ?")
        args.append(type_)
    if category in ("visualizer", "progress", "led"):
        where.append("category = ?")
        args.append(category)
    if query:
        where.append("(name like ? or author like ? or tags like ? or description like ?)")
        like = f"%{query[:40]}%"
        args += [like, like, like, like]
    # Newest first opens with what the community sent lately (newest approval first), ahead of our own
    # featured looks: seeing your look at the top of the gallery, on the site and in the app, is the thanks.
    fresh = f"(source != 'seed' and coalesce(reviewed_at, 0) > strftime('%s', 'now') - {FRESH_DAYS * 86400})"
    order = {"popular": "featured desc, downloads desc, created_at desc", "name": "name collate nocase",
             "old": "created_at asc"}.get(sort, f"{fresh} desc, case when {fresh} then reviewed_at end desc, "
                                                "featured desc, created_at desc")
    clause = " and ".join(where)
    conn = db()
    total = conn.execute(f"select count(*) from items where {clause}", args).fetchone()[0]
    rows = conn.execute(f"select * from items where {clause} order by {order} limit ? offset ?",
                        args + [limit, offset]).fetchall()
    return rows, total


def get_item(key: str | int, approved_only: bool = True) -> sqlite3.Row | None:
    conn = db()
    if isinstance(key, int) or (isinstance(key, str) and key.isdigit()):
        row = conn.execute("select * from items where id = ?", (int(key),)).fetchone()
    else:
        row = conn.execute("select * from items where slug = ?", (key,)).fetchone()
    if row is None or (approved_only and row["status"] != "approved"):
        return None
    return row


def insert_item(**fields) -> int:
    fields.setdefault("created_at", time.time())
    for key in ("tags", "colors"):
        if isinstance(fields.get(key), list):
            fields[key] = json.dumps(fields[key], ensure_ascii=False)
    if isinstance(fields.get("scan_report"), dict):
        fields["scan_report"] = json.dumps(fields["scan_report"], ensure_ascii=False)
    cols = ", ".join(fields)
    marks = ", ".join("?" for _ in fields)
    conn = db()
    cur = conn.execute(f"insert into items ({cols}) values ({marks})", list(fields.values()))
    conn.commit()
    return cur.lastrowid


def update_item(item_id: int, **fields) -> None:
    if not fields:
        return
    sets = ", ".join(f"{k} = ?" for k in fields)
    conn = db()
    conn.execute(f"update items set {sets} where id = ?", list(fields.values()) + [item_id])
    conn.commit()


def delete_item(item_id: int) -> None:
    conn = db()
    conn.execute("delete from items where id = ?", (item_id,))
    conn.commit()


def count_downloads(item_id: int) -> None:
    conn = db()
    conn.execute("update items set downloads = downloads + 1 where id = ?", (item_id,))
    conn.commit()


def bump(key: str) -> None:
    """One more on a named counter (app downloads from this server, per release)."""
    conn = db()
    conn.execute("insert into counters (key, n) values (?, 1) on conflict(key) do update set n = n + 1", (key,))
    conn.commit()


def counter_sum(prefix: str) -> int:
    return db().execute("select coalesce(sum(n), 0) from counters where key like ?", (prefix + "%",)).fetchone()[0]


def gallery_totals() -> tuple[int, int]:
    """How many approved items, and how many times they were downloaded."""
    row = db().execute("select count(*), coalesce(sum(downloads), 0) from items where status = 'approved'").fetchone()
    return row[0], row[1]


# ------------------------------------------------------------------ the dashboard's numbers

def day_of(at: float | None = None) -> str:
    """The day in Vietnam's time (UTC+7), the admin's day."""
    return time.strftime("%Y-%m-%d", time.gmtime((at or time.time()) + 7 * 3600))


def _tidy(conn: sqlite3.Connection) -> None:
    # Now and then, old rows go: page views after 120 days, the shield's notes after 60.
    if secrets.randbelow(200) == 0:
        conn.execute("delete from views where at < ?", (time.time() - 120 * 86400,))
        conn.execute("delete from sec_events where at < ?", (time.time() - 60 * 86400,))
        conn.execute("delete from bans where until < ?", (time.time() - 7 * 86400,))


def log_view(path: str, lang: str, vn: bool, bot: bool, ref: str, visitor: str) -> None:
    conn = db()
    now = time.time()
    conn.execute("insert into views values (?, ?, ?, ?, ?, ?, ?, ?)",
                 (day_of(now), now, path[:120], lang, int(vn), int(bot), ref[:80], visitor))
    _tidy(conn)
    conn.commit()


def log_download(kind: str, ref: str, vn: bool) -> None:
    conn = db()
    now = time.time()
    conn.execute("insert into dl_log values (?, ?, ?, ?, ?)", (day_of(now), now, kind, ref[:80], int(vn)))
    conn.commit()


def sec_event(ip: str, kind: str, path: str = "", ua: str = "", detail: str = "") -> None:
    conn = db()
    conn.execute("insert into sec_events (at, ip, kind, path, ua, detail) values (?, ?, ?, ?, ?, ?)",
                 (time.time(), ip[:64], kind, path[:200], ua[:200], detail[:200]))
    _tidy(conn)
    conn.commit()


def save_ban(ip: str, seconds: float, reason: str) -> float:
    now = time.time()
    conn = db()
    conn.execute("insert into bans (ip, since, until, reason) values (?, ?, ?, ?) on conflict(ip) do update set "
                 "since = excluded.since, until = excluded.until, reason = excluded.reason, blocked = 0",
                 (ip, now, now + seconds, reason[:200]))
    conn.commit()
    return now + seconds


def lift_ban(ip: str) -> None:
    conn = db()
    conn.execute("update bans set until = ? where ip = ?", (time.time(), ip))
    conn.commit()


def ban_rows(active_only: bool = True) -> list[sqlite3.Row]:
    where = "where until > ?" if active_only else "where until > ? - 86400"
    return db().execute(f"select * from bans {where} order by since desc", (time.time(),)).fetchall()


def was_banned_lately(ip: str) -> bool:
    return db().execute("select 1 from bans where ip = ? and since > ?", (ip, time.time() - 86400)).fetchone() is not None


def add_blocked(counts: dict[str, int]) -> None:
    if not counts:
        return
    conn = db()
    conn.executemany("update bans set blocked = blocked + ? where ip = ?", [(n, ip) for ip, n in counts.items()])
    conn.commit()


def log_refused(ip: str, device: str, source: str, name: str, report: dict) -> None:
    conn = db()
    conn.execute("insert into refused (at, ip_hash, device, source, name, report) values (?, ?, ?, ?, ?, ?)",
                 (time.time(), ip, device, source, name[:80], json.dumps(report, ensure_ascii=False)))
    conn.execute("delete from refused where id not in (select id from refused order by id desc limit 500)")
    conn.commit()


def stats() -> dict:
    conn = db()
    by = {r["status"]: r["n"] for r in conn.execute("select status, count(*) n from items group by status")}
    return {"pending": by.get("pending", 0), "approved": by.get("approved", 0), "rejected": by.get("rejected", 0),
            "hidden": by.get("hidden", 0),
            "downloads": conn.execute("select coalesce(sum(downloads), 0) from items").fetchone()[0],
            "refused": conn.execute("select count(*) from refused").fetchone()[0]}


# ------------------------------------------------------------------ ads

def active_ads() -> list[sqlite3.Row]:
    now = time.time()
    return db().execute(
        "select * from ads where active = 1 and (starts_at is null or starts_at <= ?) and (ends_at is null or ends_at > ?) "
        "order by weight desc, id", (now, now)).fetchall()


def all_ads() -> list[sqlite3.Row]:
    return db().execute("select * from ads order by active desc, id desc").fetchall()


def get_ad(ad_id: int) -> sqlite3.Row | None:
    return db().execute("select * from ads where id = ?", (ad_id,)).fetchone()


def insert_ad(**fields) -> int:
    fields.setdefault("created_at", time.time())
    cols = ", ".join(fields)
    marks = ", ".join("?" for _ in fields)
    conn = db()
    cur = conn.execute(f"insert into ads ({cols}) values ({marks})", list(fields.values()))
    conn.commit()
    return cur.lastrowid


def update_ad(ad_id: int, **fields) -> None:
    sets = ", ".join(f"{k} = ?" for k in fields)
    conn = db()
    conn.execute(f"update ads set {sets} where id = ?", list(fields.values()) + [ad_id])
    conn.commit()


def delete_ad(ad_id: int) -> None:
    conn = db()
    conn.execute("delete from ads where id = ?", (ad_id,))
    conn.commit()


def bump_ad(ad_id: int, column: str) -> None:
    if column not in ("impressions", "clicks"):
        return
    conn = db()
    conn.execute(f"update ads set {column} = {column} + 1 where id = ?", (ad_id,))
    conn.commit()
