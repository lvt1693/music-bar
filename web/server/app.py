"""MusicBar's website: the product page, downloads (mirrored from GitHub), the community gallery with
its review queue, the app's API (gallery, submissions, ads) and an admin area."""

from __future__ import annotations

import json
import math
import os
import re
import time
from pathlib import Path
from urllib.parse import quote, urlencode, urlparse

from fastapi import FastAPI, File, Form, Request, UploadFile
from fastapi.responses import FileResponse, HTMLResponse, JSONResponse, PlainTextResponse, RedirectResponse, Response
from fastapi.staticfiles import StaticFiles
from fastapi.templating import Jinja2Templates
from markupsafe import Markup, escape

from . import auth, geo, releases, scanner, store
from .i18n import t as translate

HERE = Path(__file__).resolve().parent
app = FastAPI(docs_url=None, redoc_url=None, openapi_url=None)
app.mount("/static", StaticFiles(directory=HERE / "static"), name="static")
templates = Jinja2Templates(directory=HERE / "templates")

def markdown(text: str) -> Markup:
    """Release notes as HTML: escaped first, then bold lines as headings, bullets, **bold**, `code`, links."""
    out, in_list = [], False
    for raw in (text or "").replace("\r", "").split("\n"):
        line = escape(raw.rstrip()).__str__()
        bullet = re.match(r"^\s*[-*]\s+(.*)", line)
        if bullet:
            if not in_list:
                out.append("<ul>")
                in_list = True
            line = "<li>" + bullet.group(1) + "</li>"
        else:
            if in_list:
                out.append("</ul>")
                in_list = False
            heading = re.fullmatch(r"\*\*(.+?)\*\*:?|#{1,4}\s*(.+)", line.strip())
            if heading:
                line = "<h3>" + (heading.group(1) or heading.group(2)) + "</h3>"
            elif line.strip():
                line = "<p>" + line + "</p>"
        line = re.sub(r"\*\*(.+?)\*\*", r"<strong>\1</strong>", line)
        line = re.sub(r"`([^`]+)`", r"<code>\1</code>", line)
        line = re.sub(r"\[([^\]]+)\]\((https://[^)\s\"<>]+)\)", r'<a href="\2" rel="nofollow noopener">\1</a>', line)
        out.append(line)
    if in_list:
        out.append("</ul>")
    return Markup("\n".join(out))


templates.env.filters["md"] = markdown

GITHUB = "https://github.com/lvt1693/music-bar"
MEDIA_DIR = HERE / "static" / "media"
CATEGORIES = ("visualizer", "progress", "led")
ACCENTS = {"visualizer": "#47A9FF", "progress": "#FF5F6D", "led": "#4CFFB0"}
PAGE_SIZE = 24

# Inline style attributes carry the cards' accent colors (always a checked #RRGGBB); scripts stay 'self' only.
CSP = ("default-src 'self'; img-src 'self' data: blob:; media-src 'self'; font-src 'self'; style-src 'self' 'unsafe-inline'; "
       "script-src 'self'; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'; object-src 'none'")


@app.on_event("startup")
def _startup() -> None:
    store.db()
    # MUSICBAR_NO_SYNC=1 when trying the site out locally: no mirror of the releases.
    if os.environ.get("MUSICBAR_NO_SYNC") != "1":
        releases.start_background()
        geo.start_background()


@app.middleware("http")
async def _headers(request: Request, call_next):
    response = await call_next(request)
    response.headers["X-Content-Type-Options"] = "nosniff"
    response.headers["Referrer-Policy"] = "strict-origin-when-cross-origin"
    response.headers["X-Frame-Options"] = "DENY"
    response.headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), interest-cohort=()"
    response.headers.setdefault("Content-Security-Policy", CSP)
    if request.url.path.startswith("/static/"):
        response.headers["Cache-Control"] = "public, max-age=86400"
    return response


# ------------------------------------------------------------------ helpers

def client_ip(request: Request) -> str:
    peer = request.client.host if request.client else ""
    forwarded = request.headers.get("x-forwarded-for", "")
    if peer in ("127.0.0.1", "::1") and forwarded:
        return forwarded.split(",")[-1].strip()
    return peer


LINK_PREVIEWS = ("facebookexternalhit", "facebot", "zalo")


