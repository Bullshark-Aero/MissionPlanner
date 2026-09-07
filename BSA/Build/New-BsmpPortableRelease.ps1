[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z-]+(\.[0-9A-Za-z-]+)*)?$')]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d{8}$')]
    [string]$ReleaseDate,

    [Parameter(Mandatory = $true)]
    [string]$SourceDirectory,

    [Parameter(Mandatory = $true)]
    [string]$OutputPath,

    [string]$SevenZipPath = "$env:ProgramFiles\7-Zip\7z.exe"
)

$ErrorActionPreference = 'Stop'
$rootName = "BSMP $Version $ReleaseDate"
$source = (Resolve-Path $SourceDirectory).Path
$productVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $source 'MissionPlanner.exe')).ProductVersion
if ($productVersion -cne $Version) {
    throw "Build ProductVersion '$productVersion' does not match release '$Version'. Build with /p:BsmpVersion=$Version."
}
$output = [System.IO.Path]::GetFullPath($OutputPath)
$work = Join-Path ([System.IO.Path]::GetTempPath()) ("bsmp-release-" + [guid]::NewGuid().ToString('N'))
$stageRoot = Join-Path $work $rootName
$archive = Join-Path $work 'payload.7z'

try {
    if (-not (Test-Path $SevenZipPath -PathType Leaf)) {
        throw "7-Zip was not found at $SevenZipPath"
    }

    New-Item -ItemType Directory -Path $stageRoot | Out-Null
    Copy-Item -Path (Join-Path $source '*') -Destination $stageRoot -Recurse -Force

    $aircraftConfigs = @(Get-ChildItem $stageRoot -Recurse -File -Filter '*.bsampconfig')
    if ($aircraftConfigs.Count -ne 0) {
        throw 'Application release contains an aircraft .bsampconfig file.'
    }

    $aircraftPlugins = @(Get-ChildItem $stageRoot -Recurse -File | Where-Object {
        $_.Name -ieq 'Judicar2600Lights.dll' -or
        $_.Name -ieq 'aero.bullshark.judicar2600.lights.dll'
    })
    if ($aircraftPlugins.Count -ne 0) {
        throw 'Application release contains the aircraft-specific Judicar lights plugin.'
    }

    Push-Location $work
    try {
        # Bound compression memory so packaging also works on 4 GB build VMs.
        & $SevenZipPath a -t7z -mx=9 -md=32m -mmt=2 $archive $rootName | Out-Host
        if ($LASTEXITCODE -ne 0) {
            throw "7-Zip archive creation failed with exit code $LASTEXITCODE"
        }
    }
    finally {
        Pop-Location
    }

    $sfxModule = Join-Path (Split-Path $SevenZipPath) '7z.sfx'
    if (-not (Test-Path $sfxModule -PathType Leaf)) {
        throw "The 7-Zip SFX module was not found at $sfxModule"
    }

    New-Item -ItemType Directory -Path (Split-Path $output) -Force | Out-Null
    $destination = [System.IO.File]::Open($output, [System.IO.FileMode]::Create)
    try {
        foreach ($part in @($sfxModule, $archive)) {
            $input = [System.IO.File]::OpenRead($part)
            try {
                $input.CopyTo($destination)
            }
            finally {
                $input.Dispose()
            }
        }
    }
    finally {
        $destination.Dispose()
    }

    & $SevenZipPath t $output | Out-Host
    if ($LASTEXITCODE -ne 0) {
        throw "Self-extractor verification failed with exit code $LASTEXITCODE"
    }

    $listing = & $SevenZipPath l -slt $output
    $payloadPaths = @($listing | Where-Object { $_ -match '^Path = ' } | ForEach-Object {
        $_.Substring(7)
    } | Where-Object { $_ -ne $output })
    $invalidPaths = @($payloadPaths | Where-Object {
        $_ -ne $rootName -and -not $_.StartsWith($rootName + '\')
    })
    if ($invalidPaths.Count -ne 0) {
        throw "Archive contains paths outside '$rootName': $($invalidPaths -join ', ')"
    }

    $hash = (Get-FileHash $output -Algorithm SHA256).Hash.ToLowerInvariant()
    Write-Output "Output: $output"
    Write-Output "Archive root: $rootName"
    Write-Output "SHA-256: $hash"
}
finally {
    if (Test-Path $work) {
        Remove-Item $work -Recurse -Force
    }
}
