[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,
    [Parameter(Mandatory = $true)]
    [string]$BucketName,
    [Parameter(Mandatory = $true)]
    [string]$PublicBaseUrl,
    [Parameter(Mandatory = $true)]
    [string]$SigningCertificatePath,
    [Parameter(Mandatory = $true)]
    [string]$SigningCertificatePasswordEnvironmentVariable,
    [string]$Prefix = 'cloudflare-r2-uploader',
    [string]$ReleaseNotes = '',
    [switch]$SkipBuild,
    [string]$InstallerPath,
    [ValidateRange(1, 300)]
    [int]$AuthenticatedReadTimeoutSeconds = 300
)

try {
    # The password is consumed by this invocation. Capture and remove it before validation,
    # command discovery, filesystem probing, or any child process can run.
    $signingCertificatePassword = [Environment]::GetEnvironmentVariable($SigningCertificatePasswordEnvironmentVariable)
    [Environment]::SetEnvironmentVariable($SigningCertificatePasswordEnvironmentVariable, $null)
    $publicationDirectory = $null
    $manifestRoot = $null
    $installerSourceLock = $null
    $installerSnapshotLock = $null
    $validatedInstallerHash = $null

    $ErrorActionPreference = 'Stop'
    Set-StrictMode -Version 2.0

    $repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
    $signerProject = Join-Path $repositoryRoot 'tools\CloudflareR2Uploader.UpdateSigner\CloudflareR2Uploader.UpdateSigner.csproj'
    $signerAssembly = Join-Path $repositoryRoot 'tools\CloudflareR2Uploader.UpdateSigner\bin\Release\net10.0\CloudflareR2Uploader.UpdateSigner.dll'
    $maximumInstallerBytes = 256L * 1024L * 1024L
    $maximumManifestBytes = 64L * 1024L

    function Test-PathsIdentifySameFile([string]$First, [string]$Second) {
        $comparison = if ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) {
            [StringComparison]::OrdinalIgnoreCase
        }
        else {
            [StringComparison]::Ordinal
        }
        return [string]::Equals(
            [System.IO.Path]::TrimEndingDirectorySeparator([System.IO.Path]::GetFullPath($First)),
            [System.IO.Path]::TrimEndingDirectorySeparator([System.IO.Path]::GetFullPath($Second)),
            $comparison)
    }

    function Get-StreamSha256([System.IO.FileStream]$Stream) {
        $originalPosition = $Stream.Position
        try {
            $Stream.Position = 0
            $sha256 = [System.Security.Cryptography.SHA256]::Create()
            try {
                return [BitConverter]::ToString($sha256.ComputeHash($Stream)).Replace('-', '').ToLowerInvariant()
            }
            finally {
                $sha256.Dispose()
            }
        }
        finally {
            $Stream.Position = $originalPosition
        }
    }

    function Assert-CredibleInstaller {
        param(
            [Parameter(Mandatory = $true)]
            [string]$Path,
            [Parameter(Mandatory = $true)]
            [string]$CertificatePath,
            [System.IO.FileStream]$LockedStream
        )

        if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw 'The release installer is missing.' }
        if (-not [string]::Equals([System.IO.Path]::GetExtension($Path), '.exe', [StringComparison]::OrdinalIgnoreCase)) {
            throw 'The release installer must be an EXE file.'
        }

        $installerInfo = Get-Item -LiteralPath $Path
        if ($installerInfo.Length -le 0 -or $installerInfo.Length -gt $maximumInstallerBytes) {
            throw 'The release installer size is outside the supported range.'
        }

        $stream = $LockedStream
        $ownsStream = $null -eq $stream
        if ($ownsStream) {
            $stream = [System.IO.File]::Open(
                $Path, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read)
        }
        $originalPosition = $stream.Position
        try {
            $stream.Position = 0
            if ($stream.Length -lt 68) { throw 'The release installer is not a valid PE file.' }
            $reader = [System.IO.BinaryReader]::new($stream, [System.Text.Encoding]::UTF8, $true)
            try {
                if ($reader.ReadUInt16() -ne 0x5A4D) { throw 'The release installer is not a valid PE file.' }
                $stream.Position = 0x3c
                $peOffset = $reader.ReadUInt32()
                if ($peOffset -gt ($stream.Length - 4)) { throw 'The release installer is not a valid PE file.' }
                $stream.Position = $peOffset
                if ($reader.ReadUInt32() -ne 0x00004550) { throw 'The release installer is not a valid PE file.' }
            }
            finally {
                $reader.Dispose()
            }
        }
        finally {
            if ($ownsStream) { $stream.Dispose() }
            else { $stream.Position = $originalPosition }
        }

        $installerDigest = if ($null -ne $LockedStream) {
            Get-StreamSha256 $LockedStream
        }
        else {
            (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
        }
        $certificateDigest = (Get-FileHash -LiteralPath $CertificatePath -Algorithm SHA256).Hash
        if ($installerDigest -ceq $certificateDigest.ToLowerInvariant()) {
            throw 'The installer must not contain the signing certificate bytes.'
        }
    }

    function Invoke-Signer {
        param(
            [Parameter(Mandatory = $true)]
            [string[]]$Arguments,
            [switch]$CaptureOutput
        )

        if ($CaptureOutput) {
            $captured = (& $dotnet.Source $signerAssembly @Arguments | Out-String).Trim()
            if ($LASTEXITCODE -ne 0) { throw 'The update signer command failed.' }
            return $captured
        }

        & $dotnet.Source $signerAssembly @Arguments
        if ($LASTEXITCODE -ne 0) { throw 'The update signer command failed.' }
    }

    function Invoke-CertificateSigner {
        param(
            [Parameter(Mandatory = $true)]
            [string[]]$Arguments,
            [switch]$CaptureOutput
        )

        try {
            [Environment]::SetEnvironmentVariable(
                $SigningCertificatePasswordEnvironmentVariable,
                $signingCertificatePassword)
            if ($CaptureOutput) { return Invoke-Signer -Arguments $Arguments -CaptureOutput }
            Invoke-Signer -Arguments $Arguments
        }
        finally {
            [Environment]::SetEnvironmentVariable($SigningCertificatePasswordEnvironmentVariable, $null)
        }
    }

    function Invoke-Wrangler([string[]]$Arguments) {
        & $npx.Source --yes wrangler@4.120.0 @Arguments
        if ($LASTEXITCODE -ne 0) { throw "Wrangler failed with exit code $LASTEXITCODE." }
    }

    function Receive-WranglerObject(
        [string]$ObjectPath,
        [string]$Destination,
        [long]$MaximumBytes) {
        if ($MaximumBytes -le 0) { throw 'The authenticated R2 object size limit is invalid.' }
        if (Test-Path -LiteralPath $Destination) { Remove-Item -LiteralPath $Destination -Force }

        $process = [System.Diagnostics.Process]::new()
        $destinationStream = $null
        $diagnosticTask = $null
        $readTask = $null
        $processStarted = $false
        $retainDestination = $false
        $timeoutMilliseconds = [long]$AuthenticatedReadTimeoutSeconds * 1000L
        $timeoutWatch = [System.Diagnostics.Stopwatch]::StartNew()
        try {
            $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
            $startInfo.UseShellExecute = $false
            $startInfo.CreateNoWindow = $true
            $startInfo.RedirectStandardOutput = $true
            $startInfo.RedirectStandardError = $true
            $wranglerArguments = @(
                '--yes', 'wrangler@4.120.0', 'r2', 'object', 'get',
                $ObjectPath, '--remote', '--pipe')
            $commandExtension = [System.IO.Path]::GetExtension($npx.Source)
            if ($commandExtension -ieq '.cmd' -or $commandExtension -ieq '.bat') {
                $startInfo.FileName = $env:ComSpec
                $startInfo.ArgumentList.Add('/d')
                $startInfo.ArgumentList.Add('/s')
                $startInfo.ArgumentList.Add('/c')
                $quotedCommand = '""' + $npx.Source.Replace('"', '""') + '"'
                foreach ($argument in $wranglerArguments) {
                    if ($argument.Contains('"')) { throw 'The authenticated R2 object path is invalid.' }
                    $quotedCommand += ' "' + $argument + '"'
                }
                $quotedCommand += '"'
                $startInfo.ArgumentList.Add($quotedCommand)
            }
            else {
                $startInfo.FileName = $npx.Source
                foreach ($argument in $wranglerArguments) { $startInfo.ArgumentList.Add($argument) }
            }

            $process.StartInfo = $startInfo
            if (-not $process.Start()) { throw 'The authenticated R2 reader could not be started.' }
            $processStarted = $true
            $diagnosticTask = $process.StandardError.ReadToEndAsync()
            $destinationStream = [System.IO.FileStream]::new(
                $Destination,
                [System.IO.FileMode]::CreateNew,
                [System.IO.FileAccess]::Write,
                [System.IO.FileShare]::None,
                81920,
                [System.IO.FileOptions]::WriteThrough)
            $buffer = [byte[]]::new(81920)
            $totalBytes = 0L
            while ($true) {
                $readTask = $process.StandardOutput.BaseStream.ReadAsync(
                    $buffer, 0, $buffer.Length)
                $remainingMilliseconds = $timeoutMilliseconds - $timeoutWatch.ElapsedMilliseconds
                if ($remainingMilliseconds -le 0 -or
                    -not $readTask.Wait([int][Math]::Min([int]::MaxValue, $remainingMilliseconds))) {
                    throw [TimeoutException]::new('The authenticated R2 object read exceeded its deadline.')
                }
                $read = $readTask.GetAwaiter().GetResult()
                $readTask = $null
                if ($read -eq 0) { break }
                $totalBytes += $read
                if ($totalBytes -gt $MaximumBytes) {
                    throw 'The authenticated R2 object is larger than allowed.'
                }
                $destinationStream.Write($buffer, 0, $read)
            }
            $destinationStream.Flush($true)
            $destinationStream.Dispose()
            $destinationStream = $null
            $remainingMilliseconds = $timeoutMilliseconds - $timeoutWatch.ElapsedMilliseconds
            if ($remainingMilliseconds -le 0 -or
                -not $process.WaitForExit([int][Math]::Min([int]::MaxValue, $remainingMilliseconds))) {
                throw [TimeoutException]::new('The authenticated R2 process exceeded its deadline.')
            }
            $remainingMilliseconds = $timeoutMilliseconds - $timeoutWatch.ElapsedMilliseconds
            if ($remainingMilliseconds -le 0 -or
                -not $diagnosticTask.Wait([int][Math]::Min([int]::MaxValue, $remainingMilliseconds))) {
                throw [TimeoutException]::new('The authenticated R2 diagnostics exceeded their deadline.')
            }
            $diagnostic = $diagnosticTask.GetAwaiter().GetResult()

            if ($process.ExitCode -eq 0) {
                if ($totalBytes -le 0) {
                    throw 'Wrangler reported success without downloading the remote object.'
                }
                $retainDestination = $true
                return $true
            }

            $diagnostic = [regex]::Replace($diagnostic, "$([char]27)\[[0-9;?]*[ -/]*[@-~]", '')
            $wranglerMissingKeyDiagnostic = 'The specified key does not exist.'
            $missingKey = @($diagnostic -split "\r?\n" | Where-Object {
                $line = $_.Trim()
                $line = $line -replace '^✘\s*', ''
                $line = $line -replace '^\[ERROR\]\s*', ''
                $line -ceq $wranglerMissingKeyDiagnostic
            })
            if ($missingKey.Count -gt 0) { return $false }
            throw 'The remote object could not be verified.'
        }
        catch [TimeoutException] {
            throw 'The authenticated R2 object download timed out.'
        }
        finally {
            if ($null -ne $destinationStream) { $destinationStream.Dispose() }
            if ($processStarted) {
                try {
                    if (-not $process.HasExited) {
                        $process.Kill($true)
                    }
                }
                catch [InvalidOperationException] { }
                catch [System.ComponentModel.Win32Exception] { }
                try { $null = $process.WaitForExit(5000) }
                catch [InvalidOperationException] { }
                if ($null -ne $readTask) {
                    try { $null = $readTask.Wait(5000) }
                    catch [AggregateException] { }
                }
                if ($null -ne $diagnosticTask) {
                    try { $null = $diagnosticTask.Wait(5000) }
                    catch [AggregateException] { }
                }
            }
            $process.Dispose()
            if (-not $retainDestination -and (Test-Path -LiteralPath $Destination)) {
                Remove-Item -LiteralPath $Destination -Force
            }
        }
    }

    function Get-UnicodeScalarCount([AllowEmptyString()][string]$Value) {
        $count = 0
        for ($index = 0; $index -lt $Value.Length; $index++) {
            $current = $Value[$index]
            if ([char]::IsHighSurrogate($current)) {
                if ($index + 1 -ge $Value.Length -or -not [char]::IsLowSurrogate($Value[$index + 1])) {
                    throw 'ReleaseNotes must contain valid Unicode scalar values.'
                }
                $index++
            }
            elseif ([char]::IsLowSurrogate($current)) {
                throw 'ReleaseNotes must contain valid Unicode scalar values.'
            }
            $count++
        }
        return $count
    }

    function Receive-PublicHttpsObject([string]$Uri, [string]$Destination, [long]$MaximumBytes) {
        $requestedUri = $null
        if (-not [Uri]::TryCreate($Uri, [UriKind]::Absolute, [ref]$requestedUri) -or
            $requestedUri.Scheme -cne 'https' -or
            -not [string]::IsNullOrEmpty($requestedUri.UserInfo) -or
            -not [string]::IsNullOrEmpty($requestedUri.Fragment)) {
            throw 'The public verification URL is not a safe HTTPS URL.'
        }

        $curlErrorPath = $Destination + '.curl.stderr'
        if (Test-Path -LiteralPath $Destination) { Remove-Item -LiteralPath $Destination -Force }
        if (Test-Path -LiteralPath $curlErrorPath) { Remove-Item -LiteralPath $curlErrorPath -Force }
        try {
            $curlArguments = @(
                '--fail', '--silent', '--show-error', '--location', '--max-redirs', '5',
                '--connect-timeout', '15', '--max-time', '300',
                '--proto', '=https', '--proto-redir', '=https',
                '--max-filesize', $MaximumBytes.ToString([Globalization.CultureInfo]::InvariantCulture),
                '--output', $Destination, '--write-out', '%{url_effective}', '--', $requestedUri.AbsoluteUri)
            $effectiveOutput = (& $curl.Source @curlArguments 2> $curlErrorPath | Out-String).Trim()
            $curlExitCode = $LASTEXITCODE
            if ($curlExitCode -ne 0) {
                if (Test-Path -LiteralPath $Destination) { Remove-Item -LiteralPath $Destination -Force }
                throw 'The public update object could not be downloaded.'
            }
            if (-not (Test-Path -LiteralPath $Destination -PathType Leaf)) {
                throw 'The public update download did not produce a file.'
            }

            $download = Get-Item -LiteralPath $Destination
            if ($download.Length -le 0 -or $download.Length -gt $MaximumBytes) {
                Remove-Item -LiteralPath $Destination -Force
                throw 'The public update object size is outside the supported range.'
            }

            $effectiveUri = $null
            if (-not [Uri]::TryCreate($effectiveOutput, [UriKind]::Absolute, [ref]$effectiveUri) -or
                $effectiveUri.Scheme -cne 'https' -or
                -not [string]::IsNullOrEmpty($effectiveUri.UserInfo) -or
                -not [string]::IsNullOrEmpty($effectiveUri.Fragment)) {
                Remove-Item -LiteralPath $Destination -Force
                throw 'The public update download ended at an unsafe URL.'
            }
        }
        finally {
            if (Test-Path -LiteralPath $curlErrorPath) { Remove-Item -LiteralPath $curlErrorPath -Force }
        }
    }

    if ([string]::IsNullOrEmpty($signingCertificatePassword)) {
        throw 'The named signing-certificate password environment variable is missing or empty.'
    }
    $publishableSemanticVersionPattern = '^(?<major>0|[1-9][0-9]*)\.(?<minor>0|[1-9][0-9]*)\.(?<patch>0|[1-9][0-9]*)(?:-(?:(?:0|[1-9][0-9]*|[0-9A-Za-z-]*[A-Za-z-][0-9A-Za-z-]*)(?:\.(?:0|[1-9][0-9]*|[0-9A-Za-z-]*[A-Za-z-][0-9A-Za-z-]*))*))?(?![\s\S])'
    if ($Version.Length -gt 128 -or $Version -notmatch $publishableSemanticVersionPattern) { throw 'Version must be a supported SemVer value.' }
    $releaseNotesScalarCount = Get-UnicodeScalarCount $ReleaseNotes
    if ($releaseNotesScalarCount -gt 8000) { throw 'ReleaseNotes must contain at most 8000 Unicode scalar values.' }
    $versionMajor = [uint16]0
    $versionMinor = [uint16]0
    $versionPatch = [uint16]0
    if (-not [uint16]::TryParse($Matches.major, [Globalization.NumberStyles]::None, [Globalization.CultureInfo]::InvariantCulture, [ref]$versionMajor) -or
        -not [uint16]::TryParse($Matches.minor, [Globalization.NumberStyles]::None, [Globalization.CultureInfo]::InvariantCulture, [ref]$versionMinor) -or
        -not [uint16]::TryParse($Matches.patch, [Globalization.NumberStyles]::None, [Globalization.CultureInfo]::InvariantCulture, [ref]$versionPatch)) {
        throw 'Version cannot be represented by Windows file-version metadata.'
    }
    if ($BucketName -cnotmatch '^[a-z0-9][a-z0-9-]{1,61}[a-z0-9]$') { throw 'BucketName must be a valid 3-63 character R2 bucket name.' }
    if ($Prefix -notmatch '^[0-9A-Za-z][0-9A-Za-z._/-]*$' -or $Prefix.Contains('..')) { throw 'Prefix contains unsupported path characters.' }
    if ([string]::IsNullOrWhiteSpace($SigningCertificatePath)) { throw 'SigningCertificatePath is required.' }
    if ([string]::IsNullOrWhiteSpace($SigningCertificatePasswordEnvironmentVariable)) { throw 'SigningCertificatePasswordEnvironmentVariable is required.' }
    $Prefix = $Prefix.Trim('/')

    $baseUri = $null
    if (-not [Uri]::TryCreate($PublicBaseUrl.Trim(), [UriKind]::Absolute, [ref]$baseUri) -or
        $baseUri.Scheme -cne 'https' -or
        -not [string]::IsNullOrEmpty($baseUri.UserInfo) -or
        -not [string]::IsNullOrEmpty($baseUri.Query) -or
        -not [string]::IsNullOrEmpty($baseUri.Fragment)) {
        throw 'PublicBaseUrl must be an absolute HTTPS URL without embedded credentials, a query, or a fragment.'
    }
    $baseUrl = $baseUri.AbsoluteUri.TrimEnd('/')

    $signerCertificate = [System.IO.Path]::GetFullPath($SigningCertificatePath)
    $canonicalInstallerPath = Join-Path $repositoryRoot "dist\CloudflareR2Uploader-v$Version-Setup.exe"
    if (-not $SkipBuild -and -not [string]::IsNullOrWhiteSpace($InstallerPath)) {
        throw 'InstallerPath cannot be supplied unless SkipBuild is set.'
    }
    if (-not $SkipBuild -or [string]::IsNullOrWhiteSpace($InstallerPath)) {
        $InstallerPath = $canonicalInstallerPath
    }
    $installer = [System.IO.Path]::GetFullPath($InstallerPath)
    if (Test-PathsIdentifySameFile $installer $signerCertificate) {
        throw 'The release installer must be different from the signing certificate.'
    }
    if (-not (Test-Path -LiteralPath $signerCertificate -PathType Leaf)) { throw 'The signing certificate file is missing.' }
    if ($SkipBuild -and (Test-Path -LiteralPath $installer -PathType Leaf)) {
        Assert-CredibleInstaller $installer $signerCertificate
    }
    elseif ($SkipBuild) {
        throw 'The release installer is missing.'
    }

    # No child process can start before the installer/certificate path and byte guards above.
    $dotnet = @(Get-Command dotnet -CommandType Application -ErrorAction Stop)[0]
    $npx = @(Get-Command npx -CommandType Application -ErrorAction Stop)[0]
    $curl = @(Get-Command curl -CommandType Application -ErrorAction Stop)[0]

    & $dotnet.Source build $signerProject -c Release --nologo -v:minimal
    if ($LASTEXITCODE -ne 0) { throw 'The update signer build failed.' }
    if (-not (Test-Path -LiteralPath $signerAssembly -PathType Leaf)) { throw 'The update signer assembly is missing.' }

    if ($SkipBuild) {
        $installerSourceLock = [System.IO.File]::Open(
            $installer, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read)
        Invoke-Signer -Arguments @(
            'validate-installer-metadata', '--installer', $installer, '--version', $Version)
        $validatedInstallerHash = Get-StreamSha256 $installerSourceLock
    }

    $publicKey = Invoke-CertificateSigner -CaptureOutput -Arguments @(
        'export-public-key', '--pfx', $signerCertificate,
        '--password-env', $SigningCertificatePasswordEnvironmentVariable)
    if ([string]::IsNullOrWhiteSpace($publicKey)) { throw 'The update signer did not export a public key.' }

    if (-not $SkipBuild) {
        & (Join-Path $repositoryRoot 'build\Build-Release.ps1') `
            -Version $Version -UpdateBaseUrl $baseUrl -UpdateManifestPublicKey $publicKey `
            -UpdatePrefix $Prefix -RequireSignedUpdate
    }

    Assert-CredibleInstaller $installer $signerCertificate
    if (-not $SkipBuild) {
        $installerSourceLock = [System.IO.File]::Open(
            $installer, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read)
        Invoke-Signer -Arguments @(
            'validate-installer-metadata', '--installer', $installer, '--version', $Version)
        $validatedInstallerHash = Get-StreamSha256 $installerSourceLock
    }
    $installerName = [System.IO.Path]::GetFileName($installer)
    if ($installerName -notmatch '^[0-9A-Za-z][0-9A-Za-z._-]*\.exe$') {
        throw 'The installer filename contains unsupported URL characters.'
    }

    $manifestRoot = Join-Path $repositoryRoot 'artifacts\update'
    $publicationDirectory = Join-Path $manifestRoot ([Guid]::NewGuid().ToString('N'))
    [System.IO.Directory]::CreateDirectory($publicationDirectory) | Out-Null
    $installerSnapshot = Join-Path $publicationDirectory 'installer-snapshot.exe'
    $installerSnapshotLock = [System.IO.FileStream]::new(
        $installerSnapshot,
        [System.IO.FileMode]::CreateNew,
        [System.IO.FileAccess]::ReadWrite,
        [System.IO.FileShare]::Read,
        81920,
        [System.IO.FileOptions]::WriteThrough)
    $installerSourceLock.Position = 0
    $installerSourceLock.CopyTo($installerSnapshotLock)
    $installerSnapshotLock.Flush($true)
    $installerHash = Get-StreamSha256 $installerSnapshotLock
    if ($installerHash -cne $validatedInstallerHash) {
        throw 'The private installer snapshot did not match the validated release installer.'
    }
    $installerSourceLock.Dispose()
    $installerSourceLock = $null
    (Get-Item -LiteralPath $installerSnapshot).IsReadOnly = $true
    Assert-CredibleInstaller $installerSnapshot $signerCertificate $installerSnapshotLock

    $installerKey = "$Prefix/releases/$Version/$installerHash-$installerName"
    $manifestKey = "$Prefix/manifest.json"
    $installerUrl = "$baseUrl/$installerKey"
    $manifestUrl = "$baseUrl/$manifestKey"
    $manifestPath = Join-Path $publicationDirectory 'manifest.json'
    $preflightInstallerPath = Join-Path $publicationDirectory 'preflight-installer.bin'
    $publishedInstallerPath = Join-Path $publicationDirectory 'published-installer.bin'
    $publishedManifestPath = Join-Path $publicationDirectory 'published-manifest.json'
    $publicInstallerPath = Join-Path $publicationDirectory 'public-installer.bin'
    $publicManifestPath = Join-Path $publicationDirectory 'public-manifest.json'

    Invoke-CertificateSigner -Arguments @(
        'sign', '--pfx', $signerCertificate,
        '--password-env', $SigningCertificatePasswordEnvironmentVariable,
        '--version', $Version, '--installer', $installerSnapshot,
        '--installer-url', $installerUrl, '--notes', $ReleaseNotes,
        '--output', $manifestPath, '--held-read-lock', 'true')
    Invoke-Signer -Arguments @(
        'verify-installer', '--manifest', $manifestPath,
        '--public-key', $publicKey, '--installer', $installerSnapshot,
        '--held-read-lock', 'true')
    if ((Get-StreamSha256 $installerSnapshotLock) -cne $installerHash) {
        throw 'The private installer snapshot changed during signing.'
    }

    $expectedInstallerBytes = $installerSnapshotLock.Length
    $remoteInstallerExists = Receive-WranglerObject `
        "$BucketName/$installerKey" $preflightInstallerPath $expectedInstallerBytes
    if ($remoteInstallerExists) {
        try {
            Invoke-Signer -Arguments @(
                'verify-installer', '--manifest', $manifestPath,
                '--public-key', $publicKey, '--installer', $preflightInstallerPath)
        }
        catch {
            throw 'The immutable installer object contains different bytes.'
        }
    }
    else {
        Invoke-Wrangler @(
            'r2', 'object', 'put', "$BucketName/$installerKey", '--remote',
            '--file', $installerSnapshot,
            '--content-type', 'application/vnd.microsoft.portable-executable',
            '--cache-control', 'public, max-age=31536000, immutable')
    }

    if (-not (Receive-WranglerObject `
        "$BucketName/$installerKey" $publishedInstallerPath $expectedInstallerBytes)) {
        throw 'The immutable installer object disappeared before manifest publication.'
    }
    Invoke-Signer -Arguments @(
        'verify-installer', '--manifest', $manifestPath,
        '--public-key', $publicKey, '--installer', $publishedInstallerPath)

    # Verify the public immutable object before publishing the mutable manifest.
    Receive-PublicHttpsObject $installerUrl $publicInstallerPath $maximumInstallerBytes
    Invoke-Signer -Arguments @(
        'verify-installer', '--manifest', $manifestPath,
        '--public-key', $publicKey, '--installer', $publicInstallerPath)

    # Manifest publication is deliberately last among writes.
    Invoke-Wrangler @(
        'r2', 'object', 'put', "$BucketName/$manifestKey", '--remote',
        '--file', $manifestPath,
        '--content-type', 'application/json; charset=utf-8',
        '--cache-control', 'no-store, no-cache, must-revalidate')

    # Authenticate the R2 read-back before trusting the public endpoint.
    $expectedManifestBytes = (Get-Item -LiteralPath $manifestPath).Length
    if (-not (Receive-WranglerObject `
        "$BucketName/$manifestKey" $publishedManifestPath $expectedManifestBytes)) {
        throw 'The published manifest could not be downloaded for verification.'
    }
    Invoke-Signer -Arguments @('verify', '--manifest', $publishedManifestPath, '--public-key', $publicKey)
    $expectedManifestHash = (Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash
    $publishedManifestHash = (Get-FileHash -LiteralPath $publishedManifestPath -Algorithm SHA256).Hash
    if ($publishedManifestHash -cne $expectedManifestHash) {
        throw 'The published manifest did not match the signed manifest that was uploaded.'
    }

    $cacheBustedManifestUrl = "${manifestUrl}?cacheBust=$([Uri]::EscapeDataString([Guid]::NewGuid().ToString('N')))"
    Receive-PublicHttpsObject $cacheBustedManifestUrl $publicManifestPath $maximumManifestBytes
    Invoke-Signer -Arguments @('verify', '--manifest', $publicManifestPath, '--public-key', $publicKey)
    $publicManifestHash = (Get-FileHash -LiteralPath $publicManifestPath -Algorithm SHA256).Hash
    if ($publicManifestHash -cne $expectedManifestHash) {
        throw 'The public manifest did not match the signed manifest that was uploaded.'
    }

    Write-Host "Published installer: $installerUrl"
    Write-Host "Published manifest:  $manifestUrl"
}
finally {
    if (-not [string]::IsNullOrWhiteSpace($SigningCertificatePasswordEnvironmentVariable)) {
        [Environment]::SetEnvironmentVariable($SigningCertificatePasswordEnvironmentVariable, $null)
    }
    $signingCertificatePassword = $null

    $publicationDirectoryValue = Get-Variable -Name publicationDirectory -ValueOnly -ErrorAction SilentlyContinue
    $manifestRootValue = Get-Variable -Name manifestRoot -ValueOnly -ErrorAction SilentlyContinue
    $sourceLockValue = Get-Variable -Name installerSourceLock -ValueOnly -ErrorAction SilentlyContinue
    $snapshotLockValue = Get-Variable -Name installerSnapshotLock -ValueOnly -ErrorAction SilentlyContinue
    if ($null -ne $sourceLockValue) { $sourceLockValue.Dispose() }
    if ($null -ne $snapshotLockValue) { $snapshotLockValue.Dispose() }
    if (-not [string]::IsNullOrWhiteSpace($publicationDirectoryValue) -and
        (Test-Path -LiteralPath $publicationDirectoryValue)) {
        Get-ChildItem -LiteralPath $publicationDirectoryValue -File -Force -ErrorAction SilentlyContinue |
            ForEach-Object { $_.IsReadOnly = $false }
        Remove-Item -LiteralPath $publicationDirectoryValue -Recurse -Force
    }
    if (-not [string]::IsNullOrWhiteSpace($manifestRootValue) -and
        (Test-Path -LiteralPath $manifestRootValue -PathType Container) -and
        @(Get-ChildItem -LiteralPath $manifestRootValue -Force).Count -eq 0) {
        Remove-Item -LiteralPath $manifestRootValue -Force -ErrorAction SilentlyContinue
    }
}
