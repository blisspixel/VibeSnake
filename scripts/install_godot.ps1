[CmdletBinding()]
param(
    [Parameter()]
    [string]$OutputDirectory = ".tools/godot"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$toolchainPath = Join-Path $repositoryRoot "native/toolchain.json"
$toolchain = Get-Content -LiteralPath $toolchainPath -Raw | ConvertFrom-Json
$version = [string]$toolchain.godot.version

if ($IsWindows) {
    $platformId = "windows-x64"
} elseif ($IsMacOS) {
    $platformId = "macos-universal"
} elseif ($IsLinux) {
    $platformId = "linux-x64"
} else {
    throw "Godot bootstrap does not support this operating system."
}

$archive = $toolchain.godot.archives.$platformId
if ($null -eq $archive) {
    throw "No Godot archive is configured for $platformId."
}

$resolvedOutputRoot = if ([System.IO.Path]::IsPathRooted($OutputDirectory)) {
    [System.IO.Path]::GetFullPath($OutputDirectory)
} else {
    [System.IO.Path]::GetFullPath((Join-Path $repositoryRoot $OutputDirectory))
}
$installDirectory = Join-Path $resolvedOutputRoot $version
$downloadDirectory = Join-Path $resolvedOutputRoot "downloads"
$cachedArchive = Join-Path $downloadDirectory ([string]$archive.file)
$stagingDirectory = Join-Path $resolvedOutputRoot ("{0}.staging.{1}" -f $version, [Guid]::NewGuid())
$temporaryArchive = $null

$safeOutputPrefix = $resolvedOutputRoot.TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar
) + [System.IO.Path]::DirectorySeparatorChar
foreach ($managedPath in @($installDirectory, $downloadDirectory, $stagingDirectory)) {
    $resolvedManagedPath = [System.IO.Path]::GetFullPath($managedPath)
    if (-not $resolvedManagedPath.StartsWith($safeOutputPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to manage a Godot path outside the requested output root: $resolvedManagedPath"
    }
}

function Find-GodotExecutable {
    param([Parameter(Mandatory)][string]$Root)

    if (-not (Test-Path -LiteralPath $Root -PathType Container)) {
        return $null
    }

    $files = Get-ChildItem -LiteralPath $Root -Recurse -File
    if ($IsWindows) {
        return $files | Where-Object { $_.Name -like "Godot*_console.exe" } | Select-Object -First 1 -ExpandProperty FullName
    }

    if ($IsMacOS) {
        return $files |
            Where-Object { $_.Name -eq "Godot" -and $_.FullName -match "[\\/]Contents[\\/]MacOS[\\/]Godot$" } |
            Select-Object -First 1 -ExpandProperty FullName
    }

    return $files |
        Where-Object { $_.Name -match "^Godot.*mono_linux.*x86_64$" } |
        Select-Object -First 1 -ExpandProperty FullName
}

function Publish-GodotPath {
    param(
        [Parameter(Mandatory)][string]$Executable,
        [Parameter(Mandatory)][string]$ArchivePath
    )

    if (-not $IsWindows) {
        & chmod +x $Executable
        if ($LASTEXITCODE -ne 0) {
            throw "Could not mark the Godot executable as runnable."
        }
    }

    $verificationOutput = & (Join-Path $PSScriptRoot "assert_godot_toolchain.ps1") `
        -GodotExecutable $Executable `
        -GodotArchivePath $ArchivePath
    $verificationOutput | Write-Output
    $verifiedVersionLine = $verificationOutput |
        Where-Object { $_ -like "GodotVerifiedVersion=*" } |
        Select-Object -Last 1
    if (-not $verifiedVersionLine) {
        throw "Godot verification did not report an exact build identity."
    }
    $reportedVersion = $verifiedVersionLine.Substring("GodotVerifiedVersion=".Length)

    if ($env:GITHUB_OUTPUT) {
        "godot-path=$Executable" | Out-File -LiteralPath $env:GITHUB_OUTPUT -Encoding utf8 -Append
    }

    Write-Output "GodotExecutable=$Executable"
    Write-Output "GodotVersion=$reportedVersion"
}

try {
    New-Item -ItemType Directory -Path $downloadDirectory -Force | Out-Null
    if (-not (Test-Path -LiteralPath $cachedArchive -PathType Leaf)) {
        $temporaryArchive = "$cachedArchive.download.$([Guid]::NewGuid())"
        $downloadUrl = "{0}/{1}" -f $toolchain.godot.releaseBaseUrl, $archive.file
        Write-Output "Downloading Godot $version for $platformId"
        Invoke-WebRequest -Uri $downloadUrl -OutFile $temporaryArchive
        $downloadedHash = (Get-FileHash -LiteralPath $temporaryArchive -Algorithm SHA512).Hash.ToLowerInvariant()
        $expectedHash = ([string]$archive.sha512).ToLowerInvariant()
        if ($downloadedHash -ne $expectedHash) {
            throw "Godot archive checksum mismatch for $platformId."
        }
        Move-Item -LiteralPath $temporaryArchive -Destination $cachedArchive
        $temporaryArchive = $null
    }

    $actualHash = (Get-FileHash -LiteralPath $cachedArchive -Algorithm SHA512).Hash.ToLowerInvariant()
    $expectedHash = ([string]$archive.sha512).ToLowerInvariant()
    if ($actualHash -ne $expectedHash) {
        throw "Cached Godot archive checksum mismatch for $platformId. Remove $cachedArchive and retry."
    }

    New-Item -ItemType Directory -Path $stagingDirectory | Out-Null
    Expand-Archive -LiteralPath $cachedArchive -DestinationPath $stagingDirectory
    $stagedExecutable = Find-GodotExecutable -Root $stagingDirectory
    if (-not $stagedExecutable) {
        throw "The verified Godot archive did not contain the expected executable."
    }

    if (Test-Path -LiteralPath $installDirectory) {
        [System.IO.Directory]::Delete([System.IO.Path]::GetFullPath($installDirectory), $true)
    }
    Move-Item -LiteralPath $stagingDirectory -Destination $installDirectory
} finally {
    if ($temporaryArchive -and (Test-Path -LiteralPath $temporaryArchive)) {
        Remove-Item -LiteralPath $temporaryArchive -Force
    }
    if (Test-Path -LiteralPath $stagingDirectory) {
        [System.IO.Directory]::Delete([System.IO.Path]::GetFullPath($stagingDirectory), $true)
    }
}

$installedExecutable = Find-GodotExecutable -Root $installDirectory
if (-not $installedExecutable) {
    throw "Godot was extracted, but its executable could not be located."
}

Publish-GodotPath -Executable $installedExecutable -ArchivePath $cachedArchive
