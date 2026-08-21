[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$buildScript = Join-Path $repositoryRoot 'build\Build-Release.ps1'
$innoRegistrationScript = Join-Path $repositoryRoot 'build\InnoSetupRegistration.ps1'
$publishScript = Join-Path $repositoryRoot 'deploy\Publish-CloudflareUpdate.ps1'
$signerAssembly = Join-Path $repositoryRoot 'tools\CloudflareR2Uploader.UpdateSigner\bin\Release\net10.0\CloudflareR2Uploader.UpdateSigner.dll'
$genuineInstallerAssembly = Join-Path $repositoryRoot 'tests\CloudflareR2Uploader.UpdateSigner.Tests\bin\Release\net10.0\CloudflareR2Uploader.UpdateSigner.Tests.dll'
$realDotnet = (Get-Command dotnet -CommandType Application -ErrorAction Stop).Source
. $innoRegistrationScript
if (-not (Test-Path -LiteralPath $genuineInstallerAssembly -PathType Leaf)) {
    throw 'The genuine PE fixture assembly must be built before the PowerShell behavioral tests.'
}
$genuineInstallerMetadata = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($genuineInstallerAssembly)
if ($genuineInstallerMetadata.ProductName -cne 'Cloudflare R2 Uploader' -or
    $genuineInstallerMetadata.FileDescription -cne 'Cloudflare R2 Uploader Setup' -or
    $genuineInstallerMetadata.FilePrivatePart -ne 0 -or
    $genuineInstallerMetadata.FileMajorPart -lt 0 -or $genuineInstallerMetadata.FileMajorPart -gt [uint16]::MaxValue -or
    $genuineInstallerMetadata.FileMinorPart -lt 0 -or $genuineInstallerMetadata.FileMinorPart -gt [uint16]::MaxValue -or
    $genuineInstallerMetadata.FileBuildPart -lt 0 -or $genuineInstallerMetadata.FileBuildPart -gt [uint16]::MaxValue) {
    throw 'The genuine PE fixture does not have a supported three-part Windows file version.'
}
$genuineInstallerVersion = ('{0}.{1}.{2}' -f
        $genuineInstallerMetadata.FileMajorPart,
        $genuineInstallerMetadata.FileMinorPart,
        $genuineInstallerMetadata.FileBuildPart)
$differentGenuineInstallerVersion = if ($genuineInstallerMetadata.FileBuildPart -lt [uint16]::MaxValue) {
    ('{0}.{1}.{2}' -f
        $genuineInstallerMetadata.FileMajorPart,
        $genuineInstallerMetadata.FileMinorPart,
        ($genuineInstallerMetadata.FileBuildPart + 1))
}
elseif ($genuineInstallerMetadata.FileMinorPart -lt [uint16]::MaxValue) {
    ('{0}.{1}.0' -f
        $genuineInstallerMetadata.FileMajorPart,
        ($genuineInstallerMetadata.FileMinorPart + 1))
}
elseif ($genuineInstallerMetadata.FileMajorPart -gt 0) { '0.0.0' }
else { '1.0.0' }

Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

public static class InstallerSnapshotSwapAttacker
{
    public static Task<string> Start(string repositoryRoot, string replacementPath)
    {
        return Task.Run(() =>
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(20);
            while (DateTime.UtcNow < deadline)
            {
                string artifacts = Path.Combine(repositoryRoot, "artifacts", "update");
                if (Directory.Exists(artifacts))
                {
                    try
                    {
                        foreach (string snapshot in Directory.EnumerateFiles(
                            artifacts, "installer-snapshot.exe", SearchOption.AllDirectories))
                        {
                            try
                            {
                                File.Copy(replacementPath, snapshot, true);
                                return "replaced";
                            }
                            catch (IOException)
                            {
                                return "blocked";
                            }
                            catch (UnauthorizedAccessException)
                            {
                                return "blocked";
                            }
                        }
                    }
                    catch (DirectoryNotFoundException)
                    {
                    }
                }
                Thread.Sleep(1);
            }
            return "timeout";
        });
    }
}
'@

function Assert-True([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw "Release-script test failed: $Message" }
}

function Invoke-ExpectFailure([scriptblock]$Action, [string]$ExpectedMessage) {
    $caught = $null
    try {
        & $Action
    }
    catch {
        $caught = $_
    }
    if ($null -eq $caught) { throw 'An expected release-script failure did not occur.' }
    if (-not $caught.Exception.Message.Contains($ExpectedMessage, [StringComparison]::Ordinal)) { throw $caught }
}

function New-TestDirectory([string]$Name) {
    $root = Join-Path ([System.IO.Path]::GetTempPath()) 'CloudflareR2Uploader Release Script Tests'
    [System.IO.Directory]::CreateDirectory($root) | Out-Null
    $path = Join-Path $root ($Name + '-' + [Guid]::NewGuid().ToString('N'))
    [System.IO.Directory]::CreateDirectory($path) | Out-Null
    return $path
}

function New-TestPassword {
    return 'ephemeral-' + [Guid]::NewGuid().ToString('N')
}

function New-EphemeralPfx([string]$Path, [string]$Password) {
    $rsa = [System.Security.Cryptography.RSA]::Create(2048)
    try {
        $request = [System.Security.Cryptography.X509Certificates.CertificateRequest]::new(
            'CN=CloudflareR2Uploader Release Script Test',
            $rsa,
            [System.Security.Cryptography.HashAlgorithmName]::SHA256,
            [System.Security.Cryptography.RSASignaturePadding]::Pkcs1)
        $certificate = $request.CreateSelfSigned(
            [DateTimeOffset]::UtcNow.AddMinutes(-1),
            [DateTimeOffset]::UtcNow.AddDays(1))
        try {
            [System.IO.File]::WriteAllBytes(
                $Path,
                $certificate.Export(
                    [System.Security.Cryptography.X509Certificates.X509ContentType]::Pkcs12,
                    $Password))
        }
        finally {
            $certificate.Dispose()
        }
    }
    finally {
        $rsa.Dispose()
    }
}

function Write-MinimalPe([string]$Path, [byte]$Marker) {
    $bytes = [byte[]]::new(512)
    $bytes[0] = 0x4d
    $bytes[1] = 0x5a
    [BitConverter]::GetBytes([uint32]0x80).CopyTo($bytes, 0x3c)
    $bytes[0x80] = 0x50
    $bytes[0x81] = 0x45
    $bytes[0x82] = 0
    $bytes[0x83] = 0
    $bytes[0x100] = $Marker
    [System.IO.File]::WriteAllBytes($Path, $bytes)
}

function Write-GenuineInstallerPe([string]$Path) {
    if (-not (Test-Path -LiteralPath $genuineInstallerAssembly -PathType Leaf)) {
        throw 'The genuine PE fixture assembly must be built before the PowerShell behavioral tests.'
    }
    Copy-Item -LiteralPath $genuineInstallerAssembly -Destination $Path
}

function Test-BuildReleaseValidation {
    Assert-True (Test-Path -LiteralPath $signerAssembly -PathType Leaf) 'Build the signer before running the PowerShell behavioral tests.'

    $temporary = New-TestDirectory 'build-validation'
    $originalPath = $env:PATH
    try {
        $bin = Join-Path $temporary 'fake commands'
        Write-FakeCommands $bin
        $env:PATH = $bin + [System.IO.Path]::PathSeparator + $originalPath
        $env:UPDATE_RELEASE_TEST_LOG = Join-Path $temporary 'commands.log'
        $env:UPDATE_RELEASE_TEST_MODE = 'build-validation'
        $env:UPDATE_RELEASE_TEST_SECRET_NAME = 'UPDATE_RELEASE_TEST_ABSENT_SECRET'
        $env:UPDATE_RELEASE_TEST_REAL_DOTNET = $realDotnet
        [Environment]::SetEnvironmentVariable($env:UPDATE_RELEASE_TEST_SECRET_NAME, $null)
        $missingCompiler = Join-Path $temporary 'missing-iscc.exe'
        $registeredCompiler = Resolve-InnoSetupCompilerRegistration -RequiredVersion '6.7.1'
        Invoke-ExpectFailure {
            & $buildScript -SkipTests -RequireSignedUpdate -UpdateBaseUrl '' -UpdateManifestPublicKey '' -InnoSetupCompiler $missingCompiler
        } 'A signed update URL and public key are required for this release build.'

        $validRsa = [System.Security.Cryptography.RSA]::Create(2048)
        $weakRsa = [System.Security.Cryptography.RSA]::Create(1024)
        $ecdsa = [System.Security.Cryptography.ECDsa]::Create(
            [System.Security.Cryptography.ECCurve]::NamedCurves.nistP256)
        try {
            $validBytes = $validRsa.ExportSubjectPublicKeyInfo()
            $keys = @(
                [Convert]::ToBase64String([byte[]](0x30, 0x00)),
                [Convert]::ToBase64String($ecdsa.ExportSubjectPublicKeyInfo()),
                [Convert]::ToBase64String($weakRsa.ExportSubjectPublicKeyInfo()),
                [Convert]::ToBase64String([byte[]]($validBytes + [byte]0))
            )

            foreach ($key in $keys) {
                Invoke-ExpectFailure {
                    & $buildScript -SkipTests -RequireSignedUpdate `
                        -UpdateBaseUrl 'https://updates.example.test' `
                        -UpdateManifestPublicKey $key `
                        -InnoSetupCompiler $registeredCompiler
                } 'UpdateManifestPublicKey must be a canonical RSA SubjectPublicKeyInfo of at least 2048 bits.'
            }

            Invoke-ExpectFailure {
                & $buildScript -SkipTests -RequireSignedUpdate `
                    -UpdateBaseUrl 'https://updates.example.test/channel?tenant=other' `
                    -UpdateManifestPublicKey ([Convert]::ToBase64String($validBytes)) `
                    -InnoSetupCompiler $missingCompiler
            } 'UpdateBaseUrl must be an absolute HTTPS URL without embedded credentials, a query, or a fragment.'
        }
        finally {
            $validRsa.Dispose()
            $weakRsa.Dispose()
            $ecdsa.Dispose()
        }
    }
    finally {
        $env:PATH = $originalPath
        Remove-Item Env:UPDATE_RELEASE_TEST_LOG,Env:UPDATE_RELEASE_TEST_MODE,Env:UPDATE_RELEASE_TEST_SECRET_NAME,Env:UPDATE_RELEASE_TEST_REAL_DOTNET -ErrorAction SilentlyContinue
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Recurse -Force }
    }
}

