#!/bin/sh
# certbot deploy hook: musicbar.truclv.com's certificate, as HAProxy reads it.
[ -d /etc/letsencrypt/live/musicbar.truclv.com ] || exit 0
cat /etc/letsencrypt/live/musicbar.truclv.com/fullchain.pem /etc/letsencrypt/live/musicbar.truclv.com/privkey.pem > /etc/haproxy/certs/musicbar.truclv.com.pem
chmod 600 /etc/haproxy/certs/musicbar.truclv.com.pem
systemctl reload haproxy
