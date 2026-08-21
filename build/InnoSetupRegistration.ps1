function Test-InnoSetupLocalDrivePath {
    [CmdletBinding()]
    [OutputType([bool])]
    param(
        [Parameter(Mandatory)]
        [AllowEmptyString()]
        [string]$Path
    )

    # Registry-controlled release tooling must never execute from a network share
    # or a Windows device namespace (for example \\server\share, \\?\, \\.\, or \??\).
    # Accept only a conventional, rooted local DOS drive path such as C:\Program Files.
    if ([string]::IsNullOrWhiteSpace($Path)) { return $false }
    return $Path -cmatch '\A[A-Za-z]:[\\/](?![\\/])'
}

function Test-InnoSetupPathChain {
    [CmdletBinding()]
    [OutputType([bool])]
    param(
        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string]$Path,
        [Parameter(Mandatory)]
        [ValidateSet('Container', 'Leaf')]
        [string]$ExpectedPathType
    )

    # Fail closed without resolving links: every lexical entry from the volume/share
    # root through the requested leaf must exist, be inspectable, and not carry the
    # ReparsePoint flag. This rejects directory junctions and file/directory symlinks
    # rather than silently binding release execution to their mutable targets.
    if (-not (Test-InnoSetupLocalDrivePath -Path $Path) -or
        -not [System.IO.Path]::IsPathFullyQualified($Path)) { return $false }
    try {
        $canonicalPath = [System.IO.Path]::GetFullPath($Path)
        $root = [System.IO.Path]::GetPathRoot($canonicalPath)
        if ([string]::IsNullOrWhiteSpace($root)) { return $false }

        $current = $root
        $attributes = [System.IO.File]::GetAttributes($current)
        if (($attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) { return $false }

        $segments = $canonicalPath.Substring($root.Length).Split(
            [char[]]@([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar),
            [StringSplitOptions]::RemoveEmptyEntries)
        foreach ($segment in $segments) {
            $current = [System.IO.Path]::Combine($current, $segment)
            $attributes = [System.IO.File]::GetAttributes($current)
            if (($attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) { return $false }
        }

        $isDirectory = ($attributes -band [System.IO.FileAttributes]::Directory) -ne 0
        return ($ExpectedPathType -ceq 'Container' -and $isDirectory) -or
            ($ExpectedPathType -ceq 'Leaf' -and -not $isDirectory)
    }
    catch {
        return $false
    }
}

function Test-InnoSetupRegistrationCandidate {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [ValidateNotNull()]
        [psobject]$Candidate,
        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string]$RequiredVersion
    )

    $expectedSubKey = 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1'
    $knownHive = [string]::Equals([string]$Candidate.RegistryHive, 'LocalMachine', [StringComparison]::Ordinal) -or
        [string]::Equals([string]$Candidate.RegistryHive, 'CurrentUser', [StringComparison]::Ordinal)
    if (-not [string]::Equals([string]$Candidate.RegistryView, 'Registry32', [StringComparison]::Ordinal) -or
        -not $knownHive -or
        -not [string]::Equals([string]$Candidate.AppId, 'Inno Setup 6', [StringComparison]::Ordinal) -or
        -not [string]::Equals([string]$Candidate.RegistrySubKey, $expectedSubKey, [StringComparison]::Ordinal) -or
        -not [string]::Equals([string]$Candidate.DisplayVersionKind, 'String', [StringComparison]::Ordinal) -or
        -not [string]::Equals([string]$Candidate.InstallLocationKind, 'String', [StringComparison]::Ordinal) -or
        -not [string]::Equals([string]$Candidate.DisplayVersion, $RequiredVersion, [StringComparison]::Ordinal) -or
        [string]::IsNullOrWhiteSpace([string]$Candidate.InstallLocation) -or
        -not [bool]$Candidate.CompilerExists -or
        -not [bool]$Candidate.InstallPathTrusted -or
        -not [bool]$Candidate.CompilerFileTrusted) {
        return $false
    }

    if (-not (Test-InnoSetupLocalDrivePath -Path ([string]$Candidate.InstallLocation)) -or
        -not (Test-InnoSetupLocalDrivePath -Path ([string]$Candidate.CompilerPath)) -or
        -not [System.IO.Path]::IsPathFullyQualified([string]$Candidate.InstallLocation) -or
        -not [System.IO.Path]::IsPathFullyQualified([string]$Candidate.CompilerPath)) {
        return $false
    }

    try {
        $canonicalInstallLocation = [System.IO.Path]::GetFullPath([string]$Candidate.InstallLocation)
        $canonicalCompiler = [System.IO.Path]::GetFullPath([string]$Candidate.CompilerPath)
        $expectedCompiler = [System.IO.Path]::GetFullPath(
            [System.IO.Path]::Combine($canonicalInstallLocation, 'ISCC.exe'))
    }
    catch {
        return $false
    }

    return [string]::Equals(
            [System.IO.Path]::GetFileName($canonicalCompiler),
            'ISCC.exe',
            [StringComparison]::OrdinalIgnoreCase) -and
        [string]::Equals($canonicalCompiler, $expectedCompiler, [StringComparison]::OrdinalIgnoreCase)
}

