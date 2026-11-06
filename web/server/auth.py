"""The admin's password (scrypt), a signed session cookie and a CSRF token tied to it."""

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


def _secret() -> bytes:
    path = store.DATA / "secret"
    store.DATA.mkdir(parents=True, exist_ok=True)
    if not path.exists():
        path.write_text(secrets.token_hex(32))
        os.chmod(path, 0o600)
    return path.read_text().strip().encode()


def set_password(password: str) -> None:
    salt = secrets.token_bytes(16)
    digest = hashlib.scrypt(password.encode(), salt=salt, n=2 ** 15, r=8, p=1, maxmem=64 * 1024 * 1024)
    store.DATA.mkdir(parents=True, exist_ok=True)
    path = store.DATA / "admin.json"
    path.write_text(json.dumps({"salt": salt.hex(), "hash": digest.hex()}))
    os.chmod(path, 0o600)


def check_password(password: str) -> bool:
    path = store.DATA / "admin.json"
    if not path.exists() or not password:
        return False
    saved = json.loads(path.read_text())
    digest = hashlib.scrypt(password.encode(), salt=bytes.fromhex(saved["salt"]), n=2 ** 15, r=8, p=1, maxmem=64 * 1024 * 1024)
    return hmac.compare_digest(digest.hex(), saved["hash"])


def _sign(payload: bytes) -> str:
    return base64.urlsafe_b64encode(hmac.new(_secret(), payload, hashlib.sha256).digest()).decode().rstrip("=")


def new_session() -> str:
    payload = base64.urlsafe_b64encode(json.dumps({"exp": time.time() + SESSION_SECONDS,
                                                   "csrf": secrets.token_urlsafe(18)}).encode()).decode().rstrip("=")
    return payload + "." + _sign(payload.encode())


def read_session(cookie: str | None) -> dict | None:
    if not cookie or "." not in cookie:
        return None
    payload, signature = cookie.rsplit(".", 1)
    if not hmac.compare_digest(signature, _sign(payload.encode())):
        return None
    try:
        data = json.loads(base64.urlsafe_b64decode(payload + "=" * (-len(payload) % 4)))
    except ValueError:
        return None
    return data if data.get("exp", 0) > time.time() else None
