[CmdletBinding()]
param(
    [switch]$SkipTests,
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',
    [string]$Version = '1.0.0-dev',
    [string]$OutputDirectory = 'dist',
    [string]$UpdateBaseUrl = $env:CLOUDFLARE_R2_UPDATER_BASE_URL,
    [string]$UpdateManifestPublicKey = $env:CLOUDFLARE_R2_UPDATER_MANIFEST_PUBLIC_KEY,
    [switch]$RequireSignedUpdate,
    [string]$UpdatePrefix = 'cloudflare-r2-uploader',
    [string]$InnoSetupCompiler = $env:INNO_SETUP_COMPILER,
    [switch]$KeepStaging
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0
. (Join-Path $PSScriptRoot 'InnoSetupRegistration.ps1')

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$solutionPath = Join-Path $repositoryRoot 'CloudflareR2Uploader.sln'
$applicationProject = Join-Path $repositoryRoot 'src\CloudflareR2Uploader.Wpf\CloudflareR2Uploader.Wpf.csproj'
$testResults = Join-Path $repositoryRoot 'TestResults'
$resolvedOutput = if ([System.IO.Path]::IsPathRooted($OutputDirectory)) { [System.IO.Path]::GetFullPath($OutputDirectory) } else { [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputDirectory)) }
$stagingRoot = Join-Path $repositoryRoot 'artifacts\release'
$packageName = "CloudflareR2Uploader-v$Version"
$stagingDirectory = Join-Path $stagingRoot ($packageName + '-payload')
$installerPath = Join-Path $resolvedOutput ($packageName + '-Setup.exe')
$installerScript = Join-Path $repositoryRoot 'installer\CloudflareR2Uploader.iss'
$iconGenerator = Join-Path $PSScriptRoot 'Generate-BrandIcon.ps1'

# Documentation assets shipped beside the README inside the install directory. These live
# under docs/ rather than under artifacts/, because artifacts/ is disposable build output
# that a clean build is expected to delete wholesale.
$readmeImageRelative = 'docs\images\cloudflare-r2-uploader.png'

$publishableSemanticVersionPattern = '^(?<major>0|[1-9][0-9]*)\.(?<minor>0|[1-9][0-9]*)\.(?<patch>0|[1-9][0-9]*)(?:-(?:(?:0|[1-9][0-9]*|[0-9A-Za-z-]*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9][0-9]*|[0-9A-Za-z-]*[A-Za-z-][0-9A-Za-z-]*))*))?(?![\s\S])'
if ($Version.Length -gt 128 -or $Version -notmatch $publishableSemanticVersionPattern) {
    throw "Version '$Version' is not supported. Use SemVer such as 1.2.3 or 1.2.3-beta.1."
}
$versionMajor = [uint16]0
$versionMinor = [uint16]0
$versionPatch = [uint16]0
if (-not [uint16]::TryParse($Matches.major, [Globalization.NumberStyles]::None, [Globalization.CultureInfo]::InvariantCulture, [ref]$versionMajor) -or
    -not [uint16]::TryParse($Matches.minor, [Globalization.NumberStyles]::None, [Globalization.CultureInfo]::InvariantCulture, [ref]$versionMinor) -or
    -not [uint16]::TryParse($Matches.patch, [Globalization.NumberStyles]::None, [Globalization.CultureInfo]::InvariantCulture, [ref]$versionPatch)) {
    throw "Version '$Version' cannot be represented by Windows file-version metadata."
}
$numericVersion = '{0}.{1}.{2}.0' -f $versionMajor, $versionMinor, $versionPatch

$hasUpdateUrl = -not [string]::IsNullOrWhiteSpace($UpdateBaseUrl)
$hasUpdateKey = -not [string]::IsNullOrWhiteSpace($UpdateManifestPublicKey)
if ($RequireSignedUpdate -and -not ($hasUpdateUrl -and $hasUpdateKey)) {
    throw 'A signed update URL and public key are required for this release build.'
}
if ($hasUpdateUrl -ne $hasUpdateKey) {
    throw 'UpdateBaseUrl and UpdateManifestPublicKey must be supplied together.'
}

if ($hasUpdateUrl) {
    $parsedUpdateBase = $null
    if (-not [Uri]::TryCreate($UpdateBaseUrl.Trim(), [UriKind]::Absolute, [ref]$parsedUpdateBase) -or $parsedUpdateBase.Scheme -ne 'https' -or -not [string]::IsNullOrEmpty($parsedUpdateBase.UserInfo) -or -not [string]::IsNullOrEmpty($parsedUpdateBase.Query) -or -not [string]::IsNullOrEmpty($parsedUpdateBase.Fragment)) {
        throw 'UpdateBaseUrl must be an absolute HTTPS URL without embedded credentials, a query, or a fragment.'
    }
    $UpdateBaseUrl = $parsedUpdateBase.AbsoluteUri.TrimEnd('/')
}
if ($UpdatePrefix -notmatch '^[0-9A-Za-z][0-9A-Za-z._/-]*$' -or $UpdatePrefix.Contains('..')) { throw 'UpdatePrefix contains unsupported path characters.' }
$UpdatePrefix = $UpdatePrefix.Trim('/')