function Select-InnoSetupCompilerCandidate {
    [CmdletBinding()]
    param(
        [AllowNull()]
        [object[]]$Candidates,
        [AllowEmptyString()]
        [string]$RequestedCompiler,
        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string]$RequiredVersion
    )

    if (-not [string]::Equals($RequiredVersion, '6.7.1', [StringComparison]::Ordinal)) {
        throw 'Only the reviewed official Inno Setup 6.7.1 package registration is supported.'
    }

    $validCompilers = [System.Collections.Generic.List[string]]::new()
    $seenCompilers = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($candidate in @($Candidates)) {
        if ($null -eq $candidate -or
            -not (Test-InnoSetupRegistrationCandidate -Candidate $candidate -RequiredVersion $RequiredVersion)) {
            continue
        }
        $canonicalCompiler = [System.IO.Path]::GetFullPath([string]$candidate.CompilerPath)
        if ($seenCompilers.Add($canonicalCompiler)) {
            $validCompilers.Add($canonicalCompiler)
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($RequestedCompiler)) {
        if (-not (Test-InnoSetupLocalDrivePath -Path $RequestedCompiler) -or
            -not [System.IO.Path]::IsPathFullyQualified($RequestedCompiler)) {
            throw 'InnoSetupCompiler must resolve to the ISCC.exe registered by official Inno Setup 6.7.1.'
        }
        try {
            $requestedFullPath = [System.IO.Path]::GetFullPath($RequestedCompiler)
        }
        catch {
            throw 'InnoSetupCompiler must resolve to the ISCC.exe registered by official Inno Setup 6.7.1.'
        }
        if (-not [string]::Equals(
                [System.IO.Path]::GetFileName($requestedFullPath),
                'ISCC.exe',
                [StringComparison]::OrdinalIgnoreCase)) {
            throw 'InnoSetupCompiler must resolve to the ISCC.exe registered by official Inno Setup 6.7.1.'
        }

        $matches = @($validCompilers | Where-Object {
                [string]::Equals($_, $requestedFullPath, [StringComparison]::OrdinalIgnoreCase)
            })
        if ($matches.Count -eq 1) { return $matches[0] }
        throw 'InnoSetupCompiler must resolve to the ISCC.exe registered by official Inno Setup 6.7.1.'
    }

    if ($validCompilers.Count -eq 0) {
        throw 'Official Inno Setup 6.7.1 is not registered. Install the reviewed 6.7.1 package before building a release.'
    }
    if ($validCompilers.Count -ne 1) {
        throw 'Multiple distinct official Inno Setup 6.7.1 compilers are registered. Set InnoSetupCompiler to one registered ISCC.exe explicitly.'
    }
    return $validCompilers[0]
}