def lang_of(request: Request) -> str:
    """The visitor's choice if they made one; otherwise Vietnamese for people in Vietnam or whose browser
    asks for Vietnamese first (and for the link previews of Facebook and Zalo, where the site is
    shared), English for everyone else."""
    wanted = request.query_params.get("lang") or request.cookies.get("lang")
    if wanted in ("vi", "en"):
        return wanted
    first = request.headers.get("accept-language", "").split(",")[0].split(";")[0].strip().lower()
    if first.startswith("vi"):
        return "vi"
    agent = request.headers.get("user-agent", "").lower()
    if any(bot in agent for bot in LINK_PREVIEWS):
        return "vi"
    return "vi" if geo.is_vietnam(client_ip(request)) else "en"


def is_admin(request: Request) -> dict | None:
    return auth.read_session(request.cookies.get(auth.COOKIE))


def page(request: Request, name: str, http_status: int = 200, **context) -> HTMLResponse:
    lang = lang_of(request)
    query = dict(request.query_params)
    lang_url = lambda code: request.url.path + "?" + urlencode(dict(query, lang=code))
    context.update(
        request=request, lang=lang, github=GITHUB, release=releases.latest(),
        t=lambda key, **v: translate(key, lang, **v), lang_url=lang_url, other_lang_url=lang_url("en" if lang == "vi" else "vi"),
        admin=is_admin(request), path=request.url.path, categories=CATEGORIES,
        num=lambda n: f"{int(n or 0):,}".replace(",", "." if lang == "vi" else ","),
        # The Start menu's recommendations and the notification center: the newest looks.
        recent=[item_urls(store.item_dict(r)) for r in store.list_items(sort="new", limit=3)[0]],
        film=lambda name: (MEDIA_DIR / name).is_file(),
        sponsor=releases.sponsor_url(),
        # A film's address carries its date, so a new render isn't hidden behind a day of browser cache.
        media=lambda name: f"/static/media/{name}?v={int((MEDIA_DIR / name).stat().st_mtime) if (MEDIA_DIR / name).is_file() else 0}",
    )
    response = templates.TemplateResponse(request, name, context, status_code=http_status)
    if request.query_params.get("lang") in ("vi", "en"):
        response.set_cookie("lang", lang, max_age=365 * 86400, samesite="lax")
    # The same address answers in two languages: caches must not hand one person's page to another.
    response.headers["Vary"] = "Accept-Language, Cookie"
    return response


def item_urls(d: dict) -> dict:
    d["page_url"] = f"/gallery/{d['slug']}"
    d["download_url"] = f"/files/item/{d['id']}/download"
    d["preview_url"] = f"/files/item/{d['id']}/preview" if d["has_preview"] else None
    d["video_url"] = f"/files/item/{d['id']}/video" if d["has_video"] else None
    return d


def blob_response(sha: str | None, media: str, download_name: str | None = None) -> Response:
    path = store.blob_path(sha or "")
    if path is None:
        return PlainTextResponse("not found", status_code=404)
    headers = {"Cache-Control": "public, max-age=3600", "Content-Security-Policy": "default-src 'none'; sandbox"}
    if download_name:
        headers["Content-Disposition"] = f"attachment; filename=\"{download_name}\"; filename*=UTF-8''{quote(download_name)}"
    return FileResponse(path, media_type=media, headers=headers)


MEDIA = {"png": "image/png", "jpeg": "image/jpeg", "webp": "image/webp", "mp4": "video/mp4", "webm": "video/webm"}


def scan_video(data: bytes) -> tuple[str | None, str]:
    """A preview clip (admin uploads only): MP4 or WebM by its header, 8 MB at most."""
    if len(data) > 8 * 1024 * 1024:
        return None, "Video is larger than 8 MB"
    if len(data) > 12 and data[4:8] == b"ftyp":
        return "mp4", ""
    if data.startswith(b"\x1a\x45\xdf\xa3"):
        return "webm", ""
    return None, "The clip must be MP4 or WebM"


# ------------------------------------------------------------------ pages

@app.get("/", response_class=HTMLResponse)
def home(request: Request):
    rows, _ = store.list_items(sort="popular", limit=8)
    total, item_downloads = store.gallery_totals()
    return page(request, "home.html", items=[item_urls(store.item_dict(r)) for r in rows], total=total,
                item_downloads=item_downloads, app_downloads=releases.downloads())


@app.get("/download", response_class=HTMLResponse)
def download_page(request: Request):
    return page(request, "download.html", releases=releases.releases(), app_downloads=releases.downloads())