$requiredInnoSetupVersion = '6.7.1'
$InnoSetupCompiler = Resolve-InnoSetupCompilerRegistration `
    -RequestedCompiler $InnoSetupCompiler `
    -RequiredVersion $requiredInnoSetupVersion

$dotnet = Get-Command dotnet -ErrorAction Stop

if ($hasUpdateUrl) {
    $signerProject = Join-Path $repositoryRoot 'tools\CloudflareR2Uploader.UpdateSigner\CloudflareR2Uploader.UpdateSigner.csproj'
    $signerAssembly = Join-Path $repositoryRoot 'tools\CloudflareR2Uploader.UpdateSigner\bin\Release\net10.0\CloudflareR2Uploader.UpdateSigner.dll'
    & $dotnet.Source build $signerProject -c Release --nologo -v:minimal
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $signerAssembly -PathType Leaf)) {
        throw 'The update public-key validator could not be built.'
    }
    $keyValidationOutput = & $dotnet.Source $signerAssembly 'validate-public-key' '--public-key' $UpdateManifestPublicKey 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw 'UpdateManifestPublicKey must be a canonical RSA SubjectPublicKeyInfo of at least 2048 bits.'
    }
}

# Every repository file the package needs is checked before anything is built, so a missing
# input fails in a second with an actionable message instead of surfacing as a raw
# Copy-Item error several minutes into the run.
$requiredInputs = @(
    'LICENSE',
    'README.md',
    'THIRD-PARTY-NOTICES.md',
    'installer\CloudflareR2Uploader.iss',
    $readmeImageRelative
)
$missingInputs = @($requiredInputs | Where-Object { -not (Test-Path -LiteralPath (Join-Path $repositoryRoot $_) -PathType Leaf) })
if ($missingInputs.Count -gt 0) {
    throw ("These files are required to build the package but are missing from the repository: " +
        ($missingInputs -join ', ') +
        ". Restore them (git checkout -- <path>) and run this script again.")
}

