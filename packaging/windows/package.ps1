<#
.SYNOPSIS
    Builds the Windows artifacts for one runtime.
.DESCRIPTION
    Turns the self-contained publish output from packaging/publish.ps1 into the three files
    a user actually gets:

      ElectronicPointer-<version>-<rid>.zip    portable tree, unzip and run
      ElectronicPointer-<version>-<rid>.msix   installable package, start menu entry
      ElectronicPointer-<version>-<rid>-setup.exe
                                                NSIS installer, per machine install

    All three land in <repo>/artifacts/dist, the same folder the Linux and macOS scripts
    write to, so a release job never has to know which platform an artifact came from.

    The MSIX is not produced by a packaging project. It is a plain makeappx pack of the
    publish tree plus AppxManifest.xml and the Assets folder, which keeps the whole Windows
    pipeline on Windows PowerShell and the Windows SDK the hosted runner already carries.
    The NSIS installer is a plain makensis run on packaging/windows/installer.nsi over the
    same staged tree, so it needs the stock NSIS package, which CI installs before calling
    this script, and nothing else.

    Compatible with Windows PowerShell 5.1 and with pwsh.

.PARAMETER Rid
    One of win-x64, win-x86 or win-arm64.
.PARAMETER PublishRoot
    Where publish.ps1 put the output. Defaults to <repo>/artifacts/out.
.PARAMETER OutputRoot
    Where the artifacts go. Defaults to <repo>/artifacts/dist.
.PARAMETER Publisher
    The Publisher attribute of the MSIX identity. Defaults to the subject of the signing
    certificate, because makeappx records the subject signtool later proves and the two
    have to match, or to CN=Mutantcat Working Group for an unsigned build.
.PARAMETER CertificatePath
    Optional .pfx. Without it a self-signed code signing certificate is generated on this
    machine and used instead, so an installer built here still says who built it. Pass
    -SkipSigning for completely unsigned output.
.PARAMETER CertificatePassword
    Password for the .pfx, when it has one.
.PARAMETER TimestampUrl
    RFC 3161 timestamp server, so a signature outlives the certificate that made it.
.PARAMETER SkipMsix
    Build only the portable zip and the installer, for example on a machine with no Windows
    SDK.
.PARAMETER SkipNsis
    Build only the portable zip and the MSIX, for example on a machine with no NSIS.
.PARAMETER SkipSigning
    Sign nothing at all, not even with the generated self-signed certificate.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('win-x64', 'win-x86', 'win-arm64')]
    [string]$Rid,

    [string]$PublishRoot,
    [string]$OutputRoot,

    [string]$Publisher,
    [string]$CertificatePath,
    [string]$CertificatePassword,
    [string]$TimestampUrl = 'http://timestamp.digicert.com',

    [switch]$SkipMsix,
    [switch]$SkipNsis,
    [switch]$SkipSigning
)

$ErrorActionPreference = 'Stop'

# This script sits in packaging/windows, one level deeper than publish.ps1 and the two
# shell scripts, so it needs two hops to reach the repository root.
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if ([string]::IsNullOrWhiteSpace($PublishRoot)) {
    $PublishRoot = Join-Path $root 'artifacts\out'
}
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $root 'artifacts\dist'
}

$out = Join-Path $PublishRoot $Rid
$stageRoot = Join-Path $root 'artifacts\stage\windows'
$manifestTemplate = Join-Path $PSScriptRoot 'Package.appxmanifest'
$assetRoot = Join-Path $PSScriptRoot 'assets'

if (-not (Test-Path -LiteralPath $manifestTemplate)) {
    throw "MSIX manifest not found: $manifestTemplate"
}
if (-not (Test-Path -LiteralPath $assetRoot)) {
    throw "MSIX assets not found: $assetRoot"
}

$executable = Join-Path $out 'ElectronicPointer.exe'
if (-not (Test-Path -LiteralPath $executable)) {
    throw "$executable is missing. Run pwsh packaging/publish.ps1 -Rid $Rid first."
}