function Test-BuildReleaseInnoRegistrationBoundary {
    $registeredCompiler = Resolve-InnoSetupCompilerRegistration -RequiredVersion '6.7.1'
    Assert-True (Test-Path -LiteralPath $registeredCompiler -PathType Leaf) 'The Chocolatey-installed Inno Setup 6.7.1 registration did not resolve to ISCC.exe.'
    $explicitCompiler = Resolve-InnoSetupCompilerRegistration -RequestedCompiler $registeredCompiler -RequiredVersion '6.7.1'
    Assert-True ([string]::Equals(
            [System.IO.Path]::GetFullPath($registeredCompiler),
            [System.IO.Path]::GetFullPath($explicitCompiler),
            [StringComparison]::OrdinalIgnoreCase)) 'The registered Chocolatey compiler was rejected when supplied explicitly.'

    $temporary = New-TestDirectory 'unregistered-inno-compiler'
    $originalPath = $env:PATH
    try {
        $bin = Join-Path $temporary 'fake commands'
        Write-FakeCommands $bin
        $env:PATH = $bin + [System.IO.Path]::PathSeparator + $originalPath
        $env:UPDATE_RELEASE_TEST_LOG = Join-Path $temporary 'commands.log'
        $env:UPDATE_RELEASE_TEST_MODE = 'unregistered-inno-compiler'
        $env:UPDATE_RELEASE_TEST_SECRET_NAME = 'UPDATE_RELEASE_TEST_ABSENT_SECRET'
        $env:UPDATE_RELEASE_TEST_REAL_DOTNET = $realDotnet
        [Environment]::SetEnvironmentVariable($env:UPDATE_RELEASE_TEST_SECRET_NAME, $null)
        $unregisteredCompiler = Join-Path $temporary 'ISCC.exe'
        Copy-Item -LiteralPath $registeredCompiler -Destination $unregisteredCompiler
        $inno7Directory = Join-Path $temporary 'Inno Setup 7'
        New-Item -ItemType Directory -Path $inno7Directory | Out-Null
        $inno7Compiler = Join-Path $inno7Directory 'ISCC.exe'
        Copy-Item -LiteralPath $registeredCompiler -Destination $inno7Compiler
        $wrongCompiler = Join-Path $temporary 'wrong\ISCC.exe'
        New-Item -ItemType Directory -Path ([System.IO.Path]::GetDirectoryName($wrongCompiler)) | Out-Null
        Copy-Item -LiteralPath $genuineInstallerAssembly -Destination $wrongCompiler
        $missingCompiler = Join-Path $temporary 'missing\ISCC.exe'

        foreach ($compiler in @($unregisteredCompiler, $inno7Compiler, $wrongCompiler, $missingCompiler)) {
            Invoke-ExpectFailure {
                & $buildScript -SkipTests -InnoSetupCompiler $compiler
            } 'InnoSetupCompiler must resolve to the ISCC.exe registered by official Inno Setup 6.7.1.'
        }

        Assert-True (-not (Test-Path -LiteralPath $env:UPDATE_RELEASE_TEST_LOG)) 'An unregistered Inno compiler started a child process.'
    }
    finally {
        $env:PATH = $originalPath
        Remove-Item Env:UPDATE_RELEASE_TEST_LOG,Env:UPDATE_RELEASE_TEST_MODE,Env:UPDATE_RELEASE_TEST_SECRET_NAME,Env:UPDATE_RELEASE_TEST_REAL_DOTNET -ErrorAction SilentlyContinue
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Recurse -Force }
    }
}

function New-InnoSetupRegistrationCandidate(
    [string]$Hive,
    [string]$View,
    [string]$InstallLocation,
    [string]$Version = '6.7.1',
    [string]$SubKey = 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1',
    [string]$CompilerPath = '',
    [bool]$InstallPathTrusted = $true,
    [bool]$CompilerFileTrusted = $true) {
    if ([string]::IsNullOrWhiteSpace($CompilerPath)) {
        $CompilerPath = [System.IO.Path]::Combine($InstallLocation, 'ISCC.exe')
    }
    return [pscustomobject]@{
        RegistryHive = $Hive
        RegistryView = $View
        RegistrySubKey = $SubKey
        AppId = 'Inno Setup 6'
        DisplayVersion = $Version
        DisplayVersionKind = 'String'
        InstallLocation = $InstallLocation
        InstallLocationKind = 'String'
        CompilerPath = $CompilerPath
        CompilerExists = $true
        InstallPathTrusted = $InstallPathTrusted
        CompilerFileTrusted = $CompilerFileTrusted
    }
}

