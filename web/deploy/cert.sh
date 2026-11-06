#!/bin/sh
# musicbar.truclv.com's own certificate, the way the other sites here get theirs: certbot standalone
# on 127.0.0.1:18080 (HAProxy's :80 sends the ACME path there), a deploy hook that writes the .pem
# HAProxy reads and reloads it, then HTTPS on for the domain. The certbot timer renews it.
set -eu
SRC=$(cd "$(dirname "$0")/.." && pwd)
D=musicbar.truclv.com
ip=$(dig +short "$D" A @dns1.registrar-servers.com | tail -1)
if [ "$ip" != "42.96.40.15" ]; then
    echo "DNS: $D points to '${ip:-nothing}', not this server. Add an A record $D -> 42.96.40.15 first."
    exit 2
fi
if [ ! -d "/etc/letsencrypt/live/$D" ]; then
    certbot certonly --standalone --http-01-port 18080 --http-01-address 127.0.0.1 -d "$D" \
        --non-interactive --agree-tos --keep-until-expiring 2>&1 | grep -v -E "^ *$|donate|EFF|ISRG|If you like" | tail -5
fi
install -m 755 "$SRC/deploy/musicbar-haproxy-hook.sh" /etc/letsencrypt/renewal-hooks/deploy/musicbar-haproxy.sh
/etc/letsencrypt/renewal-hooks/deploy/musicbar-haproxy.sh
python3 "$SRC/deploy/haproxy_musicbar.py" --https
openssl x509 -noout -subject -enddate -in "/etc/haproxy/certs/$D.pem"
