"""The shield and the admin account, on a throwaway database: python -m pytest web/tests (or run this file)."""

import os
import sys
import tempfile
from pathlib import Path

os.environ["MUSICBAR_WEB_DATA"] = tempfile.mkdtemp(prefix="musicbar-shield-")
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from server import auth, store  # noqa: E402
from server.shield import Shield  # noqa: E402

BROWSER = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/130 Safari/537.36"


def test_people_pass():
    s = Shield()
    for path in ("/", "/gallery", "/gallery/heartbeat", "/download", "/submit", "/static/css/site.css",
                 "/files/item/3/preview", "/download/v1.8.0/MusicBar-1.8.0-portable.zip", "/favicon.ico",
                 "/apple-touch-icon.png", "/robots.txt", "/.well-known/security.txt", "/api/v1/gallery"):
        assert s.check("203.0.113.1", path, "", BROWSER, False) is None, path


def test_a_browser_asking_for_its_icons_is_never_banned():
    s = Shield()
    for _ in range(20):
        for path in ("/favicon.ico", "/apple-touch-icon.png", "/apple-touch-icon-precomposed.png"):
            s.after("203.0.113.9", path, 404, BROWSER)
    assert s.check("203.0.113.9", "/", "", BROWSER, False) is None


def test_scanner_hunting_secrets_is_banned():
    s = Shield()
    ip = "198.51.100.7"
    answers = [s.check(ip, p, "", "Mozilla/5.0", False) for p in ("/.env", "/.git/config", "/wp-config.php")]
    assert answers[0] == (404, "probe")
    assert s.check(ip, "/", "", "Mozilla/5.0", False) == (403, "banned")
    assert any(b["ip"] == ip for b in store.ban_rows())


def test_attack_tool_is_banned_at_once():
    s = Shield()
    assert s.check("198.51.100.8", "/", "", "sqlmap/1.7#stable", False) == (403, "tool")
    assert s.check("198.51.100.8", "/gallery", "", BROWSER, False) == (403, "banned")


def test_injection_in_the_address():
    s = Shield()
    assert s.check("198.51.100.9", "/gallery", "q=1%27%20UNION%20SELECT%20password%20FROM%20users", BROWSER, False) == (400, "payload")
    assert s.check("198.51.100.10", "/gallery/../../etc/passwd", "", BROWSER, False) == (400, "payload")
    assert s.check("198.51.100.11", "/gallery", "q=<script>alert(1)</script>", BROWSER, False) == (400, "payload")


def test_guessing_paths_adds_up():
    s = Shield()
    ip = "198.51.100.12"
    for n in range(5):
        assert s.check(ip, f"/random-{n}", "", BROWSER, False) is None
        s.after(ip, f"/random-{n}", 404, BROWSER)
    assert s.check(ip, "/", "", BROWSER, False) == (403, "banned")


def test_flood_slows_down():
    s = Shield()
    ip = "198.51.100.13"
    answers = [s.check(ip, "/gallery", "", BROWSER, False) for _ in range(130)]
    assert answers[0] is None and (429, "slow down") in answers


def test_admin_and_trusted_skip_the_flood_count():
    s = Shield()
    assert all(s.check("198.51.100.14", "/gallery", "", BROWSER, True) is None for _ in range(200))
    assert all(s.check("127.0.0.1", "/.env", "", "sqlmap", False) is None for _ in range(10))


def test_lift_lets_back_in():
    s = Shield()
    ip = "198.51.100.15"
    s.check(ip, "/", "", "nikto", False)
    assert s.check(ip, "/", "", BROWSER, False) == (403, "banned")
    s.lift(ip)
    assert s.check(ip, "/", "", BROWSER, False) is None


def test_admin_account():
    auth.set_admin("Tester", "a long enough password 123")
    assert auth.has_admin()
    assert auth.check_login("tester", "a long enough password 123")
    assert auth.check_login(" TESTER ", "a long enough password 123")
    assert not auth.check_login("tester", "wrong")
    assert not auth.check_login("admin", "a long enough password 123")
    assert not auth.check_login("", "")


def test_session_belongs_to_its_browser():
    cookie = auth.new_session("tester", BROWSER)
    assert auth.read_session(cookie, BROWSER)["user"] == "tester"
    assert auth.read_session(cookie, "curl/8.0") is None
    assert auth.read_session(cookie[:-2] + "xx", BROWSER) is None


def test_renaming_the_account_ends_old_sessions():
    auth.set_admin("tester", "a long enough password 123")
    cookie = auth.new_session("tester", BROWSER)
    assert auth.read_session(cookie, BROWSER) is not None
    auth.set_admin("someone-else", "a long enough password 123")
    assert auth.read_session(cookie, BROWSER) is None


if __name__ == "__main__":
    failed = 0
    for name, fn in list(globals().items()):
        if name.startswith("test_"):
            try:
                fn()
                print("ok  ", name)
            except AssertionError as error:
                failed += 1
                print("FAIL", name, error)
    sys.exit(1 if failed else 0)