@app.get("/download/{tag}/{name}")
def download_file(request: Request, tag: str, name: str):
    path, asset = releases.file_path(tag, name)
    if path is None or not path.is_file():
        return RedirectResponse(f"{GITHUB}/releases", status_code=302)
    # One download a person per release every 10 minutes: a resumed or retried download isn't a new one.
    ip = store.ip_hash(client_ip(request))
    if store.count_hits(ip, f"app{tag}", 600) < 1:
        store.add_hit(ip, f"app{tag}")
        store.bump(f"app:{tag}")
    return FileResponse(path, media_type="application/octet-stream", filename=name,
                        headers={"Cache-Control": "public, max-age=86400"})


@app.get("/gallery", response_class=HTMLResponse)
def gallery(request: Request, category: str = "", type: str = "", sort: str = "new", q: str = "", p: int = 1):
    p = max(1, min(p, 500))
    query = scanner.clean_text(q, 40)
    rows, total = store.list_items(type_=type, category=category, query=query, sort=sort,
                                   limit=PAGE_SIZE, offset=(p - 1) * PAGE_SIZE)
    all_items, item_downloads = store.gallery_totals()
    return page(request, "gallery.html", items=[item_urls(store.item_dict(r)) for r in rows], total=total,
                all_items=all_items, item_downloads=item_downloads,
                category=category if category in CATEGORIES else "", type=type, sort=sort, q=query, p=p,
                pages=max(1, math.ceil(total / PAGE_SIZE)))


@app.get("/gallery/{key}", response_class=HTMLResponse)
def item_page(request: Request, key: str):
    row = store.get_item(key, approved_only=not is_admin(request))
    if row is None:
        return page(request, "notfound.html", http_status=404)
    d = item_urls(store.item_dict(row))
    source = ""
    path = store.blob_path(row["file_sha"])
    if path:
        source = path.read_text(encoding="utf-8", errors="replace")
    return page(request, "item.html", item=d, source=source)


@app.get("/submit", response_class=HTMLResponse)
def submit_page(request: Request):
    return page(request, "submit.html")


@app.post("/submit", response_class=HTMLResponse)
async def submit_form(request: Request, name: str = Form(""), nickname: str = Form(""), description: str = Form(""),
                      category: str = Form(""), agree: str = Form(""), file: UploadFile = File(...),
                      preview: UploadFile | None = File(None)):
    data = await file.read(scanner.SKIN_MAX + 1)
    picture = await preview.read(scanner.PREVIEW_MAX + 1) if preview is not None and preview.filename else b""
    result = submit(request, source="web", name=name, nickname=nickname, description=description, category=category,
                    file_name=file.filename or "", data=data, preview=picture, device="", agreed=agree == "on")
    return page(request, "submitted.html", http_status=200 if result["ok"] else 422, result=result)


@app.get("/files/item/{item_id}/{what}")
def item_file(request: Request, item_id: int, what: str):
    row = store.get_item(item_id, approved_only=not is_admin(request))
    if row is None:
        return PlainTextResponse("not found", status_code=404)
    if what == "preview":
        return blob_response(row["preview_sha"], MEDIA.get(row["preview_type"] or "", "application/octet-stream"))
    if what == "video":
        clip = store.blob_path(row["video_sha"] or "")
        if clip is None:
            return PlainTextResponse("not found", status_code=404)
        with open(clip, "rb") as f:
            kind = "webm" if f.read(4) == b"\x1a\x45\xdf\xa3" else "mp4"
        return blob_response(row["video_sha"], MEDIA[kind])
    if what == "download":
        # The app fetches skins to draw their previews (?preview=1): those aren't downloads. Nor is the
        # same person fetching the same item again within 10 minutes.
        if row["status"] == "approved" and request.query_params.get("preview") != "1":
            ip = store.ip_hash(client_ip(request))
            if store.count_hits(ip, f"dl{row['id']}", 600) < 1:
                store.add_hit(ip, f"dl{row['id']}")
                store.count_downloads(row["id"])
        name = row["slug"] + (".hlsl" if row["type"] == "skin" else ".json")
        media = "text/plain; charset=utf-8" if row["type"] == "skin" else "application/json"
        return blob_response(row["file_sha"], media, name)
    return PlainTextResponse("not found", status_code=404)


@app.get("/files/ad/{ad_id}")
def ad_image(ad_id: int):
    ad = store.get_ad(ad_id)
    if ad is None:
        return PlainTextResponse("not found", status_code=404)
    return blob_response(ad["image_sha"], MEDIA.get(ad["image_type"], "application/octet-stream"))


