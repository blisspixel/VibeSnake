[CmdletBinding()]
param(
    [Parameter()]
    [ValidateSet("current", "windows-x64", "linux-x64", "macos-universal")]
    [string]$PlatformId = "current",

    [Parameter()]
    [string]$OutputDirectory,

    [Parameter()]
    [string]$ArchivePath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$toolchainPath = Join-Path $repositoryRoot "native/toolchain.json"
$toolchain = Get-Content -LiteralPath $toolchainPath -Raw | ConvertFrom-Json
$templates = $toolchain.godot.exportTemplates
$versionDirectory = [string]$templates.versionDirectory
. (Join-Path $PSScriptRoot "platform_path_policy.ps1")

if ($PlatformId -eq "current") {
    if ($IsWindows) {
        $PlatformId = "windows-x64"
    } elseif ($IsMacOS) {
        $PlatformId = "macos-universal"
    } elseif ($IsLinux) {
        $PlatformId = "linux-x64"
    } else {
        throw "Godot export templates are not configured for this operating system."
    }
}

if (-not $OutputDirectory) {
    if ($IsWindows) {
        $applicationData = [Environment]::GetFolderPath([Environment+SpecialFolder]::ApplicationData)
        $OutputDirectory = Join-Path $applicationData "Godot/export_templates"
    } elseif ($IsMacOS) {
        $userProfile = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
        $OutputDirectory = Join-Path $userProfile "Library/Application Support/Godot/export_templates"
    } else {
        $userProfile = [Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)
        $dataRoot = Get-AbsoluteEnvironmentPathOrDefault `
            -ConfiguredPath $env:XDG_DATA_HOME `
            -DefaultPath (Join-Path $userProfile ".local/share")
        $OutputDirectory = Join-Path $dataRoot "godot/export_templates"
    }
}

$resolvedOutputRoot = [System.IO.Path]::GetFullPath($OutputDirectory)
$installDirectory = Join-Path $resolvedOutputRoot $versionDirectory
$downloadDirectory = Join-Path $resolvedOutputRoot ".vibesnake-downloads"

function Get-RequiredTemplatePatterns {
    param([Parameter(Mandatory)][string]$RequestedPlatform)

    switch ($RequestedPlatform) {
        "windows-x64" {
            return @(
                "^windows_debug_x86_64\.exe$",
                "^windows_release_x86_64\.exe$"
            )
        }
        "linux-x64" {
            return @(
                "^linux_debug(?:\.|_)x86_64$",
                "^linux_release(?:\.|_)x86_64$"
            )
        }
        "macos-universal" {
            return @("^macos\.zip$")
        }
        default {
            throw "Unknown export template platform: $RequestedPlatform"
        }
    }
}

function Test-TemplateDirectory {
    param(
        [Parameter(Mandatory)][string]$Directory,
        [Parameter(Mandatory)][string[]]$RequiredPatterns
    )

    if (-not (Test-Path -LiteralPath $Directory -PathType Container)) {
        return $false
    }

    $versionPath = Join-Path $Directory "version.txt"
    if (-not (Test-Path -LiteralPath $versionPath -PathType Leaf)) {
        return $false
    }

    $reportedVersion = (Get-Content -LiteralPath $versionPath -Raw).Trim()
    if ($reportedVersion -ne $versionDirectory) {
        return $false
    }

    $fileNames = Get-ChildItem -LiteralPath $Directory -File | Select-Object -ExpandProperty Name
    foreach ($pattern in $RequiredPatterns) {
        if (-not ($fileNames | Where-Object { $_ -match $pattern })) {
            return $false
        }
    }

    return $true
}

function Publish-TemplateDirectory {
    param([Parameter(Mandatory)][string]$Directory)

    if ($env:GITHUB_OUTPUT) {
        "godot-template-directory=$Directory" | Out-File -LiteralPath $env:GITHUB_OUTPUT -Encoding utf8 -Append
    }

    Write-Output "GodotTemplateDirectory=$Directory"
    Write-Output "GodotTemplatePlatform=$PlatformId"
}

$requiredPatterns = Get-RequiredTemplatePatterns -RequestedPlatform $PlatformId
New-Item -ItemType Directory -Path $resolvedOutputRoot -Force | Out-Null
$stagingDirectory = Join-Path $resolvedOutputRoot ("{0}.staging.{1}" -f $versionDirectory, [Guid]::NewGuid())
$temporaryArchive = $null
$safeOutputPrefix = $resolvedOutputRoot.TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar
) + [System.IO.Path]::DirectorySeparatorChar
foreach ($managedPath in @($installDirectory, $downloadDirectory, $stagingDirectory)) {
    $resolvedManagedPath = [System.IO.Path]::GetFullPath($managedPath)
    if (-not $resolvedManagedPath.StartsWith($safeOutputPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to manage a template path outside the requested output root: $resolvedManagedPath"
    }
}

try {
    if ($ArchivePath) {
        $resolvedArchive = [System.IO.Path]::GetFullPath($ArchivePath)
        if (-not (Test-Path -LiteralPath $resolvedArchive -PathType Leaf)) {
            throw "The supplied template archive does not exist: $resolvedArchive"
        }
    } else {
        New-Item -ItemType Directory -Path $downloadDirectory -Force | Out-Null
        $resolvedArchive = Join-Path $downloadDirectory ([string]$templates.file)
        if (-not (Test-Path -LiteralPath $resolvedArchive -PathType Leaf)) {
            $temporaryArchive = "$resolvedArchive.download.$([Guid]::NewGuid())"
            $downloadUrl = "{0}/{1}" -f $toolchain.godot.releaseBaseUrl, $templates.file
            Write-Output "Downloading Godot $($toolchain.godot.version) .NET export templates"
            Invoke-WebRequest -Uri $downloadUrl -OutFile $temporaryArchive
            $downloadedHash = (Get-FileHash -LiteralPath $temporaryArchive -Algorithm SHA512).Hash.ToLowerInvariant()
            $expectedHash = ([string]$templates.sha512).ToLowerInvariant()
            if ($downloadedHash -ne $expectedHash) {
                throw "Godot export template archive checksum mismatch."
            }
            Move-Item -LiteralPath $temporaryArchive -Destination $resolvedArchive
            $temporaryArchive = $null
        }
    }

    $actualHash = (Get-FileHash -LiteralPath $resolvedArchive -Algorithm SHA512).Hash.ToLowerInvariant()
    $expectedHash = ([string]$templates.sha512).ToLowerInvariant()
    if ($actualHash -ne $expectedHash) {
        throw "Godot export template archive checksum mismatch. Remove $resolvedArchive and retry."
    }

    New-Item -ItemType Directory -Path $stagingDirectory | Out-Null
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [System.IO.Compression.ZipFile]::OpenRead($resolvedArchive)
    try {
        $selectedEntries = @()
        foreach ($entry in $archive.Entries) {
            $entryPath = $entry.FullName.Replace("\", "/").TrimStart("/")
            if ($entryPath.StartsWith("templates/", [StringComparison]::OrdinalIgnoreCase)) {
                $entryPath = $entryPath.Substring("templates/".Length)
            }

            if ($entryPath.Contains("/", [StringComparison]::Ordinal) -or -not $entry.Name) {
                continue
            }

            $isVersion = $entryPath.Equals("version.txt", [StringComparison]::OrdinalIgnoreCase)
            $isPlatformTemplate = $false
            foreach ($pattern in $requiredPatterns) {
                if ($entryPath -match $pattern) {
                    $isPlatformTemplate = $true
                    break
                }
            }

            if ($isVersion -or $isPlatformTemplate) {
                $selectedEntries += [PSCustomObject]@{ Entry = $entry; RelativePath = $entryPath }
            }
        }

        foreach ($selected in $selectedEntries) {
            $destination = Join-Path $stagingDirectory $selected.RelativePath
            $entryStream = $selected.Entry.Open()
            $destinationStream = [System.IO.File]::Create($destination)
            try {
                $entryStream.CopyTo($destinationStream)
            } finally {
                $destinationStream.Dispose()
                $entryStream.Dispose()
            }
        }
    } finally {
        $archive.Dispose()
    }

    if (-not (Test-TemplateDirectory -Directory $stagingDirectory -RequiredPatterns $requiredPatterns)) {
        throw "The verified archive did not contain the expected $PlatformId templates."
    }

    if (Test-Path -LiteralPath $installDirectory) {
        [System.IO.Directory]::Delete([System.IO.Path]::GetFullPath($installDirectory), $true)
    }
    Move-Item -LiteralPath $stagingDirectory -Destination $installDirectory
} finally {
    if (Test-Path -LiteralPath $stagingDirectory) {
        $safePrefix = $resolvedOutputRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
        $resolvedStaging = [System.IO.Path]::GetFullPath($stagingDirectory)
        if (-not $resolvedStaging.StartsWith($safePrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Refusing to clean an unexpected staging directory: $resolvedStaging"
        }

        [System.IO.Directory]::Delete($resolvedStaging, $true)
    }

    if ($temporaryArchive -and (Test-Path -LiteralPath $temporaryArchive)) {
        Remove-Item -LiteralPath $temporaryArchive -Force
    }
}

if (-not (Test-TemplateDirectory -Directory $installDirectory -RequiredPatterns $requiredPatterns)) {
    throw "Godot export templates were installed but did not pass verification."
}

Publish-TemplateDirectory -Directory $installDirectory