function Test-InnoSetupCandidateSelectionPolicy {
    $machinePath = 'C:\Program Files (x86)\Inno Setup 6'
    $userPath = 'C:\Users\release\AppData\Local\Programs\Inno Setup 6'
    $machine = New-InnoSetupRegistrationCandidate -Hive 'LocalMachine' -View 'Registry32' -InstallLocation $machinePath
    $user = New-InnoSetupRegistrationCandidate -Hive 'CurrentUser' -View 'Registry32' -InstallLocation $userPath

    # normal absolute path
    # HKLM Registry32
    Assert-True ((Select-InnoSetupCompilerCandidate -Candidates @($machine) -RequiredVersion '6.7.1') -ceq $machine.CompilerPath) 'HKLM Registry32 registration was rejected.'
    # HKCU Registry32
    Assert-True ((Select-InnoSetupCompilerCandidate -Candidates @($user) -RequiredVersion '6.7.1') -ceq $user.CompilerPath) 'HKCU Registry32 registration was rejected.'

    # Registry64 rejection
    $registry64 = New-InnoSetupRegistrationCandidate -Hive 'LocalMachine' -View 'Registry64' -InstallLocation $machinePath
    Invoke-ExpectFailure {
        Select-InnoSetupCompilerCandidate -Candidates @($registry64) -RequiredVersion '6.7.1'
    } 'Official Inno Setup 6.7.1 is not registered.'

    # wrong version
    $wrongVersion = New-InnoSetupRegistrationCandidate -Hive 'LocalMachine' -View 'Registry32' -InstallLocation $machinePath -Version '6.7.2'
    Invoke-ExpectFailure {
        Select-InnoSetupCompilerCandidate -Candidates @($wrongVersion) -RequiredVersion '6.7.1'
    } 'Official Inno Setup 6.7.1 is not registered.'

    # same-path dedupe
    $sameUserPath = New-InnoSetupRegistrationCandidate -Hive 'CurrentUser' -View 'Registry32' -InstallLocation $machinePath
    Assert-True ((Select-InnoSetupCompilerCandidate -Candidates @($machine, $sameUserPath) -RequiredVersion '6.7.1') -ceq $machine.CompilerPath) 'Same compiler path was not deduplicated.'

    # ambiguous registrations
    Invoke-ExpectFailure {
        Select-InnoSetupCompilerCandidate -Candidates @($machine, $user) -RequiredVersion '6.7.1'
    } 'Multiple distinct official Inno Setup 6.7.1 compilers are registered.'

    # explicit disambiguation
    Assert-True ((Select-InnoSetupCompilerCandidate -Candidates @($machine, $user) -RequestedCompiler $user.CompilerPath -RequiredVersion '6.7.1') -ceq $user.CompilerPath) 'An explicit valid candidate did not disambiguate multiple registrations.'

    # unregistered/wrong/no unrelated child key
    $unrelatedChild = New-InnoSetupRegistrationCandidate `
        -Hive 'LocalMachine' -View 'Registry32' -InstallLocation $machinePath `
        -SubKey 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1\Unrelated'
    $wrongChildCompiler = New-InnoSetupRegistrationCandidate `
        -Hive 'LocalMachine' -View 'Registry32' -InstallLocation $machinePath `
        -CompilerPath ([System.IO.Path]::Combine($machinePath, 'Unrelated', 'ISCC.exe'))
    $invalidCandidateSets = [System.Collections.Generic.List[object[]]]::new()
    $invalidCandidateSets.Add([object[]]@())
    $invalidCandidateSets.Add([object[]]@($unrelatedChild))
    $invalidCandidateSets.Add([object[]]@($wrongChildCompiler))

    # dot InstallLocation
    $dotLocation = New-InnoSetupRegistrationCandidate `
        -Hive 'LocalMachine' -View 'Registry32' -InstallLocation '.' -CompilerPath '.\ISCC.exe'
    $invalidCandidateSets.Add([object[]]@($dotLocation))

    # relative InstallLocation
    $relativeLocation = New-InnoSetupRegistrationCandidate `
        -Hive 'LocalMachine' -View 'Registry32' -InstallLocation 'relative\Inno Setup 6' `
        -CompilerPath 'relative\Inno Setup 6\ISCC.exe'
    $invalidCandidateSets.Add([object[]]@($relativeLocation))

    # UNC InstallLocation
    $uncLocation = New-InnoSetupRegistrationCandidate `
        -Hive 'LocalMachine' -View 'Registry32' -InstallLocation '\\server\share\Inno Setup 6'
    $invalidCandidateSets.Add([object[]]@($uncLocation))

    # extended-length device InstallLocation
    $extendedDeviceLocation = New-InnoSetupRegistrationCandidate `
        -Hive 'LocalMachine' -View 'Registry32' -InstallLocation '\\?\C:\Inno Setup 6'
    $invalidCandidateSets.Add([object[]]@($extendedDeviceLocation))

    # DOS device InstallLocation
    $dosDeviceLocation = New-InnoSetupRegistrationCandidate `
        -Hive 'LocalMachine' -View 'Registry32' -InstallLocation '\\.\C:\Inno Setup 6'
    $invalidCandidateSets.Add([object[]]@($dosDeviceLocation))

    # rooted current-drive InstallLocation
    $rootedCurrentDriveLocation = New-InnoSetupRegistrationCandidate `
        -Hive 'LocalMachine' -View 'Registry32' -InstallLocation '\Inno Setup 6'
    $invalidCandidateSets.Add([object[]]@($rootedCurrentDriveLocation))

    # install-directory reparse
    $installDirectoryReparse = New-InnoSetupRegistrationCandidate `
        -Hive 'LocalMachine' -View 'Registry32' -InstallLocation $machinePath `
        -InstallPathTrusted $false
    $invalidCandidateSets.Add([object[]]@($installDirectoryReparse))

    # compiler reparse
    $compilerReparse = New-InnoSetupRegistrationCandidate `
        -Hive 'LocalMachine' -View 'Registry32' -InstallLocation $machinePath `
        -CompilerFileTrusted $false
    $invalidCandidateSets.Add([object[]]@($compilerReparse))

    foreach ($invalidCandidates in $invalidCandidateSets) {
        Invoke-ExpectFailure {
            Select-InnoSetupCompilerCandidate -Candidates $invalidCandidates -RequiredVersion '6.7.1'
        } 'Official Inno Setup 6.7.1 is not registered.'
    }
}

function Test-InnoSetupPathChainPolicy {
    $temporary = New-TestDirectory 'inno-path-chain'
    try {
        $normalDirectory = Join-Path $temporary 'normal absolute path'
        [System.IO.Directory]::CreateDirectory($normalDirectory) | Out-Null
        $normalCompiler = Join-Path $normalDirectory 'ISCC.exe'
        [System.IO.File]::WriteAllBytes($normalCompiler, [byte[]](0x4d, 0x5a))
        Assert-True (Test-InnoSetupPathChain -Path $normalDirectory -ExpectedPathType Container) 'A normal absolute install directory was rejected.'
        Assert-True (Test-InnoSetupPathChain -Path $normalCompiler -ExpectedPathType Leaf) 'A normal absolute compiler was rejected.'

        # local DOS drive InstallLocation
        Assert-True (Test-InnoSetupLocalDrivePath -Path $normalDirectory) 'A local DOS drive InstallLocation was rejected.'
        # UNC InstallLocation
        Assert-True (-not (Test-InnoSetupLocalDrivePath -Path '\\server\share\Inno Setup 6')) 'A UNC InstallLocation was accepted.'
        # extended-length device InstallLocation
        Assert-True (-not (Test-InnoSetupLocalDrivePath -Path '\\?\C:\Inno Setup 6')) 'An extended-length device InstallLocation was accepted.'
        # DOS device InstallLocation
        Assert-True (-not (Test-InnoSetupLocalDrivePath -Path '\\.\C:\Inno Setup 6')) 'A DOS device InstallLocation was accepted.'
        # rooted current-drive InstallLocation
        Assert-True (-not (Test-InnoSetupLocalDrivePath -Path '\Inno Setup 6')) 'A rooted current-drive InstallLocation was accepted.'

        Assert-True (-not (Test-InnoSetupPathChain -Path '.' -ExpectedPathType Container)) 'A dot InstallLocation was accepted.'
        Assert-True (-not (Test-InnoSetupPathChain -Path 'relative\Inno Setup 6' -ExpectedPathType Container)) 'A relative InstallLocation was accepted.'

        $junctionTarget = Join-Path $temporary 'junction target'
        [System.IO.Directory]::CreateDirectory($junctionTarget) | Out-Null
        [System.IO.File]::WriteAllBytes((Join-Path $junctionTarget 'ISCC.exe'), [byte[]](0x4d, 0x5a))
        $junction = Join-Path $temporary 'install-directory reparse'
        New-Item -ItemType Junction -Path $junction -Target $junctionTarget | Out-Null
        Assert-True (-not (Test-InnoSetupPathChain -Path $junction -ExpectedPathType Container)) 'An install-directory reparse point was accepted.'
        Assert-True (-not (Test-InnoSetupPathChain -Path (Join-Path $junction 'ISCC.exe') -ExpectedPathType Leaf)) 'A compiler below an install-directory reparse point was accepted.'

        $compilerTarget = Join-Path $temporary 'compiler target.exe'
        [System.IO.File]::WriteAllBytes($compilerTarget, [byte[]](0x4d, 0x5a))
        $compilerReparseDirectory = Join-Path $temporary 'compiler reparse'
        [System.IO.Directory]::CreateDirectory($compilerReparseDirectory) | Out-Null
        $compilerLink = Join-Path $compilerReparseDirectory 'ISCC.exe'
        [System.IO.File]::CreateSymbolicLink($compilerLink, $compilerTarget) | Out-Null
        Assert-True (-not (Test-InnoSetupPathChain -Path $compilerLink -ExpectedPathType Leaf)) 'A compiler reparse point was accepted.'
    }
    finally {
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Recurse -Force }
    }
}

function Test-SemanticVersionValidation {
    $temporary = New-TestDirectory 'semantic-version-validation'
    $originalPath = $env:PATH
    $log = Join-Path $temporary 'commands.log'
    $missingCompiler = Join-Path $temporary 'missing-iscc.exe'
    $secretName = 'UPDATE_RELEASE_TEST_PASSWORD_' + [Guid]::NewGuid().ToString('N')
    $secretValue = New-TestPassword
    try {
        $bin = Join-Path $temporary 'fake commands'
        Write-FakeCommands $bin
        $env:PATH = $bin + [System.IO.Path]::PathSeparator + $originalPath
        $env:UPDATE_RELEASE_TEST_LOG = $log
        $env:UPDATE_RELEASE_TEST_MODE = 'semantic-version-validation'
        $env:UPDATE_RELEASE_TEST_SECRET_NAME = $secretName
        $env:UPDATE_RELEASE_TEST_REAL_DOTNET = $realDotnet
        $maximumLengthVersion = $genuineInstallerVersion + '-' +
            ('a' * (128 - $genuineInstallerVersion.Length - 1))
        $overMaximumLengthVersion = $maximumLengthVersion + 'a'
        Assert-True ($maximumLengthVersion.Length -eq 128) 'The maximum-length SemVer fixture is not 128 characters.'
        Assert-True ($overMaximumLengthVersion.Length -eq 129) 'The over-maximum-length SemVer fixture is not 129 characters.'
        $invalidVersions = @(
            @{ Name = 'numeric-prerelease-leading-zero'; Value = ($genuineInstallerVersion + '-01'); Message = 'is not supported' },
            @{ Name = 'empty-prerelease'; Value = ($genuineInstallerVersion + '-'); Message = 'is not supported' },
            @{ Name = 'invalid-build'; Value = ($genuineInstallerVersion + '+build..1'); Message = 'is not supported' },
            @{ Name = 'build-metadata-not-publishable'; Value = ($genuineInstallerVersion + '+build.1'); Message = 'is not supported' },
            @{ Name = 'invalid-prerelease-character'; Value = ($genuineInstallerVersion + '-alpha_1'); Message = 'is not supported' },
            @{ Name = 'file-version-overflow'; Value = '65536.0.0'; Message = 'cannot be represented by Windows file-version metadata' },
            @{ Name = 'numeric-core-leading-zero'; Value = '01.0.0'; Message = 'is not supported' },
            @{ Name = 'trailing-newline'; Value = ($genuineInstallerVersion + "`n"); Message = 'is not supported' },
            @{ Name = 'v-prefix'; Value = ('v' + $genuineInstallerVersion); Message = 'is not supported' },
            @{ Name = 'over-maximum-length'; Value = $overMaximumLengthVersion; Message = 'is not supported' }
        )

        foreach ($case in $invalidVersions) {
            if (Test-Path -LiteralPath $log) { Remove-Item -LiteralPath $log -Force }
            Invoke-ExpectFailure {
                & $buildScript -SkipTests -Version $case.Value -InnoSetupCompiler $missingCompiler
            } $case.Message
            Assert-True (-not (Test-Path -LiteralPath $log)) "The $($case.Name) build version started child work."

            [Environment]::SetEnvironmentVariable($secretName, $secretValue)
            try {
                $publisherExpectedMessage = if ($case.Name -eq 'file-version-overflow') {
                    'Version cannot be represented by Windows file-version metadata.'
                }
                else {
                    'Version must be a supported SemVer value.'
                }
                Invoke-ExpectFailure {
                    & $publishScript -Version $case.Value -BucketName 'test-bucket' `
                        -PublicBaseUrl 'https://updates.example.test' `
                        -SigningCertificatePath (Join-Path $temporary 'missing.pfx') `
                        -SigningCertificatePasswordEnvironmentVariable $secretName `
                        -Prefix 'updates' -SkipBuild -InstallerPath (Join-Path $temporary 'missing.exe')
                } $publisherExpectedMessage
                Assert-True ([string]::IsNullOrEmpty([Environment]::GetEnvironmentVariable($secretName))) "The $($case.Name) publisher rejection did not consume the password."
                Assert-True (-not (Test-Path -LiteralPath $log)) "The $($case.Name) publisher version started child work."
            }
            finally {
                [Environment]::SetEnvironmentVariable($secretName, $null)
            }
        }

        # Reaching the explicit registration boundary proves the grammar and file-version
        # mapping accepted the version while still avoiding build or network children.
        Invoke-ExpectFailure {
            & $buildScript -SkipTests -Version ($genuineInstallerVersion + '-rc.1') `
                -InnoSetupCompiler $missingCompiler
        } 'InnoSetupCompiler must resolve to the ISCC.exe registered by official Inno Setup 6.7.1.'
        Assert-True (-not (Test-Path -LiteralPath $log)) 'The valid-prerelease compiler gate unexpectedly started child work.'

        [Environment]::SetEnvironmentVariable($secretName, $secretValue)
        Invoke-ExpectFailure {
            & $publishScript -Version ($genuineInstallerVersion + '-rc.1') -BucketName 'test-bucket' `
                -PublicBaseUrl 'https://updates.example.test' `
                -SigningCertificatePath (Join-Path $temporary 'missing.pfx') `
                -SigningCertificatePasswordEnvironmentVariable $secretName `
                -Prefix 'updates' -SkipBuild -InstallerPath (Join-Path $temporary 'missing.exe')
        } 'The signing certificate file is missing.'
        Assert-True ([string]::IsNullOrEmpty([Environment]::GetEnvironmentVariable($secretName))) 'The valid-prerelease publisher gate did not consume the password.'
        Assert-True (-not (Test-Path -LiteralPath $log)) 'The valid-prerelease publisher gate unexpectedly started child work.'

        Invoke-ExpectFailure {
            & $buildScript -SkipTests -Version $maximumLengthVersion -InnoSetupCompiler $missingCompiler
        } 'InnoSetupCompiler must resolve to the ISCC.exe registered by official Inno Setup 6.7.1.'
        Assert-True (-not (Test-Path -LiteralPath $log)) 'The maximum-length build version unexpectedly started child work.'

        [Environment]::SetEnvironmentVariable($secretName, $secretValue)
        Invoke-ExpectFailure {
            & $publishScript -Version $maximumLengthVersion -BucketName 'test-bucket' `
                -PublicBaseUrl 'https://updates.example.test' `
                -SigningCertificatePath (Join-Path $temporary 'missing.pfx') `
                -SigningCertificatePasswordEnvironmentVariable $secretName `
                -Prefix 'updates' -SkipBuild -InstallerPath (Join-Path $temporary 'missing.exe')
        } 'The signing certificate file is missing.'
        Assert-True ([string]::IsNullOrEmpty([Environment]::GetEnvironmentVariable($secretName))) 'The maximum-length publisher gate did not consume the password.'
        Assert-True (-not (Test-Path -LiteralPath $log)) 'The maximum-length publisher version unexpectedly started child work.'
    }
    finally {
        [Environment]::SetEnvironmentVariable($secretName, $null)
        $env:PATH = $originalPath
        Remove-Item Env:UPDATE_RELEASE_TEST_LOG,Env:UPDATE_RELEASE_TEST_MODE,Env:UPDATE_RELEASE_TEST_SECRET_NAME,Env:UPDATE_RELEASE_TEST_REAL_DOTNET -ErrorAction SilentlyContinue
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Recurse -Force }
    }
}

function Write-FakeCommands([string]$BinDirectory) {
    [System.IO.Directory]::CreateDirectory($BinDirectory) | Out-Null
    foreach ($command in @('dotnet', 'npx', 'curl')) {
        [System.IO.File]::WriteAllText(
            (Join-Path $BinDirectory ($command + '.cmd')),
            ('@pwsh -NoLogo -NoProfile -File "%~dp0' + $command + '-fake.ps1" %*' + "`r`n"),
            [System.Text.Encoding]::ASCII)
    }

    $dotnetFake = @'
$ErrorActionPreference = 'Stop'
$secret = [Environment]::GetEnvironmentVariable($env:UPDATE_RELEASE_TEST_SECRET_NAME)
$state = if ([string]::IsNullOrEmpty($secret)) { 'absent' } else { 'present' }
$kind = if ($args[0] -eq 'build') { 'dotnet-build' } else { 'signer-' + $args[1] }
Add-Content -LiteralPath $env:UPDATE_RELEASE_TEST_LOG -Value ($kind + '|' + $state)
if ($kind -eq 'dotnet-build') { exit 0 }

if ($args[1] -eq 'sign' -and $env:UPDATE_RELEASE_TEST_MODE -eq 'source-mutation') {
    $bytes = [System.IO.File]::ReadAllBytes($env:UPDATE_RELEASE_TEST_SOURCE_INSTALLER)
    $bytes[0x100] = $bytes[0x100] -bxor 0xff
    [System.IO.File]::WriteAllBytes($env:UPDATE_RELEASE_TEST_SOURCE_INSTALLER, $bytes)
}
if ($args[1] -eq 'sign' -and $env:UPDATE_RELEASE_TEST_MODE -eq 'validation-boundary-swap') {
    $installerIndex = [Array]::IndexOf($args, '--installer')
    $snapshot = $args[$installerIndex + 1]
    try {
        Copy-Item -LiteralPath $env:UPDATE_RELEASE_TEST_PFX -Destination $snapshot -Force
        Add-Content -LiteralPath $env:UPDATE_RELEASE_TEST_LOG -Value 'snapshot-swap-replaced|absent'
        exit 91
    }
    catch {
        Add-Content -LiteralPath $env:UPDATE_RELEASE_TEST_LOG -Value 'snapshot-swap-blocked|absent'
    }
}

& $env:UPDATE_RELEASE_TEST_REAL_DOTNET @args
exit $LASTEXITCODE
'@
    [System.IO.File]::WriteAllText(
        (Join-Path $BinDirectory 'dotnet-fake.ps1'), $dotnetFake, [System.Text.UTF8Encoding]::new($false))

    $npxFake = @'
$ErrorActionPreference = 'Stop'
$secret = [Environment]::GetEnvironmentVariable($env:UPDATE_RELEASE_TEST_SECRET_NAME)
$state = if ([string]::IsNullOrEmpty($secret)) { 'absent' } else { 'present' }
if ($args.Count -lt 6 -or $args[0] -ne '--yes' -or $args[1] -ne 'wrangler@4.120.0' -or $args[2] -ne 'r2' -or $args[3] -ne 'object') { exit 2 }
$verb = $args[4]
$objectPath = $args[5]
$isInstaller = $objectPath.Contains('/releases/', [StringComparison]::Ordinal)
$kind = 'r2-' + $verb + '-' + $(if ($isInstaller) { 'installer' } else { 'manifest' })
Add-Content -LiteralPath $env:UPDATE_RELEASE_TEST_LOG -Value ($kind + '|' + $state)
$remotePath = Join-Path $env:UPDATE_RELEASE_TEST_REMOTE ($objectPath.Replace('/', [System.IO.Path]::DirectorySeparatorChar))
$fileIndex = [Array]::IndexOf($args, '--file')

if ($verb -eq 'get') {
    if ($args.Count -ne 8 -or $args[6] -ne '--remote' -or $args[7] -ne '--pipe') { exit 2 }
    if ($env:UPDATE_RELEASE_TEST_MODE -eq 'permission-failure' -and $isInstaller) {
        [Console]::Error.WriteLine('Access denied for this R2 bucket.')
        exit 1
    }
    if ($env:UPDATE_RELEASE_TEST_MODE -eq 'network-failure' -and $isInstaller) {
        [Console]::Error.WriteLine('Network request failed while contacting Cloudflare.')
        exit 1
    }
    if (-not (Test-Path -LiteralPath $remotePath -PathType Leaf)) {
        [Console]::Error.WriteLine('The specified key does not exist.')
        exit 1
    }

    $bytes = [System.IO.File]::ReadAllBytes($remotePath)
    $getCount = @(Get-Content -LiteralPath $env:UPDATE_RELEASE_TEST_LOG | Where-Object {
        $_.StartsWith($kind + '|', [StringComparison]::Ordinal)
    }).Count
    $emitOversize =
        ($env:UPDATE_RELEASE_TEST_MODE -eq 'authenticated-preflight-oversize' -and $isInstaller -and $getCount -eq 1) -or
        ($env:UPDATE_RELEASE_TEST_MODE -eq 'authenticated-installer-readback-oversize' -and $isInstaller -and $getCount -eq 2) -or
        ($env:UPDATE_RELEASE_TEST_MODE -eq 'authenticated-manifest-readback-oversize' -and -not $isInstaller) -or
        ($env:UPDATE_RELEASE_TEST_MODE -eq 'authenticated-dishonest-length' -and $isInstaller -and $getCount -eq 1) -or
        ($env:UPDATE_RELEASE_TEST_MODE -eq 'authenticated-during-materialization-overflow' -and $isInstaller -and $getCount -eq 1)
    if ($env:UPDATE_RELEASE_TEST_MODE -eq 'authenticated-dishonest-length' -and $isInstaller -and $getCount -eq 1) {
        [Console]::Error.WriteLine('Content-Length: ' + $bytes.Length)
    }
    $standardOutput = [Console]::OpenStandardOutput()
    if ($env:UPDATE_RELEASE_TEST_MODE -eq 'authenticated-hanging-child-timeout' -and $isInstaller -and $getCount -eq 1) {
        $childStartInfo = [System.Diagnostics.ProcessStartInfo]::new()
        $childStartInfo.FileName = [Environment]::ProcessPath
        $childStartInfo.UseShellExecute = $false
        $childStartInfo.ArgumentList.Add('-NoLogo')
        $childStartInfo.ArgumentList.Add('-NoProfile')
        $childStartInfo.ArgumentList.Add('-Command')
        $childStartInfo.ArgumentList.Add('Start-Sleep -Seconds 120')
        $child = [System.Diagnostics.Process]::Start($childStartInfo)
        [System.IO.File]::WriteAllText(
            $env:UPDATE_RELEASE_TEST_HANG_CHILD_PID,
            $child.Id.ToString([Globalization.CultureInfo]::InvariantCulture))
        $standardOutput.Write($bytes, 0, 1)
        $standardOutput.Flush()
        Start-Sleep -Seconds 120
        exit 97
    }
    if ($env:UPDATE_RELEASE_TEST_MODE -eq 'authenticated-during-materialization-overflow' -and $isInstaller -and $getCount -eq 1) {
        $firstLength = [int][Math]::Max(1, [Math]::Floor($bytes.Length / 2))
        $standardOutput.Write($bytes, 0, $firstLength)
        $standardOutput.Flush()
        Start-Sleep -Milliseconds 50
        $standardOutput.Write($bytes, $firstLength, $bytes.Length - $firstLength)
    }
    else {
        $standardOutput.Write($bytes, 0, $bytes.Length)
    }
    if ($emitOversize) {
        $extra = [byte[]](0x7f)
        $standardOutput.Write($extra, 0, $extra.Length)
    }
    if ($env:UPDATE_RELEASE_TEST_MODE -eq 'authenticated-partial-failure' -and $isInstaller -and $getCount -eq 2) {
        [Console]::Error.WriteLine('Synthetic authenticated partial read failure.')
        exit 1
    }
    exit 0
}

if ($verb -eq 'put') {
    if ($fileIndex -lt 0) { exit 2 }
    $file = $args[$fileIndex + 1]
    [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($remotePath)) | Out-Null
    Copy-Item -LiteralPath $file -Destination $remotePath -Force
    if ($env:UPDATE_RELEASE_TEST_MODE -eq 'remote-mismatch' -and $isInstaller) {
        [System.IO.File]::WriteAllBytes($remotePath, [System.Text.Encoding]::UTF8.GetBytes('mutated after upload'))
    }
    exit 0
}
exit 2
'@
    [System.IO.File]::WriteAllText(
        (Join-Path $BinDirectory 'npx-fake.ps1'), $npxFake, [System.Text.UTF8Encoding]::new($false))

    $curlFake = @'
$ErrorActionPreference = 'Stop'
$secret = [Environment]::GetEnvironmentVariable($env:UPDATE_RELEASE_TEST_SECRET_NAME)
$state = if ([string]::IsNullOrEmpty($secret)) { 'absent' } else { 'present' }
$outputIndex = [Array]::IndexOf($args, '--output')
if ($outputIndex -lt 0) { exit 2 }
$destination = $args[$outputIndex + 1]
$url = $args[$args.Count - 1]
$uri = [Uri]$url
$isManifest = $uri.AbsolutePath.EndsWith('/manifest.json', [StringComparison]::Ordinal)
$expectedMaximum = if ($isManifest) { '65536' } else { '268435456' }
$expected = @(
    '--fail', '--silent', '--show-error', '--location', '--max-redirs', '5',
    '--connect-timeout', '15', '--max-time', '300',
    '--proto', '=https', '--proto-redir', '=https',
    '--max-filesize', $expectedMaximum,
    '--output', $destination, '--write-out', '%{url_effective}', '--', $url)
if ($args.Count -ne $expected.Count) {
    [Console]::Error.WriteLine('Unexpected curl argument contract.')
    exit 2
}
for ($index = 0; $index -lt $expected.Count; $index++) {
    if (-not [string]::Equals($args[$index], $expected[$index], [StringComparison]::Ordinal)) {
        [Console]::Error.WriteLine('Unexpected curl argument contract.')
        exit 2
    }
}
$kind = if ($isManifest) { 'public-get-manifest' } else { 'public-get-installer' }
Add-Content -LiteralPath $env:UPDATE_RELEASE_TEST_LOG -Value ($kind + '|' + $state + '|' + $url)

if ($env:UPDATE_RELEASE_TEST_MODE -eq 'public-partial-failure' -and -not $isManifest) {
    [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($destination)) | Out-Null
    [System.IO.File]::WriteAllText($destination, 'partial installer')
    [Console]::Out.Write($url)
    [Console]::Error.WriteLine('Synthetic partial public endpoint failure.')
    exit 22
}
if ($env:UPDATE_RELEASE_TEST_MODE -eq 'public-mixed-failure' -and $isManifest) {
    [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($destination)) | Out-Null
    [System.IO.File]::WriteAllText($destination, 'partial manifest')
    [Console]::Out.Write($url)
    [Console]::Error.WriteLine('Synthetic mixed-stream public endpoint failure.')
    exit 22
}
if ($env:UPDATE_RELEASE_TEST_MODE -eq 'public-failure' -and $isManifest) {
    [Console]::Error.WriteLine('Synthetic public endpoint failure.')
    exit 22
}

$relative = $uri.AbsolutePath.TrimStart('/').Replace('/', [System.IO.Path]::DirectorySeparatorChar)
$remotePath = Join-Path $env:UPDATE_RELEASE_TEST_REMOTE (Join-Path $env:UPDATE_RELEASE_TEST_BUCKET $relative)
[System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($destination)) | Out-Null
if ($env:UPDATE_RELEASE_TEST_MODE -eq 'public-wrong' -and -not $isManifest) {
    [System.IO.File]::WriteAllBytes($destination, [System.Text.Encoding]::UTF8.GetBytes('wrong public installer'))
}
elseif ($env:UPDATE_RELEASE_TEST_MODE -eq 'public-installer-oversize' -and -not $isManifest) {
    $oversized = [System.IO.File]::Open($destination, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write)
    try { $oversized.SetLength(268435457L) }
    finally { $oversized.Dispose() }
}
elseif ($env:UPDATE_RELEASE_TEST_MODE -eq 'public-oversize' -and $isManifest) {
    [System.IO.File]::WriteAllBytes($destination, [byte[]]::new(65537))
}
elseif ($env:UPDATE_RELEASE_TEST_MODE -eq 'public-stale' -and $isManifest) {
    Copy-Item -LiteralPath $env:UPDATE_RELEASE_TEST_STALE_MANIFEST -Destination $destination -Force
}
else {
    if (-not (Test-Path -LiteralPath $remotePath -PathType Leaf)) { exit 22 }
    Copy-Item -LiteralPath $remotePath -Destination $destination -Force
}

if ($env:UPDATE_RELEASE_TEST_MODE -eq 'public-downgrade' -and $isManifest) {
    [Console]::Out.Write('http://updates.example.test/updates/manifest.json')
}
elseif ($env:UPDATE_RELEASE_TEST_MODE -eq 'public-unsafe-credentials' -and $isManifest) {
    [Console]::Out.Write('https://user:password@updates.example.test/updates/manifest.json')
}
elseif ($env:UPDATE_RELEASE_TEST_MODE -eq 'public-unsafe-fragment' -and $isManifest) {
    [Console]::Out.Write('https://updates.example.test/updates/manifest.json#untrusted')
}
else {
    [Console]::Out.Write($url)
}
exit 0
'@
    [System.IO.File]::WriteAllText(
        (Join-Path $BinDirectory 'curl-fake.ps1'), $curlFake, [System.Text.UTF8Encoding]::new($false))
}

function Invoke-PublisherCase(
    [string]$Mode,
    [ValidateSet('absent', 'same', 'different')]
    [string]$Existing,
    [bool]$ExpectSuccess) {
    $temporary = New-TestDirectory ('publisher-' + $Mode + '-' + $Existing)
    $originalPath = $env:PATH
    $secretName = 'UPDATE_RELEASE_TEST_PASSWORD_' + [Guid]::NewGuid().ToString('N')
    $secretValue = New-TestPassword
    $hangingChildId = $null
    try {
        $paths = Join-Path $temporary 'paths with spaces'
        [System.IO.Directory]::CreateDirectory($paths) | Out-Null
        $bin = Join-Path $temporary 'fake commands'
        $remote = Join-Path $temporary 'remote objects'
        $log = Join-Path $temporary 'commands.log'
        $installer = Join-Path $paths 'Setup.exe'
        $certificate = Join-Path $paths 'signer.pfx'
        Write-GenuineInstallerPe $installer
        New-EphemeralPfx $certificate $secretValue
        $originalInstallerBytes = [System.IO.File]::ReadAllBytes($installer)
        $originalPfxHash = (Get-FileHash -LiteralPath $certificate -Algorithm SHA256).Hash

        Write-FakeCommands $bin
        $env:PATH = $bin + [System.IO.Path]::PathSeparator + $originalPath
        $env:UPDATE_RELEASE_TEST_LOG = $log
        $env:UPDATE_RELEASE_TEST_REMOTE = $remote
        $env:UPDATE_RELEASE_TEST_BUCKET = 'test-bucket'
        $env:UPDATE_RELEASE_TEST_MODE = $Mode
        $env:UPDATE_RELEASE_TEST_SECRET_NAME = $secretName
        $env:UPDATE_RELEASE_TEST_REAL_DOTNET = $realDotnet
        $env:UPDATE_RELEASE_TEST_SOURCE_INSTALLER = $installer
        $env:UPDATE_RELEASE_TEST_PFX = $certificate
        $env:UPDATE_RELEASE_TEST_HANG_CHILD_PID = Join-Path $paths 'hanging-child.pid'
        if ($Mode -eq 'public-stale') {
            $staleManifest = Join-Path $paths 'stale-manifest.json'
            [Environment]::SetEnvironmentVariable($secretName, $secretValue)
            & $realDotnet $signerAssembly sign --pfx $certificate --password-env $secretName `
                --version ($genuineInstallerVersion + '-stale') --installer $installer `
                --installer-url ("https://updates.example.test/updates/releases/$genuineInstallerVersion-stale/old-Setup.exe") `
                --notes 'stale fixture' --output $staleManifest *> $null
            if ($LASTEXITCODE -ne 0) { throw 'The real signer could not create the stale-manifest fixture.' }
            [Environment]::SetEnvironmentVariable($secretName, $null)
            $env:UPDATE_RELEASE_TEST_STALE_MANIFEST = $staleManifest
        }
        [Environment]::SetEnvironmentVariable($secretName, $secretValue)

        $hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
        $installerName = [System.IO.Path]::GetFileName($installer)
        $installerKey = "updates/releases/$genuineInstallerVersion/$hash-$installerName"
        $remoteInstaller = Join-Path $remote ("test-bucket/$installerKey".Replace('/', [System.IO.Path]::DirectorySeparatorChar))
        if ($Existing -ne 'absent') {
            [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($remoteInstaller)) | Out-Null
            if ($Existing -eq 'same') { [System.IO.File]::WriteAllBytes($remoteInstaller, $originalInstallerBytes) }
            else { [System.IO.File]::WriteAllBytes($remoteInstaller, [System.Text.Encoding]::UTF8.GetBytes('different bytes')) }
        }

        $creationBoundaryAttack = $null
        if ($Mode -eq 'creation-boundary-swap') {
            $creationBoundaryAttack = [InstallerSnapshotSwapAttacker]::Start($repositoryRoot, $certificate)
        }

        $succeeded = $true
        $failure = $null
        $publisherOutput = ''
        try {
            $publisherParameters = @{
                Version = $genuineInstallerVersion
                BucketName = 'test-bucket'
                PublicBaseUrl = 'https://updates.example.test'
                SigningCertificatePath = $certificate
                SigningCertificatePasswordEnvironmentVariable = $secretName
                Prefix = 'updates'
                SkipBuild = $true
                InstallerPath = $installer
            }
            if ($Mode -eq 'authenticated-hanging-child-timeout') {
                $publisherParameters.AuthenticatedReadTimeoutSeconds = 1
            }
            $publisherOutput = (& $publishScript @publisherParameters 2>&1 | Out-String)
        }
        catch {
            $succeeded = $false
            $failure = $_.Exception.Message
            $publisherOutput += $failure
        }

        Assert-True ($succeeded -eq $ExpectSuccess) "Publisher result for $Mode/$Existing was '$failure'."
        if ($Mode -eq 'authenticated-hanging-child-timeout') {
            Assert-True ($failure.Contains('The authenticated R2 object download timed out.', [StringComparison]::Ordinal)) 'The hanging Wrangler path did not return the controlled timeout error.'
            Assert-True (Test-Path -LiteralPath $env:UPDATE_RELEASE_TEST_HANG_CHILD_PID -PathType Leaf) 'The hanging Wrangler fixture did not start its descendant process.'
            $hangingChildId = [int][System.IO.File]::ReadAllText($env:UPDATE_RELEASE_TEST_HANG_CHILD_PID)
            $childExitDeadline = [DateTime]::UtcNow.AddSeconds(10)
            while ($null -ne (Get-Process -Id $hangingChildId -ErrorAction SilentlyContinue) -and
                [DateTime]::UtcNow -lt $childExitDeadline) {
                Start-Sleep -Milliseconds 50
            }
            Assert-True ($null -eq (Get-Process -Id $hangingChildId -ErrorAction SilentlyContinue)) 'The timed-out Wrangler descendant process survived tree termination.'
        }
        if ($null -ne $creationBoundaryAttack) {
            $attackResult = $creationBoundaryAttack.GetAwaiter().GetResult()
            Assert-True ($attackResult -eq 'blocked') "The creation-boundary PFX swap was not blocked: $attackResult."
        }
        Assert-True ([string]::IsNullOrEmpty([Environment]::GetEnvironmentVariable($secretName))) 'The password environment variable was restored or leaked.'
        Assert-True ((Get-FileHash -LiteralPath $certificate -Algorithm SHA256).Hash -ceq $originalPfxHash) 'The PFX bytes changed during publication.'
        $lines = if (Test-Path -LiteralPath $log) { @(Get-Content -LiteralPath $log) } else { @() }
        Assert-True (-not (($lines -join "`n").Contains($secretName, [StringComparison]::Ordinal))) 'A password variable name reached a child log.'
        Assert-True (-not (($lines -join "`n").Contains($secretValue, [StringComparison]::Ordinal))) 'A password value reached a child log.'
        Assert-True (-not $publisherOutput.Contains($secretName, [StringComparison]::Ordinal)) 'A password variable name reached publisher output.'
        Assert-True (-not $publisherOutput.Contains($secretValue, [StringComparison]::Ordinal)) 'A password value reached publisher output.'
        foreach ($line in $lines) {
            $parts = $line.Split('|')
            $requiresCertificate = $parts[0] -eq 'signer-export-public-key' -or $parts[0] -eq 'signer-sign'
            Assert-True ($parts[1] -eq $(if ($requiresCertificate) { 'present' } else { 'absent' })) "Unexpected child environment: $line"
        }
        if ($Mode -eq 'validation-boundary-swap') {
            Assert-True (@($lines | Where-Object { $_.StartsWith('snapshot-swap-blocked|', [StringComparison]::Ordinal) }).Count -eq 1) 'The validation-boundary PFX swap was not blocked.'
            Assert-True (@($lines | Where-Object { $_.StartsWith('snapshot-swap-replaced|', [StringComparison]::Ordinal) }).Count -eq 0) 'PFX bytes replaced the held snapshot.'
        }

        $manifestPutIndexes = @()
        $installerPublicIndex = -1
        $lastInstallerVerification = -1
        for ($index = 0; $index -lt $lines.Count; $index++) {
            if ($lines[$index].StartsWith('r2-put-manifest|', [StringComparison]::Ordinal)) { $manifestPutIndexes += $index }
            if ($lines[$index].StartsWith('public-get-installer|', [StringComparison]::Ordinal)) { $installerPublicIndex = $index }
            if ($lines[$index].StartsWith('signer-verify-installer|', [StringComparison]::Ordinal)) { $lastInstallerVerification = $index }
        }
        Assert-True ($manifestPutIndexes.Count -le 1) 'The manifest was uploaded more than once.'
        if ($manifestPutIndexes.Count -eq 1) {
            Assert-True ($installerPublicIndex -ge 0 -and $lastInstallerVerification -gt $installerPublicIndex -and $manifestPutIndexes[0] -gt $lastInstallerVerification) 'The manifest write was not last after authenticated and public installer verification.'
        }
        $failsBeforeManifest = $Existing -eq 'different' -or $Mode -in @(
            'remote-mismatch', 'permission-failure', 'network-failure', 'public-wrong',
            'public-partial-failure', 'public-installer-oversize',
            'authenticated-preflight-oversize', 'authenticated-installer-readback-oversize',
            'authenticated-partial-failure', 'authenticated-dishonest-length',
            'authenticated-during-materialization-overflow', 'authenticated-hanging-child-timeout')
        if ($failsBeforeManifest) {
            Assert-True ($manifestPutIndexes.Count -eq 0) 'A failure before final publication wrote the manifest.'
        }
        if (-not $ExpectSuccess -and $Mode -in @(
            'public-stale', 'public-downgrade', 'public-oversize', 'public-failure',
            'public-mixed-failure', 'public-unsafe-credentials', 'public-unsafe-fragment',
            'authenticated-manifest-readback-oversize')) {
            Assert-True ($manifestPutIndexes.Count -eq 1) 'The public-manifest failure did not execute after the manifest-last write.'
        }
        if ($ExpectSuccess) {
            Assert-True ($manifestPutIndexes.Count -eq 1) 'A successful publication did not upload one manifest.'
            $publicManifest = @($lines | Where-Object { $_.StartsWith('public-get-manifest|', [StringComparison]::Ordinal) })
            Assert-True ($publicManifest.Count -eq 1 -and $publicManifest[0].Contains('cacheBust=', [StringComparison]::Ordinal)) 'The public manifest was not fetched once with cache busting.'
            Assert-True ($lines[-1].StartsWith('signer-verify|', [StringComparison]::Ordinal)) 'Public manifest verification was not the final child operation.'
        }

        if ($Existing -eq 'same') {
            Assert-True (@($lines | Where-Object { $_.StartsWith('r2-put-installer|', [StringComparison]::Ordinal) }).Count -eq 0) 'An identical existing installer was overwritten.'
        }
        if ($Existing -eq 'different') {
            Assert-True (([System.Text.Encoding]::UTF8.GetString([System.IO.File]::ReadAllBytes($remoteInstaller))) -eq 'different bytes') 'A different existing installer was overwritten.'
        }
        if ($Mode -eq 'normal' -and $Existing -eq 'absent') {
            Assert-True (@($lines | Where-Object { $_.StartsWith('r2-put-installer|', [StringComparison]::Ordinal) }).Count -eq 1) 'A genuinely missing immutable key was not uploaded once.'
        }
        if ($Mode -eq 'source-mutation') {
            Assert-True ((Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant() -cne $hash) 'The mutation boundary did not run.'
            Assert-True ((Get-FileHash -LiteralPath $remoteInstaller -Algorithm SHA256).Hash.ToLowerInvariant() -ceq $hash) 'Original-file mutation changed the signed and published snapshot.'
        }

        $scratch = Join-Path $repositoryRoot 'artifacts\update'
        Assert-True ((-not (Test-Path -LiteralPath $scratch)) -or @(Get-ChildItem -LiteralPath $scratch -Force).Count -eq 0) 'Publisher scratch files were not cleaned after completion/failure.'
        if (-not $ExpectSuccess -and $Mode.StartsWith('public-', [StringComparison]::Ordinal)) {
            Assert-True ((-not (Test-Path -LiteralPath $scratch)) -or
                @(Get-ChildItem -LiteralPath $scratch -Recurse -File -Force).Count -eq 0) 'A failed curl left a partial verification file.'
        }
    }
    finally {
        [Environment]::SetEnvironmentVariable($secretName, $null)
        $env:PATH = $originalPath
        if ($null -ne $hangingChildId) {
            $hangingChild = Get-Process -Id $hangingChildId -ErrorAction SilentlyContinue
            if ($null -ne $hangingChild) {
                try {
                    if (-not $hangingChild.HasExited) { $hangingChild.Kill($true) }
                    $null = $hangingChild.WaitForExit(5000)
                }
                catch [InvalidOperationException] { }
                finally { $hangingChild.Dispose() }
            }
        }
        Remove-Item Env:UPDATE_RELEASE_TEST_LOG,Env:UPDATE_RELEASE_TEST_REMOTE,Env:UPDATE_RELEASE_TEST_BUCKET,Env:UPDATE_RELEASE_TEST_MODE,Env:UPDATE_RELEASE_TEST_SECRET_NAME,Env:UPDATE_RELEASE_TEST_REAL_DOTNET,Env:UPDATE_RELEASE_TEST_SOURCE_INSTALLER,Env:UPDATE_RELEASE_TEST_PFX,Env:UPDATE_RELEASE_TEST_STALE_MANIFEST,Env:UPDATE_RELEASE_TEST_HANG_CHILD_PID -ErrorAction SilentlyContinue
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Recurse -Force }
    }
}

