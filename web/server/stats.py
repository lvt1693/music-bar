"""The numbers on the admin's dashboard and security page, read from the database in one place."""

from __future__ import annotations

import time

from . import releases, store

KINDS = {
    "probe": "Dò file nhạy cảm", "notfound": "Đoán đường dẫn", "payload": "Chèn mã tấn công", "tool": "Công cụ hack",
    "flood": "Gửi dồn dập", "downloads": "Tải dồn dập", "ban": "Bị khóa", "unban": "Được mở khóa",
    "login_fail": "Đăng nhập sai", "login_fail_strike": "Đăng nhập sai (tính điểm)", "login_locked": "Bị chặn đăng nhập",
    "login_ok": "Đăng nhập", "honeypot": "Bot điền form", "form": "Form gửi bất thường",
}
DANGER = {"probe", "payload", "tool", "flood", "downloads", "ban", "login_fail", "login_locked", "honeypot", "form", "notfound"}


def _days(days: int) -> list[str]:
    now = time.time()
    return [store.day_of(now - i * 86400) for i in reversed(range(days))]


def _change(now: int, before: int) -> int | None:
    return None if not before else round((now - before) * 100 / before)


def dashboard(days: int) -> dict:
    conn = store.db()
    labels = _days(days)
    first = labels[0]
    prev_first = store.day_of(time.time() - (2 * days - 1) * 86400)
    period_start = time.time() - days * 86400

    # Visits, people only (crawlers apart).
    by_day = {r["day"]: r for r in conn.execute(
        "select day, count(*) n, count(distinct vh) u from views where day >= ? and bot = 0 group by day", (first,))}
    views = [by_day[d]["n"] if d in by_day else 0 for d in labels]
    visitors = [by_day[d]["u"] if d in by_day else 0 for d in labels]
    total = conn.execute("select count(*) n, count(distinct vh) u, coalesce(sum(vn), 0) vn from views "
                         "where day >= ? and bot = 0", (first,)).fetchone()
    before = conn.execute("select count(*) n, count(distinct vh) u from views where day >= ? and day < ? and bot = 0",
                          (prev_first, first)).fetchone()
    bots = conn.execute("select count(*) from views where day >= ? and bot = 1", (first,)).fetchone()[0]
    pages = conn.execute("select path, count(*) n from views where day >= ? and bot = 0 group by path order by n desc limit 8",
                         (first,)).fetchall()
    refs = conn.execute("select ref, count(*) n from views where day >= ? and bot = 0 and ref != '' group by ref "
                        "order by n desc limit 8", (first,)).fetchall()
    langs = {r["lang"]: r["n"] for r in conn.execute(
        "select lang, count(*) n from views where day >= ? and bot = 0 group by lang", (first,))}

    # Downloads: the app from this server, looks from the gallery, Apply clicks on the website.
    dl = {(r["day"], r["kind"]): r["n"] for r in conn.execute(
        "select day, kind, count(*) n from dl_log where day >= ? group by day, kind", (first,))}
    app_dl = [dl.get((d, "app"), 0) for d in labels]
    item_dl = [dl.get((d, "item"), 0) for d in labels]
    applies = [dl.get((d, "apply"), 0) for d in labels]
    dl_before = {r["kind"]: r["n"] for r in conn.execute(
        "select kind, count(*) n from dl_log where day >= ? and day < ? group by kind", (prev_first, first))}
    names = {str(r["id"]): r for r in conn.execute("select id, slug, name, author, downloads from items")}
    slugs = {r["slug"]: r for r in names.values()}
    top_items = [dict(name=names[r["ref"]]["name"], author=names[r["ref"]]["author"], slug=names[r["ref"]]["slug"],
                      n=r["n"], total=names[r["ref"]]["downloads"])
                 for r in conn.execute("select ref, count(*) n from dl_log where kind = 'item' and day >= ? group by ref "
                                       "order by n desc limit 8", (first,)) if r["ref"] in names]
    top_applies = [dict(name=slugs[r["ref"]]["name"] if r["ref"] in slugs else r["ref"], n=r["n"])
                   for r in conn.execute("select ref, count(*) n from dl_log where kind = 'apply' and day >= ? group by ref "
                                         "order by n desc limit 6", (first,))]
    app_tags = conn.execute("select ref, count(*) n from dl_log where kind = 'app' and day >= ? group by ref order by ref desc",
                            (first,)).fetchall()

    # The gallery's queue over the period.
    queue = conn.execute(
        "select sum(status = 'pending') pending, sum(created_at >= ?) sent, sum(status = 'approved' and reviewed_at >= ?) approved, "
        "sum(status = 'rejected' and reviewed_at >= ?) rejected, sum(status = 'approved') live, "
        "coalesce(sum(case when status = 'approved' then downloads end), 0) downloads from items",
        (period_start, period_start, period_start)).fetchone()
    refused = conn.execute("select count(*) from refused where at >= ?", (period_start,)).fetchone()[0]
    ads = [dict(a, ctr=(a["clicks"] * 100 / a["impressions"]) if a["impressions"] else 0) for a in store.all_ads()]

    # The shield over the period.
    sec_kinds = {r["kind"]: r["n"] for r in conn.execute(
        "select kind, count(*) n from sec_events where at >= ? group by kind", (period_start,))}
    blocked = conn.execute("select coalesce(sum(blocked), 0) from bans where since >= ?", (period_start,)).fetchone()[0]
    return {
        "labels": labels,
        "views": views, "visitors": visitors,
        "total_views": total["n"], "total_visitors": total["u"], "bots": bots,
        "vn_share": round(total["vn"] * 100 / total["n"]) if total["n"] else 0,
        "views_change": _change(total["n"], before["n"]), "visitors_change": _change(total["u"], before["u"]),
        "pages": pages, "refs": refs,
        "lang_vi": langs.get("vi", 0), "lang_en": langs.get("en", 0),
        "app_dl": app_dl, "item_dl": item_dl, "applies": applies,
        "total_app_dl": sum(app_dl), "total_item_dl": sum(item_dl), "total_applies": sum(applies),
        "app_change": _change(sum(app_dl), dl_before.get("app", 0)),
        "item_change": _change(sum(item_dl), dl_before.get("item", 0)),
        "app_all_time": releases.downloads(), "app_tags": app_tags,
        "top_items": top_items, "top_applies": top_applies,
        "queue": dict(queue), "refused": refused, "ads": ads,
        "threats": sum(n for k, n in sec_kinds.items() if k in DANGER), "blocked": blocked,
        "bans_now": len(store.ban_rows()), "sec_kinds": sec_kinds,
        "offenders": offenders(period_start, 6), "logins": logins(6), "kinds": KINDS,
    }


