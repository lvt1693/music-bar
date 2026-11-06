"""The site's shield: what it turns away before a request reaches a page.

Scanners hunting for leaked files (/.env, /.git, wp-config.php...), known attack tools, injection
attempts in the address, floods. Each bad request earns its address strikes; five within ten minutes
and the address is shut out for an hour (a day if it was shut out in the last day). Everything it does
is written down for the admin's dashboard. One worker process serves the site, so the counting lives
in memory; bans are also kept in the database and come back after a restart.
"""

from __future__ import annotations

import re
import threading
import time
from collections import defaultdict, deque
from urllib.parse import unquote

from . import store

# The site's own addresses: never a probe.
SITE = ("/static/", "/files/", "/download/", "/api/v1/", "/gallery", "/admin", "/go/", "/submit")
# ... and what browsers, search engines and phones ask for by themselves: never a guess.
OWN = {"/", "/robots.txt", "/favicon.ico", "/healthz", "/download", "/sitemap.xml", "/ads.txt",
       "/apple-touch-icon.png", "/apple-touch-icon-precomposed.png", "/manifest.json", "/site.webmanifest",
       "/browserconfig.xml"}

# What scanners look for: files that leak secrets, other software's admin pages, server internals.
PROBE = re.compile(
    r"(?i)(/\.(env|git|svn|hg|aws|ssh|docker|vscode|idea|htaccess|htpasswd|ds_store)|"
    r"wp-(admin|login|config|content|includes|json)|xmlrpc\.php|phpmyadmin|/pma\b|\.php\d?(/|$)|"
    r"\.(aspx?|jsp|cgi|env|sql|bak|old|swp|save|orig|log|ini|ya?ml|conf|config|key|pem|crt|p12|db|sqlite3?|tar|gz|zip|rar|7z)$|"
    r"cgi-bin|/vendor/|/actuator|/server-status|/server-info|/telescope|/_profiler|/debug/|/console|/solr|"
    r"/boaform|/hnap1|/owa/|/autodiscover|/ecp/|/remote/|/manager/html|/jenkins|/druid|/nacos|"
    r"(secrets|credentials|config|settings|appsettings|database)\.json|docker-compose|dockerfile|/var/task/|"
    r"(nuxt|next|vite|webpack)\.config|package(-lock)?\.json|yarn\.lock|composer\.(json|lock)|web\.config|"
    r"/v2/_catalog|trace\.axd|elmah|/etc/passwd|/proc/self|/(login|signin|user/login|administrator)/?$)"
)
# Attempts to break in through the address itself.
PAYLOAD = re.compile(
    r"(?i)(union(\s|\+|/\*.*\*/)+(all(\s|\+)+)?select|select(\s|\+).+(\s|\+)from(\s|\+)|information_schema|"
    r"sleep\(\s*\d|benchmark\(|waitfor(\s|\+)delay|<script|javascript:|onerror\s*=|onload\s*=|"
    r"\.\./|\.\.\\|/etc/passwd|\$\{jndi:|\$\{\{|base64_decode|eval\(|exec\(|system\(|cmd\.exe|/bin/(ba)?sh)"
)
# Tools that only attack.
ATTACK_TOOL = re.compile(
    r"(?i)(sqlmap|nikto|nmap|masscan|zgrab|nuclei|dirbuster|dirb/|gobuster|ffuf|feroxbuster|wpscan|acunetix|"
    r"nessus|openvas|netsparker|jorgee|zmeu|fuzz|havij|hydra|medusa|whatweb|wfuzz|commix|xsstrike)"
)
# Crawlers and scripts: counted apart from people on the dashboard, never blocked for being one.
BOT = re.compile(r"(?i)(bot|crawl|spider|slurp|facebookexternalhit|zalo|preview|monitor|curl|wget|python|"
                 r"go-http|java/|okhttp|headless|scrapy|httpclient|axios|node-fetch)")

STRIKES_TO_BAN = 5
WINDOW = 600
BAN = 3600
BAN_AGAIN = 86400
PAGE_LIMIT = 120          # requests a minute to pages and the API (not static files, clips, pictures)
DOWNLOAD_LIMIT = 6        # app downloads in ten minutes
TRUSTED = {"127.0.0.1", "::1", "42.96.40.15"}