function Test-PublisherReleaseNotesScalars {
    $temporary = New-TestDirectory 'release-notes-scalars'
    $secretName = 'UPDATE_RELEASE_TEST_PASSWORD_' + [Guid]::NewGuid().ToString('N')
    $secretValue = New-TestPassword
    try {
        $astral = [char]::ConvertFromUtf32(0x1f680)
        $maximum = $astral * 8000
        $oversized = $maximum + $astral
        $malformed = [string]::new([char[]]@([char]0xd800))
        foreach ($case in @(
            @{ Name = 'maximum-scalar-release-notes'; Notes = $maximum; Message = 'The signing certificate file is missing.' },
            @{ Name = 'oversized-scalar-release-notes'; Notes = $oversized; Message = 'ReleaseNotes must contain at most 8000 Unicode scalar values.' },
            @{ Name = 'malformed-surrogate-release-notes'; Notes = $malformed; Message = 'ReleaseNotes must contain valid Unicode scalar values.' }
        )) {
            [Environment]::SetEnvironmentVariable($secretName, $secretValue)
            Invoke-ExpectFailure {
                & $publishScript -Version $genuineInstallerVersion -BucketName 'test-bucket' `
                    -PublicBaseUrl 'https://updates.example.test' `
                    -SigningCertificatePath (Join-Path $temporary 'missing.pfx') `
                    -SigningCertificatePasswordEnvironmentVariable $secretName `
                    -Prefix 'updates' -ReleaseNotes $case.Notes -SkipBuild `
                    -InstallerPath (Join-Path $temporary 'missing.exe')
            } $case.Message
            Assert-True ([string]::IsNullOrEmpty([Environment]::GetEnvironmentVariable($secretName))) ($case.Name + ' did not consume the password.')
        }
    }
    finally {
        [Environment]::SetEnvironmentVariable($secretName, $null)
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Recurse -Force }
    }
}

function Test-PublisherConsumesPasswordOnEveryFailure {
    $temporary = New-TestDirectory 'password-preflight'
    $originalPath = $env:PATH
    try {
        $bin = Join-Path $temporary 'fake commands'
        $log = Join-Path $temporary 'commands.log'
        Write-FakeCommands $bin
        $env:PATH = $bin + [System.IO.Path]::PathSeparator + $originalPath
        $env:UPDATE_RELEASE_TEST_LOG = $log
        $env:UPDATE_RELEASE_TEST_MODE = 'preflight'
        $env:UPDATE_RELEASE_TEST_REAL_DOTNET = $realDotnet
        $certificate = Join-Path $temporary 'signer.pfx'
        $installer = Join-Path $temporary 'Setup.exe'
        $password = New-TestPassword
        New-EphemeralPfx $certificate $password
        Write-GenuineInstallerPe $installer

        $cases = @(
            @{ Name = 'invalid-version'; Version = (' ' + $genuineInstallerVersion + ' '); Bucket = 'test-bucket'; Prefix = 'updates'; Base = 'https://updates.example.test'; Certificate = $certificate; Installer = $installer },
            @{ Name = 'invalid-bucket'; Version = $genuineInstallerVersion; Bucket = 'BAD'; Prefix = 'updates'; Base = 'https://updates.example.test'; Certificate = $certificate; Installer = $installer },
            @{ Name = 'invalid-prefix'; Version = $genuineInstallerVersion; Bucket = 'test-bucket'; Prefix = '../updates'; Base = 'https://updates.example.test'; Certificate = $certificate; Installer = $installer },
            @{ Name = 'invalid-base-url'; Version = $genuineInstallerVersion; Bucket = 'test-bucket'; Prefix = 'updates'; Base = 'http://updates.example.test'; Certificate = $certificate; Installer = $installer },
            @{ Name = 'missing-certificate'; Version = $genuineInstallerVersion; Bucket = 'test-bucket'; Prefix = 'updates'; Base = 'https://updates.example.test'; Certificate = (Join-Path $temporary 'missing.pfx'); Installer = $installer },
            @{ Name = 'missing-installer'; Version = $genuineInstallerVersion; Bucket = 'test-bucket'; Prefix = 'updates'; Base = 'https://updates.example.test'; Certificate = $certificate; Installer = (Join-Path $temporary 'missing.exe') }
        )

        foreach ($case in $cases) {
            $secretName = 'UPDATE_RELEASE_TEST_PASSWORD_' + [Guid]::NewGuid().ToString('N')
            $env:UPDATE_RELEASE_TEST_SECRET_NAME = $secretName
            [Environment]::SetEnvironmentVariable($secretName, $password)
            try {
                $publisherFailed = $false
                try {
                    & $publishScript -Version $case.Version -BucketName $case.Bucket `
                        -PublicBaseUrl $case.Base -SigningCertificatePath $case.Certificate `
                        -SigningCertificatePasswordEnvironmentVariable $secretName `
                        -Prefix $case.Prefix -SkipBuild -InstallerPath $case.Installer
                }
                catch {
                    $publisherFailed = $true
                }
                Assert-True $publisherFailed "The $($case.Name) preflight unexpectedly succeeded."
                Assert-True ([string]::IsNullOrEmpty([Environment]::GetEnvironmentVariable($secretName))) "The $($case.Name) path did not consume the password."
            }
            finally {
                [Environment]::SetEnvironmentVariable($secretName, $null)
            }
        }
        Assert-True (-not (Test-Path -LiteralPath $log)) 'A preflight failure started a child process.'
    }
    finally {
        $env:PATH = $originalPath
        Remove-Item Env:UPDATE_RELEASE_TEST_LOG,Env:UPDATE_RELEASE_TEST_MODE,Env:UPDATE_RELEASE_TEST_REAL_DOTNET,Env:UPDATE_RELEASE_TEST_SECRET_NAME -ErrorAction SilentlyContinue
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Recurse -Force }
    }
}