try {
    & $iconGenerator
    if (-not $?) { throw 'The icon generator failed.' }
    & $dotnet.Source restore $solutionPath --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit code $LASTEXITCODE." }
    & $dotnet.Source build $solutionPath -c $Configuration --no-restore --nologo -v:minimal "/p:Version=$Version" "/p:AssemblyVersion=$numericVersion" "/p:FileVersion=$numericVersion" "/p:InformationalVersion=$Version"
    if ($LASTEXITCODE -ne 0) { throw "The $Configuration build failed with exit code $LASTEXITCODE." }

    if (-not $SkipTests) {
        if (Test-Path -LiteralPath $testResults) { Remove-Item -LiteralPath $testResults -Recurse -Force }
        New-Item -ItemType Directory -Path $testResults | Out-Null
        & $dotnet.Source test $solutionPath -c $Configuration --no-build --nologo -v:minimal --logger trx --results-directory $testResults
        if ($LASTEXITCODE -ne 0) { throw "The automated tests failed with exit code $LASTEXITCODE." }
    }

    if (Test-Path -LiteralPath $stagingDirectory) { Remove-Item -LiteralPath $stagingDirectory -Recurse -Force }
    & $dotnet.Source publish $applicationProject -c $Configuration -r win-x64 --self-contained false --no-restore --nologo -v:minimal -o $stagingDirectory "/p:Version=$Version" "/p:AssemblyVersion=$numericVersion" "/p:FileVersion=$numericVersion" "/p:InformationalVersion=$Version" /p:PublishSingleFile=false
    if ($LASTEXITCODE -ne 0) { throw "The WPF publish failed with exit code $LASTEXITCODE." }
    Get-ChildItem -LiteralPath $stagingDirectory -Recurse -Filter '*.pdb' | Remove-Item -Force
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'README.md') -Destination $stagingDirectory
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'THIRD-PARTY-NOTICES.md') -Destination $stagingDirectory
    Copy-Item -LiteralPath (Join-Path $repositoryRoot 'LICENSE') -Destination $stagingDirectory
    # The image keeps its repository-relative path inside the payload so the README's own
    # link still resolves when it is read from the install directory.
    $packageImage = Join-Path $stagingDirectory $readmeImageRelative
    New-Item -ItemType Directory -Path (Split-Path -Parent $packageImage) -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $repositoryRoot $readmeImageRelative) -Destination $packageImage
    if ($hasUpdateUrl) {
        $manifestUrl = $UpdateBaseUrl + '/' + $UpdatePrefix + '/manifest.json'
        $source = [ordered]@{
            manifestUrl = $manifestUrl
            manifestPublicKey = $UpdateManifestPublicKey
        } | ConvertTo-Json
        [System.IO.File]::WriteAllText((Join-Path $stagingDirectory 'update-source.json'), $source + "`r`n", [System.Text.UTF8Encoding]::new($false))
        Write-Host "Update channel: $manifestUrl"
    } else {
        Write-Warning 'No update URL/public-key pair was supplied. This build will not perform automatic checks.'
    }

    $expected = @(
        'CloudflareR2Uploader.exe',
        'CloudflareR2Uploader.dll',
        'CloudflareR2Uploader.deps.json',
        'CloudflareR2Uploader.runtimeconfig.json',
        'LICENSE',
        'README.md',
        'THIRD-PARTY-NOTICES.md',
        $readmeImageRelative,
        'runtimes\win-x86\native\WebView2Loader.dll',
        'runtimes\win-x64\native\WebView2Loader.dll',
        'runtimes\win-arm64\native\WebView2Loader.dll'
    )
    foreach ($relative in $expected) {
        if (-not (Test-Path -LiteralPath (Join-Path $stagingDirectory $relative) -PathType Leaf)) { throw "Required installer payload file is missing: $relative" }
    }
    if (Get-ChildItem -LiteralPath $stagingDirectory -Recurse -Filter '*.pdb') { throw 'The end-user installer payload contains PDB files.' }

    Add-Type -AssemblyName System.Drawing
    $applicationExecutable = Join-Path $stagingDirectory 'CloudflareR2Uploader.exe'
    $applicationIcon = [System.Drawing.Icon]::ExtractAssociatedIcon($applicationExecutable)
    if ($null -eq $applicationIcon) { throw 'Windows could not extract the embedded application icon.' }
    $applicationIcon.Dispose()

    if (-not (Test-Path -LiteralPath $resolvedOutput)) { New-Item -ItemType Directory -Path $resolvedOutput | Out-Null }
    Get-ChildItem -LiteralPath $resolvedOutput -File | Where-Object {
        $_.Name -like 'CloudflareR2Uploader-v*-Setup.exe' -or
        $_.Name -like 'CloudflareR2Uploader-v*-win.zip' -or
        $_.Name -like 'CloudflareR2Uploader-v*-win.zip.sha256'
    } | Remove-Item -Force
    $outputBaseFilename = [System.IO.Path]::GetFileNameWithoutExtension($installerPath)
    & $InnoSetupCompiler "/DAppVersion=$Version" "/DNumericVersion=$numericVersion" "/DPayloadDir=$stagingDirectory" "/DOutputDir=$resolvedOutput" "/DOutputBaseFilename=$outputBaseFilename" "/DRepositoryRoot=$repositoryRoot" $installerScript
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed with exit code $LASTEXITCODE." }
    if (-not (Test-Path -LiteralPath $installerPath -PathType Leaf)) { throw 'Inno Setup did not produce the expected installer executable.' }

    $installerIcon = [System.Drawing.Icon]::ExtractAssociatedIcon($installerPath)
    if ($null -eq $installerIcon) { throw 'Windows could not extract the embedded installer icon.' }
    $installerIcon.Dispose()
    $versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($installerPath)
    if ($versionInfo.ProductName.Trim() -ne 'Cloudflare R2 Uploader' -or
        $versionInfo.FileDescription.Trim() -ne 'Cloudflare R2 Uploader Setup' -or
        $versionInfo.FileMajorPart -ne $versionMajor -or
        $versionInfo.FileMinorPart -ne $versionMinor -or
        $versionInfo.FileBuildPart -ne $versionPatch -or
        $versionInfo.FilePrivatePart -ne 0) {
        throw ('The generated EXE does not contain the expected Inno Setup release metadata. ' +
            'Actual numeric file version: {0}.{1}.{2}.{3}.' -f
            $versionInfo.FileMajorPart,
            $versionInfo.FileMinorPart,
            $versionInfo.FileBuildPart,
            $versionInfo.FilePrivatePart)
    }

    $releaseFiles = @(Get-ChildItem -LiteralPath $resolvedOutput -File | Where-Object { $_.Name -like 'CloudflareR2Uploader-v*' })
    if ($releaseFiles.Count -ne 1 -or $releaseFiles[0].Extension -ne '.exe') { throw 'The release output must contain exactly one versioned installer EXE.' }
    $hash = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Host ('Release installer: {0} ({1:N0} bytes)' -f $installerPath, (Get-Item -LiteralPath $installerPath).Length)
    Write-Host ('SHA-256: {0}' -f $hash)
}
finally {
    if (-not $KeepStaging) {
        if (Test-Path -LiteralPath $stagingDirectory) { Remove-Item -LiteralPath $stagingDirectory -Recurse -Force }
    }
}
