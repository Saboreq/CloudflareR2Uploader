[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,
    [Parameter(Mandatory = $true)]
    [string]$BucketName,
    [Parameter(Mandatory = $true)]
    [string]$PublicBaseUrl,
    [string]$Prefix = 'cloudflare-r2-uploader',
    [string]$ReleaseNotes = '',
    [switch]$SkipBuild,
    [string]$InstallerPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0
$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))

if ($Version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$') { throw 'Version must be a supported SemVer value.' }
if ($BucketName -notmatch '^[a-z0-9][a-z0-9-]{1,61}[a-z0-9]$') { throw 'BucketName must be a valid 3-63 character R2 bucket name.' }
if ($Prefix -notmatch '^[0-9A-Za-z][0-9A-Za-z._/-]*$' -or $Prefix.Contains('..')) { throw 'Prefix contains unsupported path characters.' }
$Prefix = $Prefix.Trim('/')
$baseUri = $null
if (-not [Uri]::TryCreate($PublicBaseUrl.Trim(), [UriKind]::Absolute, [ref]$baseUri) -or $baseUri.Scheme -ne 'https' -or -not [string]::IsNullOrEmpty($baseUri.UserInfo)) {
    throw 'PublicBaseUrl must be an absolute HTTPS URL without embedded credentials.'
}
$baseUrl = $baseUri.AbsoluteUri.TrimEnd('/')

if (-not $SkipBuild) {
    & (Join-Path $repositoryRoot 'build\Build-Release.ps1') -Version $Version -UpdateBaseUrl $baseUrl -UpdatePrefix $Prefix
    if ($LASTEXITCODE -ne 0) { throw "Release build failed with exit code $LASTEXITCODE." }
}

if ([string]::IsNullOrWhiteSpace($InstallerPath)) { $InstallerPath = Join-Path $repositoryRoot "dist\CloudflareR2Uploader-v$Version-Setup.exe" }
$installer = [System.IO.Path]::GetFullPath($InstallerPath)
if (-not (Test-Path -LiteralPath $installer -PathType Leaf)) { throw "Installer not found: $installer" }

$installerName = [System.IO.Path]::GetFileName($installer)
$installerKey = "$Prefix/releases/$Version/$installerName"
$manifestKey = "$Prefix/manifest.json"
$installerUrl = "$baseUrl/$installerKey"
$manifestUrl = "$baseUrl/$manifestKey"
$installerInfo = Get-Item -LiteralPath $installer
$installerHash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
$manifest = [ordered]@{
    schemaVersion = 1
    version = $Version
    installerUrl = $installerUrl
    sha256 = $installerHash
    sizeBytes = $installerInfo.Length
    publishedUtc = [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ')
    notes = $ReleaseNotes
}
$manifestDirectory = Join-Path $repositoryRoot 'artifacts\update'
if (-not (Test-Path -LiteralPath $manifestDirectory)) { New-Item -ItemType Directory -Path $manifestDirectory | Out-Null }
$manifestPath = Join-Path $manifestDirectory 'manifest.json'
[System.IO.File]::WriteAllText($manifestPath, (($manifest | ConvertTo-Json -Depth 4) + "`r`n"), [System.Text.UTF8Encoding]::new($false))

function Invoke-Wrangler([string[]]$Arguments) {
    & npx --yes wrangler@latest @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Wrangler failed with exit code $LASTEXITCODE." }
}

# The versioned installer is immutable. The small manifest is uploaded last so clients
# never observe a version whose installer has not finished publishing.
Invoke-Wrangler @('r2', 'object', 'put', "$BucketName/$installerKey", '--remote', '--file', $installer, '--content-type', 'application/vnd.microsoft.portable-executable', '--cache-control', 'public, max-age=31536000, immutable')
Invoke-Wrangler @('r2', 'object', 'put', "$BucketName/$manifestKey", '--remote', '--file', $manifestPath, '--content-type', 'application/json; charset=utf-8', '--cache-control', 'no-store, no-cache, must-revalidate')

$verificationUrl = $manifestUrl + '?verify=' + [Guid]::NewGuid().ToString('N')
$published = Invoke-RestMethod -Uri $verificationUrl -Method Get -Headers @{ 'Cache-Control' = 'no-cache' }
if ($published.schemaVersion -ne 1 -or $published.version -ne $Version -or $published.sha256 -ne $installerHash -or $published.installerUrl -ne $installerUrl) {
    throw 'The public manifest verification did not match the release that was uploaded.'
}

Write-Host "Published installer: $installerUrl"
Write-Host "Published manifest:  $manifestUrl"
Write-Host "SHA-256:            $installerHash"
