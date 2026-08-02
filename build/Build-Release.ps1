[CmdletBinding()]
param(
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = [System.IO.Path]::GetFullPath(
    (Join-Path -Path $PSScriptRoot -ChildPath '..'))
$solutionPath = Join-Path -Path $repositoryRoot -ChildPath 'CloudflareR2Uploader.sln'
$releaseExecutable = Join-Path -Path $repositoryRoot -ChildPath 'src\CloudflareR2Uploader\bin\Release\CloudflareR2Uploader.exe'
$testAssembly = Join-Path -Path $repositoryRoot -ChildPath 'tests\CloudflareR2Uploader.Tests\bin\Release\CloudflareR2Uploader.Tests.dll'
$distributionDirectory = Join-Path -Path $repositoryRoot -ChildPath 'dist'
$distributionExecutable = Join-Path -Path $distributionDirectory -ChildPath 'CloudflareR2Uploader.exe'
$iconGenerator = Join-Path -Path $PSScriptRoot -ChildPath 'Generate-BrandIcon.ps1'

$vswherePath = Join-Path -Path ${env:ProgramFiles(x86)} -ChildPath 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $vswherePath -PathType Leaf)) {
    throw 'vswhere.exe was not found. Install Visual Studio with the .NET desktop development workload.'
}

$visualStudioPath = & $vswherePath -latest -products * -requires Microsoft.Component.MSBuild -property installationPath
if ([string]::IsNullOrWhiteSpace($visualStudioPath)) {
    throw 'A Visual Studio installation containing MSBuild was not found.'
}

$msbuildPath = Join-Path -Path $visualStudioPath -ChildPath 'MSBuild\Current\Bin\MSBuild.exe'
$vstestPath = Join-Path -Path $visualStudioPath -ChildPath 'Common7\IDE\CommonExtensions\Microsoft\TestWindow\vstest.console.exe'

if (-not (Test-Path -LiteralPath $msbuildPath -PathType Leaf)) {
    throw "MSBuild was not found at $msbuildPath"
}

& $iconGenerator

& $msbuildPath $solutionPath -t:Restore,Rebuild -p:Configuration=Release -v:minimal -nologo
if ($LASTEXITCODE -ne 0) {
    throw "The Release build failed with exit code $LASTEXITCODE."
}

if (-not $SkipTests) {
    if (-not (Test-Path -LiteralPath $vstestPath -PathType Leaf)) {
        throw "VSTest was not found at $vstestPath"
    }

    & $vstestPath $testAssembly /Platform:x64
    if ($LASTEXITCODE -ne 0) {
        throw "The automated tests failed with exit code $LASTEXITCODE."
    }
}

if (-not (Test-Path -LiteralPath $releaseExecutable -PathType Leaf)) {
    throw "The woven Release executable was not produced at $releaseExecutable"
}

if (-not (Test-Path -LiteralPath $distributionDirectory -PathType Container)) {
    New-Item -ItemType Directory -Path $distributionDirectory | Out-Null
}

if (Test-Path -LiteralPath $distributionExecutable -PathType Leaf) {
    Remove-Item -LiteralPath $distributionExecutable -Force
}

Copy-Item -LiteralPath $releaseExecutable -Destination $distributionExecutable

Add-Type -AssemblyName System.Drawing

# Confirm the icon that Windows extracts from the produced PE is the same 32 px
# Saboreq frame generated above. Comparing pixels avoids false failures caused by
# different PNG encoder metadata after the compiler embeds the ICO resources.
$iconBytes = [System.IO.File]::ReadAllBytes(
    (Join-Path -Path $repositoryRoot -ChildPath 'src\CloudflareR2Uploader\Resources\app.ico'))
$iconCount = [System.BitConverter]::ToUInt16($iconBytes, 4)
$iconEntryOffset = -1

for ($index = 0; $index -lt $iconCount; $index++) {
    $entry = 6 + (16 * $index)
    $width = if ($iconBytes[$entry] -eq 0) { 256 } else { [int]$iconBytes[$entry] }
    if ($width -eq 32) {
        $iconEntryOffset = $entry
        break
    }
}

if ($iconEntryOffset -lt 0) {
    throw 'The generated brand icon does not contain a 32 px frame.'
}

$frameLength = [System.BitConverter]::ToUInt32($iconBytes, $iconEntryOffset + 8)
$frameOffset = [System.BitConverter]::ToUInt32($iconBytes, $iconEntryOffset + 12)
$frameBytes = New-Object byte[] $frameLength
[System.Array]::Copy($iconBytes, $frameOffset, $frameBytes, 0, $frameLength)

$frameStream = [System.IO.MemoryStream]::new($frameBytes, $false)
$sourceBitmap = [System.Drawing.Bitmap]::new($frameStream)
$executableIcon = [System.Drawing.Icon]::ExtractAssociatedIcon($distributionExecutable)

if ($null -eq $executableIcon) {
    $sourceBitmap.Dispose()
    $frameStream.Dispose()
    throw 'Windows could not extract an application icon from the produced EXE.'
}

$executableBitmap = $executableIcon.ToBitmap()
try {
    if ($sourceBitmap.Width -ne $executableBitmap.Width -or
        $sourceBitmap.Height -ne $executableBitmap.Height) {
        throw 'The produced EXE icon dimensions do not match the generated brand icon.'
    }

    for ($y = 0; $y -lt $sourceBitmap.Height; $y++) {
        for ($x = 0; $x -lt $sourceBitmap.Width; $x++) {
            if ($sourceBitmap.GetPixel($x, $y).ToArgb() -ne
                $executableBitmap.GetPixel($x, $y).ToArgb()) {
                throw "The produced EXE icon differs from the Saboreq brand icon at pixel $x,$y."
            }
        }
    }
}
finally {
    $executableBitmap.Dispose()
    $executableIcon.Dispose()
    $sourceBitmap.Dispose()
    $frameStream.Dispose()
}

$distributionFiles = @(Get-ChildItem -LiteralPath $distributionDirectory -File)
if ($distributionFiles.Count -ne 1 -or $distributionFiles[0].Name -ne 'CloudflareR2Uploader.exe') {
    throw 'The dist directory contains files other than CloudflareR2Uploader.exe. Remove unrelated files before publishing it.'
}

$result = Get-Item -LiteralPath $distributionExecutable
Write-Host ('Release ready: {0} ({1:N0} bytes)' -f $result.FullName, $result.Length)
