function Assert-NativeArtifactPath {
    param([Parameter(Mandatory)][string]$RelativePath)

    if (
        [System.IO.Path]::IsPathRooted($RelativePath) -or
        $RelativePath -match "^(?:[A-Za-z]:|[\\/])"
    ) {
        throw "Artifact contains an invalid path: $RelativePath"
    }

    $normalized = $RelativePath.Replace("\", "/")
    $segments = @($normalized.Split("/"))
    $invalidSegment = $segments | Where-Object {
        -not $_ -or $_ -eq "." -or $_ -eq ".."
    } | Select-Object -First 1
    if (-not $normalized -or $null -ne $invalidSegment) {
        throw "Artifact contains an invalid path: $RelativePath"
    }

    # Godot Linux exports ship a product launcher shell beside the binary.
    $allowedProductLaunchers = @("VibeSnake.sh")
    if ($allowedProductLaunchers -contains $normalized) {
        return $normalized
    }

    $prohibitedPathPatterns = @(
        "(^|/)(?:__pycache__|qa_reports|archive|tests?)(?:/|$)",
        "(^|/)(?:\.env[^/]*|packages(?:\.[^/]+)?\.lock\.json)$",
        "\.(?:py|pyc|pyo|ps1|sh|bat|cmd|sln|slnx|csproj|tpz|pfx|pem|key)$",
        "(^|/)(?:python(?:3(?:\.\d+)?)?|pygame)(?:/|$)",
        "(^|/)(?:python(?:w|\d+(?:\.\d+)*)?(?:_d)?\.(?:dll|exe)|libpython[^/]*\.(?:so(?:\.\d+)*|dylib))$",
        "(^|/)python(?:\d+(?:\.\d+)*)?\.framework(?:/|$)"
    )
    foreach ($pattern in $prohibitedPathPatterns) {
        if ($normalized -match $pattern) {
            throw "Artifact contains prohibited content: $normalized"
        }
    }

    return $normalized
}