@app.get("/go/{ad_id}")
def ad_click(request: Request, ad_id: int):
    ad = store.get_ad(ad_id)
    if ad is None:
        return RedirectResponse("/", status_code=302)
    ip = store.ip_hash(client_ip(request))
    if store.count_hits(ip, f"click{ad_id}", 600) < 3:
        store.add_hit(ip, f"click{ad_id}")
        store.bump_ad(ad_id, "clicks")
    return RedirectResponse(ad["link"], status_code=302)


@app.get("/healthz")
def healthz():
    return {"ok": True}


# ------------------------------------------------------------------ submissions (web and app)

def submit(request: Request, *, source: str, name: str, nickname: str, description: str, category: str,
           file_name: str, data: bytes, preview: bytes, device: str, agreed: bool) -> dict:
    ip = store.ip_hash(client_ip(request))
    if store.count_hits(ip, "submit", 3600) >= 8 or store.count_hits(ip, "submit", 86400) >= 30:
        return {"ok": False, "rate": True, "findings": [{"level": "block", "message": "rate limit", "where": ""}]}
    store.add_hit(ip, "submit")
    if store.db().execute("select count(*) from items where status = 'pending'").fetchone()[0] >= 500:
        return {"ok": False, "findings": [{"level": "block", "message": "The review queue is full, try again later", "where": ""}]}

    report = scanner.Report()
    lower = file_name.lower()
    kind = "preset" if lower.endswith(".json") or (not lower.endswith((".hlsl", ".fx", ".txt")) and data.lstrip()[:1] == b"{") else "skin"
    file_report = scanner.scan_preset(data) if kind == "preset" else scanner.scan_skin(data)
    for f in file_report.findings:
        report.add(f["level"], f["message"], f["where"])
    if source == "web" and not agreed:
        report.add("block", "Please confirm you have the right to share it")
    meta = file_report.meta
    nick = scanner.check_text(report, "Nickname", nickname, 2, 24, nickname=True)
    title = scanner.check_text(report, "Name", name or meta.get("name", ""), 2, 40)
    about = scanner.check_text(report, "Description", description or meta.get("description", ""), 0, 300) if (description or meta.get("description")) else ""
    device = re.sub(r"[^A-Za-z0-9-]", "", device or "")[:40]

    preview_report = None
    if preview:
        preview_report = scanner.scan_preview(preview)
        for f in preview_report.findings:
            report.add(f["level"], "Preview: " + f["message"], f["where"])

    if kind == "preset":
        cat = meta.get("kind", "visualizer")
    else:
        cat = category if category in CATEGORIES else (meta.get("kind") if meta.get("kind") in CATEGORIES else "visualizer")

    if report.verdict == "block":
        store.log_refused(ip, device, source, title or file_name, report.to_dict())
        return {"ok": False, "findings": report.findings}

    file_sha = store.put_blob(file_report.clean_bytes)
    fields = dict(slug=store.slugify(title), type=kind, category=cat, name=title, description=about, author=nick,
                  device=device, source=source, status="pending", file_sha=file_sha, file_size=len(file_report.clean_bytes),
                  accent=(meta.get("colors") or [ACCENTS[cat]])[0], colors=meta.get("colors", []), style=meta.get("style", ""),
                  scan_verdict=report.verdict, scan_report=report.to_dict(), ip_hash=ip)
    if preview_report is not None and preview_report.clean_bytes:
        fields["preview_sha"] = store.put_blob(preview_report.clean_bytes)
        fields["preview_type"] = preview_report.meta["type"]
    item_id = store.insert_item(**fields)
    return {"ok": True, "id": item_id, "name": title, "verdict": report.verdict, "findings": report.findings}


# ------------------------------------------------------------------ the app's API

@app.get("/api/v1/gallery")
def api_gallery(category: str = "", type: str = "", sort: str = "new", q: str = "", page: int = 1, size: int = 30):
    size = max(1, min(size, 60))
    page = max(1, min(page, 500))
    rows, total = store.list_items(type_=type, category=category, query=scanner.clean_text(q, 40), sort=sort,
                                   limit=size, offset=(page - 1) * size)
    return {"total": total, "page": page, "size": size, "items": [item_urls(store.item_dict(r)) for r in rows]}