class Shield:
    def __init__(self) -> None:
        self._lock = threading.Lock()
        self._strikes: dict[str, deque] = defaultdict(deque)
        self._pages: dict[str, deque] = defaultdict(deque)
        self._downloads: dict[str, deque] = defaultdict(deque)
        self._bans: dict[str, float] = {}
        self._blocked: dict[str, int] = defaultdict(int)
        self._noted: dict[tuple[str, str], float] = {}
        self._loaded = False
        self._flushed = time.time()

    # ------------------------------------------------------------------ the check before the page

    def check(self, ip: str, path: str, query: str, agent: str, admin: bool) -> tuple[int, str] | None:
        """None to let the request through, or (status, reason) to turn it away."""
        if ip in TRUSTED:
            return None
        self._load()
        now = time.time()
        with self._lock:
            until = self._bans.get(ip)
            if until and until > now:
                self._blocked[ip] += 1
                self._flush(now)
                return 403, "banned"
        if ATTACK_TOOL.search(agent):
            self.strike(ip, STRIKES_TO_BAN, "tool", path, agent, "attack tool")
            return 403, "tool"
        raw = unquote(unquote(path + ("?" + query if query else "")))
        if PAYLOAD.search(raw):
            self.strike(ip, 3, "payload", path + ("?" + query if query else ""), agent)
            return 400, "payload"
        if not path.startswith(SITE) and path not in OWN and not path.startswith("/.well-known/") and PROBE.search(path):
            self.strike(ip, 2, "probe", path, agent)
            return 404, "probe"
        # Floods are counted on pages and the API; static files, clips and the gallery's small files are not
        # (the app fetches thirty previews at once).
        if admin or path.startswith(("/static/", "/files/")):
            return None
        with self._lock:
            pages = self._pages[ip]
            pages.append(now)
            while pages and pages[0] < now - 60:
                pages.popleft()
            flood = len(pages) > PAGE_LIMIT
            heavy = False
            if path.startswith("/download/"):
                dl = self._downloads[ip]
                dl.append(now)
                while dl and dl[0] < now - 600:
                    dl.popleft()
                heavy = len(dl) > DOWNLOAD_LIMIT
        if flood or heavy:
            self.strike(ip, 1, "flood" if flood else "downloads", path, agent, once_a_minute=True)
            return 429, "slow down"
        return None

    def after(self, ip: str, path: str, status: int, agent: str) -> None:
        """A 404 outside the site's own pages is a guess: one strike."""
        if (status == 404 and ip not in TRUSTED and not path.startswith(SITE) and path not in OWN
                and not path.startswith("/.well-known/")):
            self.strike(ip, 1, "notfound", path, agent, once_a_minute=False)

    # ------------------------------------------------------------------ strikes and bans

    def strike(self, ip: str, points: int, kind: str, path: str = "", agent: str = "", detail: str = "",
               once_a_minute: bool = False) -> None:
        if ip in TRUSTED:
            return
        now = time.time()
        with self._lock:
            note = (ip, kind)
            write = not once_a_minute or self._noted.get(note, 0) < now - 60
            if write:
                self._noted[note] = now
            marks = self._strikes[ip]
            for _ in range(points):
                marks.append(now)
            while marks and marks[0] < now - WINDOW:
                marks.popleft()
            ban = len(marks) >= STRIKES_TO_BAN and not (self._bans.get(ip, 0) > now)
        if write:
            store.sec_event(ip, kind, path, agent, detail)
        if ban:
            self.ban(ip, kind, agent)

    def ban(self, ip: str, why: str, agent: str = "", seconds: float | None = None) -> None:
        if seconds is None:
            seconds = BAN_AGAIN if store.was_banned_lately(ip) else BAN
        until = store.save_ban(ip, seconds, why)
        with self._lock:
            self._bans[ip] = until
            self._strikes.pop(ip, None)
        store.sec_event(ip, "ban", "", agent, f"{why}, {int(seconds // 3600)} h")

    def lift(self, ip: str) -> None:
        store.lift_ban(ip)
        with self._lock:
            self._bans.pop(ip, None)
            self._strikes.pop(ip, None)
        store.sec_event(ip, "unban", "", "", "by the admin")

    def _load(self) -> None:
        if self._loaded:
            return
        with self._lock:
            if self._loaded:
                return
            for row in store.ban_rows():
                self._bans[row["ip"]] = row["until"]
            self._loaded = True

    def _flush(self, now: float) -> None:
        # Requests turned away while banned are counted on the ban, a few times a minute (lock held).
        if now - self._flushed < 20 or not self._blocked:
            return
        counts, self._blocked = dict(self._blocked), defaultdict(int)
        self._flushed = now
        threading.Thread(target=store.add_blocked, args=(counts,), daemon=True).start()
        # Old notes and expired bans fall away from memory too.
        self._noted = {k: v for k, v in self._noted.items() if v > now - 120}
        self._bans = {k: v for k, v in self._bans.items() if v > now}
        for table in (self._strikes, self._pages, self._downloads):
            for key in [k for k, q in table.items() if not q or q[-1] < now - WINDOW]:
                del table[key]


shield = Shield()