# publish.ps1 leaves buildstamp.txt next to the executable with the same version line on
# every platform, so the deb control file, the AppImage name and this package can all be
# derived without guessing.
$stampText = Get-Content -LiteralPath (Join-Path $out 'buildstamp.txt') -Raw
$stampLines = $stampText -split '\r?\n'
$version = ([string]$stampLines[0]).Trim()

if ($version -notmatch '^\d+\.\d+\.\d{8}$') {
    throw "buildstamp.txt has an unusable version '$version'. Expected something like 1.0.20260928."
}
Write-Host "packaging ElectronicPointer $Rid version=$version"

# MSIX compares the four version fields as plain integers and each field tops out at 65535,
# so the yyyyMMdd stamp cannot be dropped into one of them. Counting days from the project's
# first release keeps the same ordering as the stamp and stays a legal field size, which is
# what Settings > Apps shows and what the installer compares against an older package.
$invariant = [System.Globalization.CultureInfo]::InvariantCulture
$stamp8 = $version.Split('.')[2]
$projectEpoch = [datetime]::ParseExact('20200101', 'yyyyMMdd', $invariant)
$stampDate = [datetime]::ParseExact($stamp8, 'yyyyMMdd', $invariant)
$msixBuild = ($stampDate - $projectEpoch).Days
$msixRevision = 0

$arch = switch ($Rid) {
    'win-x64' { 'x64' }
    'win-arm64' { 'arm64' }
    default { 'x86' }
}

# One installer target for all three payloads. The architecture of the payload never depends
# on the architecture of the installer binary: the script writes the 64 bit registry view
# through SetRegView 64 and installs into $PROGRAMFILES64, so the 64 bit and the ARM64
# payloads land in the same places they would land from a native installer. NSIS ships stubs
# for the x86-unicode target only, and CI installs the stock package rather than building a
# custom one, so an amd64 or an arm64 stub would have to be built from source first. This is
# also what most of the NSIS ecosystem ships: the familiar 32 bit installer binary, with the
# native application inside it.
$nsisTarget = 'x86-unicode'

function Find-WindowsSdkTool {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Name
    )

    # The kit drops one folder per SDK version and an arch folder under each; the newest
    # x64 copy is the one that matches the runner's architecture.
    $kitRoot = 'C:\Program Files (x86)\Windows Kits\10\bin'
    if (-not (Test-Path -LiteralPath $kitRoot)) {
        return $null
    }

    $hit = Get-ChildItem -Path $kitRoot -Filter $Name -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -like '*\x64\*' } |
        Sort-Object FullName -Descending |
        Select-Object -First 1

    if ($hit) {
        return $hit.FullName
    }
    return $null
}