function Test-PublisherRejectsFreshBuildCustomInstallerBeforeWork {
    $temporary = New-TestDirectory 'fresh-build-custom-installer'
    $originalPath = $env:PATH
    $secretName = 'UPDATE_RELEASE_TEST_PASSWORD_' + [Guid]::NewGuid().ToString('N')
    $password = New-TestPassword
    try {
        $bin = Join-Path $temporary 'fake commands'
        $log = Join-Path $temporary 'commands.log'
        Write-FakeCommands $bin
        $env:PATH = $bin + [System.IO.Path]::PathSeparator + $originalPath
        $env:UPDATE_RELEASE_TEST_LOG = $log
        $env:UPDATE_RELEASE_TEST_MODE = 'fresh-build-custom-installer'
        $env:UPDATE_RELEASE_TEST_SECRET_NAME = $secretName
        $env:UPDATE_RELEASE_TEST_REAL_DOTNET = $realDotnet
        $certificate = Join-Path $temporary 'signer.pfx'
        $installer = Join-Path $temporary 'stale-custom-installer.exe'
        New-EphemeralPfx $certificate $password
        Write-GenuineInstallerPe $installer
        [Environment]::SetEnvironmentVariable($secretName, $password)

        Invoke-ExpectFailure {
            & $publishScript -Version $genuineInstallerVersion -BucketName 'test-bucket' `
                -PublicBaseUrl 'https://updates.example.test' `
                -SigningCertificatePath $certificate `
                -SigningCertificatePasswordEnvironmentVariable $secretName `
                -Prefix 'updates' -InstallerPath $installer
        } 'InstallerPath cannot be supplied unless SkipBuild is set.'

        Assert-True ([string]::IsNullOrEmpty([Environment]::GetEnvironmentVariable($secretName))) 'The fresh-build custom-path rejection did not consume the password.'
        Assert-True (-not (Test-Path -LiteralPath $log)) 'A stale custom installer started a child process.'
    }
    finally {
        [Environment]::SetEnvironmentVariable($secretName, $null)
        $env:PATH = $originalPath
        Remove-Item Env:UPDATE_RELEASE_TEST_LOG,Env:UPDATE_RELEASE_TEST_MODE,Env:UPDATE_RELEASE_TEST_SECRET_NAME,Env:UPDATE_RELEASE_TEST_REAL_DOTNET -ErrorAction SilentlyContinue
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Recurse -Force }
    }
}

