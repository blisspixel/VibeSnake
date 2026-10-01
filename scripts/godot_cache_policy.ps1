# Compare the complete installed editor with an already checksum-verified ZIP.
function Test-GodotArchiveExtraction {
    param(
        [Parameter(Mandatory)][string]$ArchivePath,
        [Parameter(Mandatory)][string]$ExtractionRoot
    )

    if (-not (Test-Path -LiteralPath $ExtractionRoot -PathType Container)) {
        return $false
    }
    if ((Get-Item -LiteralPath $ExtractionRoot -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
        return $false
    }
    $comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    $root = [System.IO.Path]::GetFullPath($ExtractionRoot)
    $prefix = $root.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    $expectedPaths = [Collections.Generic.HashSet[string]]::new(
        $(if ($IsWindows) { [StringComparer]::OrdinalIgnoreCase } else { [StringComparer]::Ordinal }))
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $zip = [System.IO.Compression.ZipFile]::OpenRead($ArchivePath)
    try {
        foreach ($entry in $zip.Entries) {
            if ($entry.FullName.EndsWith("/", [StringComparison]::Ordinal)) {
                continue
            }
            $entryPath = [System.IO.Path]::GetFullPath((Join-Path $root $entry.FullName))
            if (-not $entryPath.StartsWith($prefix, $comparison) -or -not $expectedPaths.Add($entryPath)) {
                throw "Godot archive contains an unsafe or duplicate extraction path."
            }
            if (-not (Test-Path -LiteralPath $entryPath -PathType Leaf)) { return $false }
            $file = Get-Item -LiteralPath $entryPath
            if ($file.Length -ne $entry.Length -or
                ($file.Attributes -band [System.IO.FileAttributes]::ReparsePoint)) { return $false }
            $parent = $file.Directory
            while ($parent -and $parent.FullName.StartsWith($prefix, $comparison)) {
                if ($parent.Attributes -band [System.IO.FileAttributes]::ReparsePoint) { return $false }
                $parent = $parent.Parent
            }
            $stream = $entry.Open()
            try {
                $archiveHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream))
            } finally { $stream.Dispose() }
            if ((Get-FileHash -LiteralPath $entryPath -Algorithm SHA256).Hash -cne $archiveHash) { return $false }
        }
        foreach ($file in Get-ChildItem -LiteralPath $root -Recurse -File -Force) {
            if (-not $expectedPaths.Contains($file.FullName)) { return $false }
        }
        return $expectedPaths.Count -gt 0
    } finally { $zip.Dispose() }
}
