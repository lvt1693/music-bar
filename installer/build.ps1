# Builds the two release files into artifacts\:
#   MusicBar-<version>-Setup.exe     per-user installer (Inno Setup 6)
#   MusicBar-<version>-portable.zip  the same self-contained app, unzip and run
# Needs the .NET 10 SDK, MSVC (for the Windows 11 taskbar module) and Inno Setup 6.
#
# -IdentityCert <thumbprint>: also builds the Windows 10 identity package (installer\identity, #21), signed with that
# code-signing certificate from CurrentUser\My; its subject must be the publisher app.manifest names. -TestCert: the
# certificate is a self-made test one, shipped as MusicBar-test.cer for the setup to trust (Windows SDK needed).
param([string]$Version = "1.0.0", [string]$IdentityCert = "", [switch]$TestCert)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot
$artifacts = Join-Path $root "artifacts"
$publish = Join-Path $artifacts "publish"

if (Test-Path $artifacts) { Remove-Item $artifacts -Recurse -Force }

# Self-contained single file: nothing to install first, not even .NET. Not compressed: a compressed
# bundle is inflated into the process's private memory (measured 143 MB against 58 MB), while an
# uncompressed one is mapped from disk; the installer and the zip compress it for the download anyway.
dotnet publish (Join-Path $root "src\MusicBar\MusicBar.csproj") -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=false `
    -p:DebugType=none -p:Version=$Version -o $publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

# The identity package: the manifest with this version and the certificate's subject, packed and signed.
$isccExtra = @()
if ($IdentityCert) {
    $cert = Get-Item "Cert:\CurrentUser\My\$IdentityCert"
    $declared = ([xml](Get-Content (Join-Path $root "src\MusicBar\app.manifest") -Raw)).assembly.msix.publisher
    if ($cert.Subject -ne $declared) { throw "The certificate's subject '$($cert.Subject)' isn't the publisher app.manifest names ('$declared')" }
    $kit = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\makeappx.exe" | Sort-Object FullName -Descending | Select-Object -First 1
    if (-not $kit) { throw "makeappx.exe not found (Windows SDK)" }
    $packageVersion = (@($Version.Split('.')) + @("0", "0", "0"))[0..3] -join "."
    $layout = Join-Path $artifacts "identity-layout"
    New-Item -ItemType Directory -Force $layout | Out-Null
    Copy-Item (Join-Path $PSScriptRoot "identity\Assets") $layout -Recurse
    (Get-Content (Join-Path $PSScriptRoot "identity\AppxManifest.xml") -Raw).Replace('$PUBLISHER$', $cert.Subject).Replace('$VERSION$', $packageVersion) |
        Set-Content (Join-Path $layout "AppxManifest.xml") -Encoding utf8
    $identity = New-Item -ItemType Directory -Force (Join-Path $publish "identity")
    $msix = Join-Path $identity "MusicBar.Identity.msix"
    & $kit.FullName pack /d $layout /p $msix /nv /o | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "makeappx failed" }
    & (Join-Path $kit.DirectoryName "signtool.exe") sign /fd SHA256 /sha1 $IdentityCert /s My $msix | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "signtool failed" }
    Remove-Item $layout -Recurse -Force
    if ($TestCert) {
        Export-Certificate -Cert $cert -FilePath (Join-Path $identity "MusicBar-test.cer") | Out-Null
        $isccExtra += "/DTestCertThumb=$($cert.Thumbprint)"
    }
    "identity package: $packageVersion, signed by $($cert.Subject)"
}

# Portable: the app, its font licenses and the license, in one folder.
$stage = Join-Path $artifacts "MusicBar"
New-Item -ItemType Directory -Force $stage | Out-Null
Copy-Item (Join-Path $publish "*") $stage -Recurse
Copy-Item (Join-Path $root "LICENSE") (Join-Path $stage "LICENSE.txt")
Compress-Archive -Path $stage -DestinationPath (Join-Path $artifacts "MusicBar-$Version-portable.zip") -CompressionLevel Optimal
Remove-Item $stage -Recurse -Force

# Installer.
$iscc = @(
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) { throw "Inno Setup 6 not found (winget install JRSoftware.InnoSetup)" }
& $iscc /Q "/DAppVersion=$Version" "/DPublishDir=$publish" @isccExtra (Join-Path $PSScriptRoot "MusicBar.iss")
if ($LASTEXITCODE -ne 0) { throw "ISCC failed" }

Get-ChildItem $artifacts -File | ForEach-Object { "{0,-34} {1,7:N1} MB" -f $_.Name, ($_.Length / 1MB) }