@app.get("/api/v1/items/{key}")
def api_item(key: str):
    row = store.get_item(key)
    if row is None:
        return JSONResponse({"error": "not found"}, status_code=404)
    return item_urls(store.item_dict(row))


@app.post("/api/v1/submit")
async def api_submit(request: Request, name: str = Form(""), nickname: str = Form(""), description: str = Form(""),
                     category: str = Form(""), device: str = Form(""), file: UploadFile = File(...),
                     preview: UploadFile | None = File(None)):
    data = await file.read(scanner.SKIN_MAX + 1)
    picture = await preview.read(scanner.PREVIEW_MAX + 1) if preview is not None and preview.filename else b""
    result = submit(request, source="app", name=name, nickname=nickname, description=description, category=category,
                    file_name=file.filename or "", data=data, preview=picture, device=device, agreed=True)
    status = 200 if result["ok"] else (429 if result.get("rate") else 422)
    return JSONResponse(result, status_code=status)


@app.get("/api/v1/ads")
def api_ads():
    # Links relative to the site: the app puts its own address in front (behind HAProxy this server can't tell it).
    ads = [{"id": a["id"], "title": a["title"], "alt": a["alt"], "width": a["width"], "height": a["height"],
            "image_url": f"/files/ad/{a['id']}?v={int(a['created_at'])}", "click_url": f"/go/{a['id']}",
            "weight": a["weight"]} for a in store.active_ads()]
    return {"rotate_seconds": 8, "ads": ads}


@app.post("/api/v1/ads/{ad_id}/seen")
def api_ad_seen(request: Request, ad_id: int):
    ip = store.ip_hash(client_ip(request))
    if store.get_ad(ad_id) and store.count_hits(ip, f"seen{ad_id}", 3600) < 1:
        store.add_hit(ip, f"seen{ad_id}")
        store.bump_ad(ad_id, "impressions")
    return {"ok": True}


@app.get("/api/v1/release")
def api_release():
    rel = releases.latest()
    if rel is None:
        return JSONResponse({"error": "not mirrored yet"}, status_code=503)
    return dict(rel, downloads=releases.downloads())


# ------------------------------------------------------------------ admin

def admin_guard(request: Request, csrf: str | None = None) -> dict | None:
    session = is_admin(request)
    if session is None:
        return None
    if csrf is not None and csrf != session.get("csrf"):
        return None
    return session


@app.get("/admin", response_class=HTMLResponse)
def admin_home(request: Request, status: str = "pending"):
    session = is_admin(request)
    if session is None:
        return page(request, "admin_login.html")
    if status not in ("pending", "approved", "rejected", "hidden"):
        status = "pending"
    rows, total = store.list_items(status=status, sort="old" if status == "pending" else "new", limit=200)
    refused = store.db().execute("select * from refused order by id desc limit 30").fetchall()
    return page(request, "admin.html", session=session, status=status, total=total, stats=store.stats(),
                items=[item_urls(store.item_dict(r, admin=True)) for r in rows],
                refused=[dict(r, report=json.loads(r["report"])) for r in refused], mirror_error=releases.last_error())


@app.post("/admin/login")
def admin_login(request: Request, password: str = Form("")):
    ip = store.ip_hash(client_ip(request))
    if store.count_hits(ip, "login", 900) >= 8:
        return page(request, "admin_login.html", http_status=429, error="Too many attempts, wait 15 minutes")
    store.add_hit(ip, "login")
    if not auth.check_password(password):
        time.sleep(1)
        return page(request, "admin_login.html", http_status=401, error="Wrong password")
    response = RedirectResponse("/admin", status_code=303)
    secure = request.headers.get("x-forwarded-proto") == "https"
    response.set_cookie(auth.COOKIE, auth.new_session(), max_age=auth.SESSION_SECONDS, httponly=True,
                        samesite="strict", secure=secure, path="/")
    return response


@app.post("/admin/logout")
def admin_logout():
    response = RedirectResponse("/", status_code=303)
    response.delete_cookie(auth.COOKIE, path="/")
    return response


@app.get("/admin/item/{item_id}", response_class=HTMLResponse)
def admin_item(request: Request, item_id: int):
    session = is_admin(request)
    if session is None:
        return RedirectResponse("/admin", status_code=303)
    row = store.get_item(item_id, approved_only=False)
    if row is None:
        return page(request, "notfound.html", http_status=404)
    path = store.blob_path(row["file_sha"])
    source = path.read_text(encoding="utf-8", errors="replace") if path else ""
    return page(request, "admin_item.html", session=session, item=item_urls(store.item_dict(row, admin=True)), source=source)