function Test-CallerWrapperClearsPasswordOnBindingFailure {
    $secretName = 'UPDATE_RELEASE_TEST_PASSWORD_' + [Guid]::NewGuid().ToString('N')
    $password = New-TestPassword
    [Environment]::SetEnvironmentVariable($secretName, $password)
    $failed = $false
    $parameters = @{
        Version = $genuineInstallerVersion
        BucketName = 'test-bucket'
        PublicBaseUrl = 'https://updates.example.test'
        SigningCertificatePath = 'unused.pfx'
        SigningCertificatePasswordEnvironmentVariable = $secretName
        UnknownBindingParameter = 'unknown-binding-parameter'
    }
    try {
        try {
            & $publishScript @parameters
        }
        catch {
            $failed = $true
        }
    }
    finally {
        [Environment]::SetEnvironmentVariable($secretName, $null)
    }

    Assert-True $failed 'The unknown publisher parameter unexpectedly bound.'
    Assert-True ([string]::IsNullOrEmpty([Environment]::GetEnvironmentVariable($secretName))) 'The caller wrapper did not clear the password after parameter binding failed.'
}

function Test-PublisherRejectsInvalidInstallerMetadataBeforePublication {
    $temporary = New-TestDirectory 'installer-metadata'
    $originalPath = $env:PATH
    $secretName = 'UPDATE_RELEASE_TEST_PASSWORD_' + [Guid]::NewGuid().ToString('N')
    $password = New-TestPassword
    try {
        $bin = Join-Path $temporary 'fake commands'
        $log = Join-Path $temporary 'commands.log'
        Write-FakeCommands $bin
        $env:PATH = $bin + [System.IO.Path]::PathSeparator + $originalPath
        $env:UPDATE_RELEASE_TEST_LOG = $log
        $env:UPDATE_RELEASE_TEST_SECRET_NAME = $secretName
        $env:UPDATE_RELEASE_TEST_REAL_DOTNET = $realDotnet
        $certificate = Join-Path $temporary 'signer.pfx'
        New-EphemeralPfx $certificate $password

        $stub = Join-Path $temporary 'metadata-stub.exe'
        Write-MinimalPe $stub 0x41
        $wrongProduct = Join-Path $temporary 'metadata-wrong-product.exe'
        Copy-Item -LiteralPath $signerAssembly -Destination $wrongProduct
        $wrongVersion = Join-Path $temporary 'metadata-wrong-version.exe'
        Write-GenuineInstallerPe $wrongVersion
        $malformed = Join-Path $temporary 'metadata-malformed.exe'
        [System.IO.File]::WriteAllText($malformed, 'not a PE image')
        $cases = @(
            @{ Name = 'metadata-stub'; Path = $stub; Version = $genuineInstallerVersion },
            @{ Name = 'metadata-wrong-product'; Path = $wrongProduct; Version = $genuineInstallerVersion },
            @{ Name = 'metadata-wrong-version'; Path = $wrongVersion; Version = $differentGenuineInstallerVersion },
            @{ Name = 'metadata-malformed'; Path = $malformed; Version = $genuineInstallerVersion }
        )

        foreach ($case in $cases) {
            $env:UPDATE_RELEASE_TEST_MODE = $case.Name
            if (Test-Path -LiteralPath $log) { Remove-Item -LiteralPath $log -Force }
            [Environment]::SetEnvironmentVariable($secretName, $password)
            $failed = $false
            try {
                & $publishScript -Version $case.Version -BucketName 'test-bucket' `
                    -PublicBaseUrl 'https://updates.example.test' `
                    -SigningCertificatePath $certificate `
                    -SigningCertificatePasswordEnvironmentVariable $secretName `
                    -Prefix 'updates' -SkipBuild -InstallerPath $case.Path
            }
            catch {
                $failed = $true
            }
            Assert-True $failed "The $($case.Name) installer unexpectedly passed credibility validation."
            Assert-True ([string]::IsNullOrEmpty([Environment]::GetEnvironmentVariable($secretName))) "The $($case.Name) rejection did not consume the password."
            $lines = if (Test-Path -LiteralPath $log) { @(Get-Content -LiteralPath $log) } else { @() }
            Assert-True (@($lines | Where-Object {
                $_.StartsWith('signer-export-public-key|', [StringComparison]::Ordinal) -or
                $_.StartsWith('signer-sign|', [StringComparison]::Ordinal) -or
                $_.StartsWith('r2-', [StringComparison]::Ordinal) -or
                $_.StartsWith('public-', [StringComparison]::Ordinal)
            }).Count -eq 0) "The $($case.Name) rejection reached signing or network work."
        }
    }
    finally {
        [Environment]::SetEnvironmentVariable($secretName, $null)
        $env:PATH = $originalPath
        Remove-Item Env:UPDATE_RELEASE_TEST_LOG,Env:UPDATE_RELEASE_TEST_MODE,Env:UPDATE_RELEASE_TEST_SECRET_NAME,Env:UPDATE_RELEASE_TEST_REAL_DOTNET -ErrorAction SilentlyContinue
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Recurse -Force }
    }
}