def offenders(since: float, limit: int) -> list[dict]:
    conn = store.db()
    rows = conn.execute(
        "select ip, count(*) n, max(at) last, min(at) first, group_concat(distinct kind) kinds from sec_events "
        "where at >= ? and kind not in ('login_ok', 'unban') group by ip order by n desc limit ?", (since, limit)).fetchall()
    bans = {b["ip"]: b for b in store.ban_rows(active_only=False)}
    out = []
    for r in rows:
        ban = bans.get(r["ip"])
        sample = conn.execute("select path, ua from sec_events where ip = ? and path != '' order by at desc limit 3",
                              (r["ip"],)).fetchall()
        out.append(dict(ip=r["ip"], n=r["n"], first=r["first"], last=r["last"],
                        kinds=[KINDS.get(k, k) for k in (r["kinds"] or "").split(",") if k],
                        banned=bool(ban and ban["until"] > time.time()), blocked=ban["blocked"] if ban else 0,
                        paths=[s["path"] for s in sample], ua=sample[0]["ua"] if sample else ""))
    return out


def logins(limit: int) -> list:
    return store.db().execute("select * from sec_events where kind in ('login_ok', 'login_fail', 'login_locked') "
                              "order by at desc limit ?", (limit,)).fetchall()


def security(kind: str = "", ip: str = "") -> dict:
    conn = store.db()
    where, args = ["1 = 1"], []
    if kind in KINDS:
        where.append("kind = ?")
        args.append(kind)
    if ip:
        where.append("ip = ?")
        args.append(ip[:64])
    events = conn.execute(f"select * from sec_events where {' and '.join(where)} order by at desc limit 200", args).fetchall()
    day = time.time() - 86400
    return {
        "events": events, "kinds": KINDS, "danger": DANGER,
        "bans": store.ban_rows(), "recent_bans": [b for b in store.ban_rows(active_only=False) if b["until"] <= time.time()],
        "offenders": offenders(time.time() - 7 * 86400, 20),
        "logins": logins(20),
        "last_day": {r["kind"]: r["n"] for r in conn.execute(
            "select kind, count(*) n from sec_events where at >= ? group by kind", (day,))},
    }
