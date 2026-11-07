#!/bin/sh
# Installs or updates the MusicBar website from an unpacked bundle (run as root on the server):
# code in /opt/musicbar-web (root's, read-only to the app), data in /var/lib/musicbar-web (the app's
# own user), a systemd service on 127.0.0.1:7490, and HAProxy's lines for it. Safe to run again.
set -eu
SRC=$(cd "$(dirname "$0")/.." && pwd)
APP=/opt/musicbar-web
DATA=/var/lib/musicbar-web

id musicbar >/dev/null 2>&1 || useradd --system --no-create-home --home-dir "$DATA" --shell /usr/sbin/nologin musicbar
mkdir -p "$APP" "$DATA"

rm -rf "$APP/server.new"
cp -r "$SRC/server" "$APP/server.new"
find "$APP/server.new" -name __pycache__ -prune -exec rm -rf {} +
rm -rf "$APP/server.old"
[ -d "$APP/server" ] && mv "$APP/server" "$APP/server.old"
mv "$APP/server.new" "$APP/server"
rm -rf "$APP/server.old"
cp "$SRC/requirements.txt" "$APP/requirements.txt"

if [ ! -x "$APP/venv/bin/python" ]; then
    python3 -m venv "$APP/venv" || { apt-get install -y -q python3-venv >/dev/null && python3 -m venv "$APP/venv"; }
fi
# pip itself first: older ones have path traversal bugs when installing packages.
"$APP/venv/bin/python" -m pip install -q --disable-pip-version-check --upgrade "pip>=26.2"
"$APP/venv/bin/pip" install -q --disable-pip-version-check -r "$APP/requirements.txt"
chown -R root:root "$APP"
chmod -R a+rX,go-w "$APP"
chown -R musicbar:musicbar "$DATA"
chmod 750 "$DATA"

as_app() { (cd "$APP" && runuser -u musicbar -- env MUSICBAR_WEB_DATA="$DATA" "$APP/venv/bin/python" -m server.cli "$@"); }

# The admin signs in with a name and a long random password; both are kept for root only. An older
# password-only account is replaced the same way.
if ! as_app has-admin >/dev/null 2>&1; then
    # The name isn't written here (this file is public): MUSICBAR_ADMIN_USER, or a random one.
    ADMIN_USER="${MUSICBAR_ADMIN_USER:-admin-$(python3 -c 'import secrets; print(secrets.token_hex(3))')}"
    PW=$(python3 -c 'import secrets; print(secrets.token_urlsafe(24))')
    (cd "$APP" && runuser -u musicbar -- env MUSICBAR_WEB_DATA="$DATA" MUSICBAR_ADMIN_PASSWORD="$PW" "$APP/venv/bin/python" -m server.cli set-admin "$ADMIN_USER")
    umask 077
    printf 'user: %s\npassword: %s\n' "$ADMIN_USER" "$PW" > /root/musicbar-admin.txt
    rm -f /root/musicbar-admin-password.txt
    echo "admin account set (kept in /root/musicbar-admin.txt)"
fi

[ -f "$SRC/seed/manifest.json" ] && as_app seed "$SRC/seed"
[ -f "$SRC/ads/ads.json" ] && as_app seed-ads "$SRC/ads"

install -m 644 "$SRC/deploy/musicbar-web.service" /etc/systemd/system/musicbar-web.service
systemctl daemon-reload
systemctl enable musicbar-web >/dev/null 2>&1
systemctl restart musicbar-web
for i in 1 2 3 4 5 6 7 8 9 10; do curl -fsS http://127.0.0.1:7490/healthz >/dev/null 2>&1 && break; sleep 1; done
curl -fsS http://127.0.0.1:7490/healthz && echo " app up"

if [ -d /etc/letsencrypt/live/musicbar.truclv.com ]; then
    python3 "$SRC/deploy/haproxy_musicbar.py" --https
else
    python3 "$SRC/deploy/haproxy_musicbar.py"
fi