@app.post("/admin/item/{item_id}")
async def admin_item_post(request: Request, item_id: int, csrf: str = Form(""), action: str = Form(""),
                          note: str = Form(""), name: str = Form(""), description: str = Form(""),
                          category: str = Form(""), accent: str = Form(""), tags: str = Form(""),
                          media: UploadFile | None = File(None)):
    if admin_guard(request, csrf) is None:
        return RedirectResponse("/admin", status_code=303)
    row = store.get_item(item_id, approved_only=False)
    if row is None:
        return RedirectResponse("/admin", status_code=303)
    now = time.time()
    if action == "approve":
        store.update_item(item_id, status="approved", reviewed_at=now, note=scanner.clean_text(note, 300))
    elif action == "reject":
        store.update_item(item_id, status="rejected", reviewed_at=now, note=scanner.clean_text(note, 300))
    elif action == "hide":
        store.update_item(item_id, status="hidden", reviewed_at=now)
    elif action in ("feature", "unfeature"):
        store.update_item(item_id, featured=1 if action == "feature" else 0)
    elif action == "delete":
        store.delete_item(item_id)
        return RedirectResponse("/admin", status_code=303)
    elif action == "edit":
        fields = {}
        if name:
            fields["name"] = scanner.clean_text(name, 40)
        fields["description"] = scanner.clean_text(description, 300)
        if category in CATEGORIES:
            fields["category"] = category
        if re.fullmatch(r"#[0-9A-Fa-f]{6}", accent or ""):
            fields["accent"] = accent.upper()
        fields["tags"] = json.dumps([scanner.clean_text(x, 20) for x in tags.split(",") if x.strip()][:8], ensure_ascii=False)
        store.update_item(item_id, **fields)
    elif action == "media" and media is not None and media.filename:
        data = await media.read(8 * 1024 * 1024 + 1)
        video, error = scan_video(data)
        if video:
            store.update_item(item_id, video_sha=store.put_blob(data))
        else:
            pic = scanner.scan_preview(data)
            if pic.verdict != "block":
                store.update_item(item_id, preview_sha=store.put_blob(pic.clean_bytes), preview_type=pic.meta["type"])
    return RedirectResponse(f"/admin/item/{item_id}", status_code=303)


@app.get("/admin/ads", response_class=HTMLResponse)
def admin_ads(request: Request):
    session = is_admin(request)
    if session is None:
        return RedirectResponse("/admin", status_code=303)
    return page(request, "admin_ads.html", session=session, ads=[dict(a) for a in store.all_ads()])


@app.post("/admin/ads")
async def admin_ads_post(request: Request, csrf: str = Form(""), action: str = Form("create"), ad_id: int = Form(0),
                         title: str = Form(""), link: str = Form(""), alt: str = Form(""), weight: int = Form(1),
                         image: UploadFile | None = File(None)):
    if admin_guard(request, csrf) is None:
        return RedirectResponse("/admin", status_code=303)
    if action == "create" and image is not None and image.filename:
        pic = scanner.scan_preview(await image.read(scanner.PREVIEW_MAX + 1))
        url = urlparse(link.strip())
        if pic.verdict != "block" and url.scheme == "https" and url.netloc and len(link) <= 600 and " " not in link:
            store.insert_ad(title=scanner.clean_text(title, 80) or url.netloc, image_sha=store.put_blob(pic.clean_bytes),
                            image_type=pic.meta["type"], width=pic.meta["width"], height=pic.meta["height"],
                            link=link.strip(), alt=scanner.clean_text(alt, 160), weight=max(1, min(weight, 10)))
    elif action in ("on", "off") and ad_id:
        store.update_ad(ad_id, active=1 if action == "on" else 0)
    elif action == "delete" and ad_id:
        store.delete_ad(ad_id)
    return RedirectResponse("/admin/ads", status_code=303)


@app.post("/admin/sync")
def admin_sync(request: Request, csrf: str = Form("")):
    if admin_guard(request, csrf) is None:
        return RedirectResponse("/admin", status_code=303)
    releases.sync()
    return RedirectResponse("/admin", status_code=303)


@app.exception_handler(404)
async def not_found(request: Request, exc):
    if request.url.path.startswith(("/api/", "/files/")):
        return JSONResponse({"error": "not found"}, status_code=404)
    return page(request, "notfound.html", http_status=404)