function Get-InnoSetupRegistrationCandidates {
    [CmdletBinding()]
    param()

    # Official Inno Setup 6.7.1 declares AppId "Inno Setup 6" and AppVersion
    # 6.7.1. That AppId produces this exact uninstall key. The official package
    # writes the registration into the 32-bit uninstall view, including on x64.
    # DisplayVersion and InstallLocation are language-invariant registry values;
    # ISCC.exe itself intentionally has unhelpful 0.0.0.0 file metadata.
    # Primary sources:
    # https://github.com/jrsoftware/issrc/blob/is-6_7_1/setup.iss
    # https://jrsoftware.org/ishelp/topic_setup_appid.htm
    $uninstallSubKey = 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1'
    $registryHives = @(
        [Microsoft.Win32.RegistryHive]::LocalMachine,
        [Microsoft.Win32.RegistryHive]::CurrentUser
    )
    $candidates = [System.Collections.Generic.List[object]]::new()

    foreach ($hive in $registryHives) {
        $baseKey = $null
        $uninstallKey = $null
        try {
            $baseKey = [Microsoft.Win32.RegistryKey]::OpenBaseKey(
                $hive,
                [Microsoft.Win32.RegistryView]::Registry32)
            $uninstallKey = $baseKey.OpenSubKey($uninstallSubKey, $false)
            if ($null -eq $uninstallKey) { continue }

            $displayVersion = $uninstallKey.GetValue(
                'DisplayVersion',
                $null,
                [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
            $installLocation = $uninstallKey.GetValue(
                'InstallLocation',
                $null,
                [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
            $displayVersionKind = try { [string]$uninstallKey.GetValueKind('DisplayVersion') } catch { '' }
            $installLocationKind = try { [string]$uninstallKey.GetValueKind('InstallLocation') } catch { '' }
            $compilerPath = $null
            $compilerExists = $false
            $installPathTrusted = $false
            $compilerFileTrusted = $false
            if (-not [string]::IsNullOrWhiteSpace([string]$installLocation) -and
                (Test-InnoSetupLocalDrivePath -Path ([string]$installLocation)) -and
                [System.IO.Path]::IsPathFullyQualified([string]$installLocation)) {
                $compilerPath = [System.IO.Path]::Combine([string]$installLocation, 'ISCC.exe')
                $installPathTrusted = Test-InnoSetupPathChain `
                    -Path ([string]$installLocation) `
                    -ExpectedPathType Container
                $compilerExists = Test-Path -LiteralPath $compilerPath -PathType Leaf
                $compilerFileTrusted = $compilerExists -and
                    (Test-InnoSetupPathChain -Path $compilerPath -ExpectedPathType Leaf)
            }

            $candidates.Add([pscustomobject]@{
                    RegistryHive = [string]$hive
                    RegistryView = 'Registry32'
                    RegistrySubKey = $uninstallSubKey
                    AppId = 'Inno Setup 6'
                    DisplayVersion = [string]$displayVersion
                    DisplayVersionKind = $displayVersionKind
                    InstallLocation = [string]$installLocation
                    InstallLocationKind = $installLocationKind
                    CompilerPath = [string]$compilerPath
                    CompilerExists = $compilerExists
                    InstallPathTrusted = $installPathTrusted
                    CompilerFileTrusted = $compilerFileTrusted
                })
        }
        catch [System.Security.SecurityException] {
            continue
        }
        catch [UnauthorizedAccessException] {
            continue
        }
        catch [System.IO.IOException] {
            continue
        }
        finally {
            if ($null -ne $uninstallKey) { $uninstallKey.Dispose() }
            if ($null -ne $baseKey) { $baseKey.Dispose() }
        }
    }
    return $candidates.ToArray()
}

function Resolve-InnoSetupCompilerRegistration {
    [CmdletBinding()]
    param(
        [AllowEmptyString()]
        [string]$RequestedCompiler,
        [Parameter(Mandatory)]
        [ValidateNotNullOrEmpty()]
        [string]$RequiredVersion
    )

    $candidates = Get-InnoSetupRegistrationCandidates
    return Select-InnoSetupCompilerCandidate `
        -Candidates $candidates `
        -RequestedCompiler $RequestedCompiler `
        -RequiredVersion $RequiredVersion
}
