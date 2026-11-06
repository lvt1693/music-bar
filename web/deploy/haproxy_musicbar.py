"""Adds (or refreshes) MusicBar's lines in /etc/haproxy/haproxy.cfg, between "# >>> musicbar" and
"# <<< musicbar" markers like the other sites on this server, then checks the file before it is used.

  python3 haproxy_musicbar.py [--https]

Plain HTTP for musicbar.truclv.com and for the bare address until the certificate exists; with
--https the domain redirects to HTTPS (the bare address stays HTTP: no certificate can name it).
"""

import re
import shutil
import subprocess
import sys
import time

CFG = "/etc/haproxy/haproxy.cfg"
HOST = "musicbar.truclv.com"
IP = "42.96.40.15"
TAG = "musicbar"


def block(lines):
    return [f"    # >>> {TAG}"] + ["    " + l for l in lines] + [f"    # <<< {TAG}"]


def main():
    global CFG
    https = "--https" in sys.argv
    dry = "--dry-run" in sys.argv
    if "--cfg" in sys.argv:
        CFG = sys.argv[sys.argv.index("--cfg") + 1]
    text = open(CFG, encoding="utf-8").read()
    # Out with any earlier version of our lines.
    text = re.sub(rf"\n[ \t]*# >>> {TAG}.*?# <<< {TAG}[^\n]*", "", text, flags=re.S)
    text = re.sub(rf"\n# >>> {TAG}: backend.*?# <<< {TAG}[^\n]*", "", text, flags=re.S)
    lines = text.split("\n")

    is_ours = f"{{ hdr(host),field(1,:) -i {HOST} }}"
    is_ip = f"{{ hdr(host),field(1,:) -i {IP} }}"
    # Uploads go no further than 6 MB, and only with a declared length: nothing big ever reaches the app.
    guard = [f"acl host_{TAG} hdr(host),field(1,:) -i {HOST} {IP}",
             f"http-request deny deny_status 413 if host_{TAG} {{ req.hdr_val(content-length) gt 6000000 }}",
             f"http-request deny deny_status 400 if host_{TAG} METH_POST !{{ req.hdr(content-length) -m found }}"]
    http_rules = guard[:]
    if https:
        http_rules.append(f"http-request redirect scheme https code 301 if {is_ours} !acme")
    else:
        http_rules.append(f"use_backend {TAG} if {is_ours} !acme")
    http_rules.append(f"use_backend {TAG} if {is_ip} !acme")
    https_rules = guard + [f"use_backend {TAG} if {is_ours}"]

    def section(header):
        start = next(n for n, l in enumerate(lines) if l.strip() == header)
        end = next((n for n in range(start + 1, len(lines)) if lines[n] and not lines[n][0].isspace()), len(lines))
        return start, end

    # http_in: after its ACME acl, which our rules use.
    start, end = section("frontend http_in")
    acme = next(n for n in range(start, end) if lines[n].strip().startswith("acl acme "))
    lines[acme + 1:acme + 1] = block(http_rules)
    # https_in: after its bind and its forwarded-proto header.
    start, end = section("frontend https_in")
    at = start + 1
    while at < end and lines[at].strip().startswith(("bind", "http-request set-header X-Forwarded-Proto")):
        at += 1
    lines[at:at] = block(https_rules)
    text = "\n".join(lines).rstrip("\n") + "\n"
    text += (f"\n# >>> {TAG}: backend (MusicBar website, /opt/musicbar-web, uvicorn on 127.0.0.1:7490)\n"
             f"backend {TAG}\n"
             # Edge limits, per address, before the app sees anything: a flood (over 150 requests in
             # ten seconds) or a scanner's run of errors (over 60 a minute) gets 429 straight away.
             "    stick-table type ip size 200k expire 10m store http_req_rate(10s),http_err_rate(60s)\n"
             "    http-request track-sc0 src\n"
             "    http-request deny deny_status 429 if { sc_http_req_rate(0) gt 150 }\n"
             "    http-request deny deny_status 429 if { sc_http_err_rate(0) gt 60 }\n"
             "    http-request set-header X-Forwarded-Proto https if { ssl_fc }\n"
             "    compression algo gzip\n"
             "    compression type text/html text/css application/javascript text/javascript application/json image/svg+xml text/plain\n"
             "    timeout server 300s\n"
             "    server app 127.0.0.1:7490 check inter 5s\n"
             f"# <<< {TAG}\n")
    if dry:
        print(text)
        return

    backup = f"{CFG}.bak-{TAG}-{time.strftime('%Y%m%d%H%M%S')}"
    shutil.copy2(CFG, backup)
    candidate = CFG + ".new"
    open(candidate, "w", encoding="utf-8").write(text)
    check = subprocess.run(["haproxy", "-c", "-f", candidate], capture_output=True, text=True)
    if check.returncode != 0:
        print("haproxy refused the new config, nothing changed:\n" + check.stdout + check.stderr)
        sys.exit(1)
    shutil.move(candidate, CFG)
    subprocess.run(["systemctl", "reload", "haproxy"], check=True)
    print(f"haproxy updated ({'https' if https else 'http'}), backup at {backup}")


if __name__ == "__main__":
    main()