function Find-Nsis {
    # makensis is on PATH when NSIS was installed for everybody; otherwise it lives in one
    # of the two well known folders the installer offers.
    $onPath = Get-Command makensis -ErrorAction SilentlyContinue
    if ($onPath) {
        return $onPath.Source
    }

    foreach ($candidate in @(
        (Join-Path $env:ProgramFiles 'NSIS\makensis.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'NSIS\makensis.exe'))) {
        if (-not [string]::IsNullOrWhiteSpace($candidate) -and (Test-Path -LiteralPath $candidate)) {
            return $candidate
        }
    }
    return $null
}

function Test-NsisTarget {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Nsis,

        [Parameter(Mandatory = $true)]
        [string]$Target
    )

    # A Windows install of NSIS to which only the x86 tools were added has no zlib-, bzip2-
    # or lzma-amd64-unicode stub, and makensis refuses to build the target rather than
    # falling back to a 32 bit installer. Any stub named after the target is enough, so the
    # check does not care which compressor ships.
    $stubs = Join-Path (Split-Path -Parent $Nsis) 'Stubs'
    if (-not (Test-Path -LiteralPath $stubs)) {
        return $false
    }

    return [bool](Get-ChildItem -LiteralPath $stubs -Filter "*$Target*" -File -ErrorAction SilentlyContinue |
        Select-Object -First 1)
}

function New-SelfSignedCodeSigningCertificate {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Subject
    )

    # A release gets its identity from a real certificate. Everything else - a fork, a
    # workstation, a pull request - still deserves a signature that names the publisher, so
    # one is minted here. It lives in the current user store and is reused across runs.
    $existing = Get-ChildItem -Path 'Cert:\CurrentUser\My' -CodeSigningCert -ErrorAction SilentlyContinue |
        Where-Object {
            $_.Subject -eq "CN=$Subject" -and
            $_.HasPrivateKey -and
            $_.NotAfter -gt (Get-Date).AddYears(1)
        } |
        Sort-Object -Property NotAfter -Descending |
        Select-Object -First 1

    if ($existing) {
        Write-Host "reusing the self-signed certificate $($existing.Thumbprint)"
        return $existing.Thumbprint
    }

    try {
        $created = New-SelfSignedCertificate `
            -Type CodeSigningCert `
            -Subject "CN=$Subject" `
            -CertStoreLocation 'Cert:\CurrentUser\My' `
            -KeyExportPolicy Exportable `
            -KeyUsage DigitalSignature `
            -HashAlgorithm SHA256 `
            -NotAfter (Get-Date).AddYears(3)
    }
    catch {
        Write-Warning "could not create a self-signed certificate: $($_.Exception.Message)"
        return $null
    }

    Write-Host "created a self-signed certificate $($created.Thumbprint) for CN=$Subject"
    return $created.Thumbprint
}

function Invoke-Signature {
    param(
        [Parameter(Mandatory = $true)]
        [string]$TargetPath,

        [switch]$WithTimestamp
    )

    if (-not $script:CertificateThumbprint) {
        Write-Warning "nothing signs with anything; leaving $(Split-Path $TargetPath -Leaf) unsigned."
        return
    }

    $signtool = Find-WindowsSdkTool -Name 'signtool.exe'
    if (-not $signtool) {
        Write-Warning "signtool.exe not found; leaving $(Split-Path $TargetPath -Leaf) unsigned."
        return
    }

    $signArgs = @('sign', '/fd', 'SHA256')
    if ($script:Certificate) {
        # Either a .pfx handed over, or the generated self-signed certificate, which is
        # already in the store under that thumbprint.
        $signArgs += @('/sha1', $script:Certificate.Thumbprint)
    }
    else {
        $signArgs += @('/f', $CertificatePath)
        if ($CertificatePassword) {
            $signArgs += @('/p', $CertificatePassword)
        }
    }
    if ($WithTimestamp -and $TimestampUrl) {
        $signArgs += @('/tr', $TimestampUrl, '/td', 'sha256')
    }
    $signArgs += $TargetPath

    & $signtool @signArgs
    if ($LASTEXITCODE -ne 0) {
        throw "signtool failed on $(Split-Path $TargetPath -Leaf) with exit code $LASTEXITCODE"
    }
}

# The certificate decides two things: what signtool signs with, and the Publisher attribute
# makeappx records. Resolve it once, before the artifacts stage, because the MSIX manifest is
# rendered from it and the zip carries the executable it signs.
$script:Certificate = $null
$script:CertificateThumbprint = $null

if ($CertificatePath) {
    $script:Certificate = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2(
        $CertificatePath, $CertificatePassword)
    $script:CertificateThumbprint = $script:Certificate.Thumbprint
    Write-Host "signing with $($script:Certificate.Subject) from $(Split-Path $CertificatePath -Leaf)"
}
elseif (-not $SkipSigning) {
    $script:CertificateThumbprint = New-SelfSignedCodeSigningCertificate -Subject 'Mutantcat Working Group'
    if ($script:CertificateThumbprint) {
        $script:Certificate = Get-Item -Path "Cert:\CurrentUser\My\$script:CertificateThumbprint"
    }
}
$script:SigningIsSelfMade = [bool](-not $CertificatePath -and $script:CertificateThumbprint)

if ([string]::IsNullOrWhiteSpace($Publisher)) {
    if ($script:Certificate) {
        $Publisher = $script:Certificate.Subject
    }
    else {
        $Publisher = 'CN=Mutantcat Working Group'
    }
}
if ($script:SigningIsSelfMade -and $Publisher -notlike 'CN=*') {
    # makeappx refuses anything that is not a distinguished name, and the self-signed
    # certificate is issued for exactly this subject.
    $Publisher = "CN=$Publisher"
}
Write-Host "msix identity: name=Mutantcat.ElectronicPointer publisher=$Publisher version=1.0.$msixBuild.$msixRevision arch=$arch"
if (-not $script:CertificateThumbprint -and -not $SkipSigning) {
    Write-Warning 'no code signing certificate could be produced; everything stays unsigned.'
}

New-Item -ItemType Directory -Force -Path $stageRoot | Out-Null
New-Item -ItemType Directory -Force -Path $OutputRoot | Out-Null

# --- portable zip -------------------------------------------------------------
# The staged tree is a straight copy of the publish output, minus the stamp file, which is
# build infrastructure and would only confuse an unpacked install.
$portableStage = Join-Path $stageRoot "ElectronicPointer-$version-$Rid"
Remove-Item -LiteralPath $portableStage -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $portableStage | Out-Null

Copy-Item -Path (Join-Path $out '*') -Destination $portableStage -Recurse -Force
Remove-Item -LiteralPath (Join-Path $portableStage 'buildstamp.txt') -Force -ErrorAction SilentlyContinue

# Sign the executable that ends up inside the zip, the installer and the MSIX rather than
# the containers around them: only the executable is what a user double clicks. Doing it
# here means the NSIS payload is packed from a tree that already carries the signature.
if ($script:CertificateThumbprint) {
    Invoke-Signature -TargetPath (Join-Path $portableStage 'ElectronicPointer.exe') -WithTimestamp
}

$zipPath = Join-Path $OutputRoot "ElectronicPointer-$version-$Rid.zip"
if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
Compress-Archive -Path (Get-ChildItem -LiteralPath $portableStage).FullName `
    -DestinationPath $zipPath `
    -CompressionLevel Optimal

# --- msix ---------------------------------------------------------------------
if ($SkipMsix) {
    Write-Warning "-SkipMsix was passed; only the portable zip was built."
}
else {
    $makeappx = Find-WindowsSdkTool -Name 'makeappx.exe'
    if (-not $makeappx) {
        Write-Warning "makeappx.exe not found; skipping the MSIX. The portable zip is still usable."
    }
    else {
        $msixStage = Join-Path $stageRoot 'msix'
        Remove-Item -LiteralPath $msixStage -Recurse -Force -ErrorAction SilentlyContinue
        New-Item -ItemType Directory -Path $msixStage | Out-Null

        Copy-Item -Path (Join-Path $out '*') -Destination $msixStage -Recurse -Force
        Remove-Item -LiteralPath (Join-Path $msixStage 'buildstamp.txt') -Force -ErrorAction SilentlyContinue

        $manifest = Join-Path $msixStage 'AppxManifest.xml'
        $manifestText = [System.IO.File]::ReadAllText($manifestTemplate)
        $manifestText = $manifestText.Replace('@PUBLISHER@', $Publisher)
        $manifestText = $manifestText.Replace('@BUILD@', $msixBuild)
        $manifestText = $manifestText.Replace('@REVISION@', $msixRevision)
        $manifestText = $manifestText.Replace('@ARCH@', $arch)
        # Written without a byte order mark: the manifest declares its own encoding, and a
        # stray BOM in front of the XML declaration makes some installers refuse the file.
        [System.IO.File]::WriteAllText($manifest, $manifestText, (New-Object System.Text.UTF8Encoding($false)))

        $assetTarget = Join-Path $msixStage 'Assets'
        New-Item -ItemType Directory -Path $assetTarget | Out-Null
        Copy-Item -Path (Join-Path $assetRoot '*') -Destination $assetTarget -Force

        $msixPath = Join-Path $OutputRoot "ElectronicPointer-$version-$Rid.msix"
        if (Test-Path -LiteralPath $msixPath) {
            Remove-Item -LiteralPath $msixPath -Force
        }

        & $makeappx pack /d $msixStage /p $msixPath /o
        if ($LASTEXITCODE -ne 0) {
            throw "makeappx failed with exit code $LASTEXITCODE"
        }

        if ($script:CertificateThumbprint) {
            Invoke-Signature -TargetPath $msixPath -WithTimestamp
        }
    }
}

# --- nsis installer -------------------------------------------------------------
if ($SkipNsis) {
    Write-Warning "-SkipNsis was passed; the installer was not built."
}
else {
    $makensis = Find-Nsis
    if (-not $makensis) {
        Write-Warning "makensis not found; skipping the installer. The zip is still usable."
    }
    elseif (-not (Test-NsisTarget -Nsis $makensis -Target $nsisTarget)) {
        # A stock NSIS always carries the x86-unicode stub, so this only fires on an install
        # that was pruned by hand. Better to ship without the installer than to fail a build
        # for a reason the user cannot act on mid-run.
        Write-Warning "this NSIS install has no $nsisTarget stub; skipping the $Rid installer. Reinstall NSIS with the full target set to get it."
    }
    else {
        $installerPath = Join-Path $OutputRoot "ElectronicPointer-$version-$Rid-setup.exe"
        if (Test-Path -LiteralPath $installerPath) {
            Remove-Item -LiteralPath $installerPath -Force
        }

        $installerScript = Join-Path $PSScriptRoot 'installer.nsi'
        $installerIcon = Join-Path $assetRoot 'electronicpointer.ico'
        $winVersion = "1.0.$msixBuild.$msixRevision"

        # Every value that can contain a space is quoted, because makensis splits its
        # command line on whitespace and a repository path with a space would truncate
        # the payload directory into a wrong path.
        $makensisArgs = @(
            '/INPUTCHARSET', 'UTF8'
            "/DVERSION=$version"
            "/DWINVERSION=$winVersion"
            "/DAPPARCH=$arch"
            "/DPAYLOAD=$portableStage"
            "/DICON=$installerIcon"
            "/DOUTFILE=$installerPath"
            $installerScript
        )

        Write-Host "installer: $nsisTarget payload=$portableStage version=$winVersion"
        & $makensis @makensisArgs
        if ($LASTEXITCODE -ne 0) {
            throw "makensis failed on $Rid with exit code $LASTEXITCODE"
        }

        if ($script:CertificateThumbprint) {
            # An NSIS installer seals its payload, and the signature rides in the overlay
            # signtool appends, so the pack has to happen before this call.
            Invoke-Signature -TargetPath $installerPath -WithTimestamp
        }
    }
}

Write-Host ""
Write-Host "Windows artifacts in ${OutputRoot}:"
Get-ChildItem -LiteralPath $OutputRoot -File |
    Where-Object { $_.Name -like "*$Rid*" } |
    ForEach-Object {
        Write-Host ("  {0}  ({1:N1} MB)" -f $_.Name, ($_.Length / 1MB))
    }

if (-not $CertificatePath) {
    if ($script:CertificateThumbprint) {
        Write-Warning "Everything was signed with a self-signed certificate. Windows will ask the user to trust 'Mutantcat Working Group' once; a real release should pass -CertificatePath with a certificate issued by a trusted authority."
    }
    else {
        Write-Warning "Nothing was signed. An unsigned MSIX cannot be installed, NSIS will warn when the installer starts and SmartScreen will warn about the portable zip, so pass a certificate or let the script mint one before shipping a release."
    }
}
