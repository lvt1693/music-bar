# Packs web/ (server, deploy scripts, seed content, ads) and installs it on the server with setup.sh.
# Uses Windows' own OpenSSH, whose agent holds the server's key.  .\deploy.ps1 [-Cert]
param([string]$Server = "root@42.96.40.15", [switch]$Cert)
$ErrorActionPreference = "Stop"
$web = Split-Path $PSScriptRoot
$ssh = "$env:SystemRoot\System32\OpenSSH\ssh.exe"
$scp = "$env:SystemRoot\System32\OpenSSH\scp.exe"
$bundle = Join-Path $env:TEMP "musicbar-web.tgz"
Remove-Item $bundle -ErrorAction SilentlyContinue
$parts = @("server", "deploy", "requirements.txt") + @("seed", "ads" | Where-Object { Test-Path (Join-Path $web $_) })
& tar.exe -czf $bundle -C $web --exclude=__pycache__ --exclude=*.pyc @parts
"bundle: {0:0.0} MB" -f ((Get-Item $bundle).Length / 1MB)
& $scp -o BatchMode=yes -q $bundle "${Server}:/tmp/musicbar-web.tgz"
$remote = "rm -rf /tmp/musicbar-web-deploy && mkdir -p /tmp/musicbar-web-deploy && tar -xzf /tmp/musicbar-web.tgz -C /tmp/musicbar-web-deploy && " +
          "find /tmp/musicbar-web-deploy/deploy -name '*.sh' -exec sed -i 's/\r$//' {} + && sh /tmp/musicbar-web-deploy/deploy/setup.sh"
if ($Cert) { $remote += " && sh /tmp/musicbar-web-deploy/deploy/cert.sh" }
& $ssh -o BatchMode=yes $Server $remote