function Test-PublisherRejectsInstallerCertificateAliasesBeforeWork {
    $temporary = New-TestDirectory 'installer-certificate-aliases'
    $originalPath = $env:PATH
    $secretName = 'UPDATE_RELEASE_TEST_PASSWORD_' + [Guid]::NewGuid().ToString('N')
    $password = New-TestPassword
    try {
        $bin = Join-Path $temporary 'fake commands'
        $log = Join-Path $temporary 'commands.log'
        Write-FakeCommands $bin
        $env:PATH = $bin + [System.IO.Path]::PathSeparator + $originalPath
        $env:UPDATE_RELEASE_TEST_LOG = $log
        $env:UPDATE_RELEASE_TEST_MODE = 'alias-preflight'
        $env:UPDATE_RELEASE_TEST_SECRET_NAME = $secretName
        $env:UPDATE_RELEASE_TEST_REAL_DOTNET = $realDotnet

        $certificate = Join-Path $temporary 'signing-material.pfx'
        New-EphemeralPfx $certificate $password
        $originalPfxHash = (Get-FileHash -LiteralPath $certificate -Algorithm SHA256).Hash
        $hardlink = Join-Path $temporary 'hardlink.exe'
        $copy = Join-Path $temporary 'copied-key-material.exe'
        New-Item -ItemType HardLink -Path $hardlink -Target $certificate | Out-Null
        Copy-Item -LiteralPath $certificate -Destination $copy

        $sameExtensionCertificate = Join-Path $temporary 'SigningMaterial.EXE'
        New-EphemeralPfx $sameExtensionCertificate $password
        $relativeCertificate = [System.IO.Path]::GetRelativePath($temporary, $certificate)
        $cases = @(
            @{ Certificate = $certificate; Installer = $certificate; Relative = $false },
            @{ Certificate = $certificate; Installer = $relativeCertificate; Relative = $true },
            @{ Certificate = $sameExtensionCertificate; Installer = (Join-Path $temporary 'signingmaterial.exe'); Relative = $false },
            @{ Certificate = $certificate; Installer = $hardlink; Relative = $false },
            @{ Certificate = $certificate; Installer = $copy; Relative = $false }
        )

        foreach ($case in $cases) {
            $caseCertificateHash = (Get-FileHash -LiteralPath $case.Certificate -Algorithm SHA256).Hash
            [Environment]::SetEnvironmentVariable($secretName, $password)
            if ($case.Relative) { Push-Location $temporary }
            try {
                $publisherFailed = $false
                try {
                    & $publishScript -Version $genuineInstallerVersion -BucketName 'test-bucket' `
                        -PublicBaseUrl 'https://updates.example.test' `
                        -SigningCertificatePath $case.Certificate `
                        -SigningCertificatePasswordEnvironmentVariable $secretName `
                        -Prefix 'updates' -SkipBuild -InstallerPath $case.Installer
                }
                catch {
                    $publisherFailed = $true
                }
                Assert-True $publisherFailed 'An installer/certificate alias unexpectedly passed preflight.'
            }
            finally {
                if ($case.Relative) { Pop-Location }
            }
            Assert-True ([string]::IsNullOrEmpty([Environment]::GetEnvironmentVariable($secretName))) 'An alias rejection did not consume the password.'
            Assert-True ((Get-FileHash -LiteralPath $case.Certificate -Algorithm SHA256).Hash -ceq $caseCertificateHash) 'The PFX bytes changed while rejecting an alias.'
        }

        Assert-True (-not (Test-Path -LiteralPath $log)) 'An installer/certificate alias started a child process.'
        Assert-True ((Get-FileHash -LiteralPath $certificate -Algorithm SHA256).Hash -ceq $originalPfxHash) 'The PFX bytes changed while rejecting aliases.'
    }
    finally {
        [Environment]::SetEnvironmentVariable($secretName, $null)
        $env:PATH = $originalPath
        Remove-Item Env:UPDATE_RELEASE_TEST_LOG,Env:UPDATE_RELEASE_TEST_MODE,Env:UPDATE_RELEASE_TEST_SECRET_NAME,Env:UPDATE_RELEASE_TEST_REAL_DOTNET -ErrorAction SilentlyContinue
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Recurse -Force }
    }
}

