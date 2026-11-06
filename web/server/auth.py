"""The admin's account (a name and a scrypt password), a signed session cookie tied to the browser that
signed in, and a CSRF token tied to the session."""

from __future__ import annotations

import base64
import hashlib
import hmac
import json
import os
import secrets
import time

from . import store

COOKIE = "mb_admin"
SESSION_SECONDS = 12 * 3600
_SCRYPT = dict(n=2 ** 15, r=8, p=1, maxmem=64 * 1024 * 1024)
# Hashed for a name that doesn't exist, so a wrong name takes as long as a wrong password.
_DUMMY_SALT = b"musicbar-no-such-admin"


def _secret() -> bytes:
    path = store.DATA / "secret"
    store.DATA.mkdir(parents=True, exist_ok=True)
    if not path.exists():
        path.write_text(secrets.token_hex(32))
        os.chmod(path, 0o600)
    return path.read_text().strip().encode()


def set_admin(user: str, password: str) -> None:
    salt = secrets.token_bytes(16)
    digest = hashlib.scrypt(password.encode(), salt=salt, **_SCRYPT)
    store.DATA.mkdir(parents=True, exist_ok=True)
    path = store.DATA / "admin.json"
    path.write_text(json.dumps({"user": user.strip().lower(), "salt": salt.hex(), "hash": digest.hex()}))
    os.chmod(path, 0o600)


def has_admin() -> bool:
    try:
        return bool(json.loads((store.DATA / "admin.json").read_text()).get("user"))
    except (OSError, ValueError):
        return False


def check_login(user: str, password: str) -> bool:
    try:
        saved = json.loads((store.DATA / "admin.json").read_text())
    except (OSError, ValueError):
        saved = {}
    known = saved.get("user", "")
    salt = bytes.fromhex(saved["salt"]) if known else _DUMMY_SALT
    digest = hashlib.scrypt((password or "").encode(), salt=salt, **_SCRYPT).hex()
    name_ok = hmac.compare_digest((user or "").strip().lower().encode(), known.encode())
    pass_ok = hmac.compare_digest(digest, saved.get("hash", "0" * 128))
    return bool(known) and bool(password) and name_ok and pass_ok


def _agent(agent: str) -> str:
    return hashlib.sha256((agent or "").encode()).hexdigest()[:16]


def _sign(payload: bytes) -> str:
    return base64.urlsafe_b64encode(hmac.new(_secret(), payload, hashlib.sha256).digest()).decode().rstrip("=")


def sign(text: str) -> str:
    """A short signature for something the site hands out and wants back unchanged (the form's clock)."""
    return _sign(text.encode())[:22]


def new_session(user: str, agent: str) -> str:
    data = {"exp": time.time() + SESSION_SECONDS, "user": user.strip().lower(), "ua": _agent(agent),
            "csrf": secrets.token_urlsafe(18)}
    payload = base64.urlsafe_b64encode(json.dumps(data).encode()).decode().rstrip("=")
    return payload + "." + _sign(payload.encode())


def read_session(cookie: str | None, agent: str) -> dict | None:
    if not cookie or "." not in cookie:
        return None
    payload, signature = cookie.rsplit(".", 1)
    if not hmac.compare_digest(signature, _sign(payload.encode())):
        return None
    try:
        data = json.loads(base64.urlsafe_b64decode(payload + "=" * (-len(payload) % 4)))
    except ValueError:
        return None
    # A cookie carried to another browser is worth nothing, and so is one for a name that was changed.
    if data.get("exp", 0) <= time.time() or not data.get("user") or data.get("ua") != _agent(agent):
        return None
    try:
        current = json.loads((store.DATA / "admin.json").read_text()).get("user", "")
    except (OSError, ValueError):
        return None
    return data if hmac.compare_digest(data["user"].encode(), current.encode()) else None