Test-InnoSetupCandidateSelectionPolicy
Test-InnoSetupPathChainPolicy
Test-BuildReleaseInnoRegistrationBoundary
Test-BuildReleaseValidation
Test-SemanticVersionValidation
Test-PublisherConsumesPasswordOnEveryFailure
Test-PublisherRejectsFreshBuildCustomInstallerBeforeWork
Test-CallerWrapperClearsPasswordOnBindingFailure
Test-PublisherRejectsInvalidInstallerMetadataBeforePublication
Test-PublisherRejectsInstallerCertificateAliasesBeforeWork
Test-PublisherReleaseNotesScalars
Invoke-PublisherCase -Mode 'normal' -Existing 'absent' -ExpectSuccess $true
Invoke-PublisherCase -Mode 'normal' -Existing 'same' -ExpectSuccess $true
Invoke-PublisherCase -Mode 'normal' -Existing 'different' -ExpectSuccess $false
Invoke-PublisherCase -Mode 'remote-mismatch' -Existing 'absent' -ExpectSuccess $false
Invoke-PublisherCase -Mode 'authenticated-preflight-oversize' -Existing 'same' -ExpectSuccess $false
Invoke-PublisherCase -Mode 'authenticated-installer-readback-oversize' -Existing 'absent' -ExpectSuccess $false
Invoke-PublisherCase -Mode 'authenticated-partial-failure' -Existing 'absent' -ExpectSuccess $false
Invoke-PublisherCase -Mode 'authenticated-missing-length' -Existing 'absent' -ExpectSuccess $true
Invoke-PublisherCase -Mode 'authenticated-dishonest-length' -Existing 'same' -ExpectSuccess $false
Invoke-PublisherCase -Mode 'authenticated-during-materialization-overflow' -Existing 'same' -ExpectSuccess $false
Invoke-PublisherCase -Mode 'authenticated-hanging-child-timeout' -Existing 'same' -ExpectSuccess $false
Invoke-PublisherCase -Mode 'permission-failure' -Existing 'absent' -ExpectSuccess $false
Invoke-PublisherCase -Mode 'network-failure' -Existing 'absent' -ExpectSuccess $false
Invoke-PublisherCase -Mode 'source-mutation' -Existing 'absent' -ExpectSuccess $true
Invoke-PublisherCase -Mode 'creation-boundary-swap' -Existing 'absent' -ExpectSuccess $true
Invoke-PublisherCase -Mode 'validation-boundary-swap' -Existing 'absent' -ExpectSuccess $true
Invoke-PublisherCase -Mode 'public-wrong' -Existing 'absent' -ExpectSuccess $false
Invoke-PublisherCase -Mode 'public-partial-failure' -Existing 'absent' -ExpectSuccess $false
Invoke-PublisherCase -Mode 'public-installer-oversize' -Existing 'absent' -ExpectSuccess $false
Invoke-PublisherCase -Mode 'public-stale' -Existing 'absent' -ExpectSuccess $false
Invoke-PublisherCase -Mode 'public-downgrade' -Existing 'absent' -ExpectSuccess $false
Invoke-PublisherCase -Mode 'public-unsafe-credentials' -Existing 'absent' -ExpectSuccess $false
Invoke-PublisherCase -Mode 'public-unsafe-fragment' -Existing 'absent' -ExpectSuccess $false
Invoke-PublisherCase -Mode 'public-oversize' -Existing 'absent' -ExpectSuccess $false
Invoke-PublisherCase -Mode 'public-failure' -Existing 'absent' -ExpectSuccess $false
Invoke-PublisherCase -Mode 'public-mixed-failure' -Existing 'absent' -ExpectSuccess $false
Invoke-PublisherCase -Mode 'authenticated-manifest-readback-oversize' -Existing 'absent' -ExpectSuccess $false
Write-Host 'Release-script behavioral tests passed.'
