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
        "\.(?:py|pyc|pyo|ps1|sh|bat|cmd|sln|slnx|csproj|tpz|pfx|p12|pem|key|p8|jks|keystore)$",
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

function Assert-ArtifactRespectsContentInventory {
    param(
        [Parameter(Mandatory)][string]$InventoryPath,
        [Parameter(Mandatory)][string[]]$ArtifactRelativePaths
    )

    if (-not (Test-Path -LiteralPath $InventoryPath -PathType Leaf)) {
        throw "Content inventory is required: $InventoryPath"
    }

    $inventory = Get-Content -LiteralPath $InventoryPath -Raw | ConvertFrom-Json
    if ([int]$inventory.schemaVersion -ne 1) {
        throw "Content inventory schemaVersion must be 1."
    }

    $blockedInventoryPaths = [System.Collections.Generic.HashSet[string]]::new(
        [StringComparer]::OrdinalIgnoreCase)
    $exportEligibleCount = 0
    foreach ($asset in @($inventory.assets)) {
        $assetPath = ([string]$asset.path).Replace("\", "/")
        if (
            [string]::IsNullOrWhiteSpace($assetPath) -or
            $assetPath.Contains("..") -or
            [System.IO.Path]::IsPathRooted($assetPath)
        ) {
            throw "Content inventory contains an unsafe path: $assetPath"
        }

        if ([bool]$asset.exportEligible) {
            $exportEligibleCount++
        } else {
            [void]$blockedInventoryPaths.Add($assetPath)
        }
    }

    if ($exportEligibleCount -ne 0) {
        throw (
            "Content inventory exportEligible count is $exportEligibleCount; " +
            "pack approval and allowlist wiring are required before non-zero eligibility."
        )
    }

    foreach ($artifactPath in $ArtifactRelativePaths) {
        $normalizedArtifactPath = ([string]$artifactPath).Replace("\", "/")
        foreach ($blockedPath in $blockedInventoryPaths) {
            if (
                $normalizedArtifactPath -eq $blockedPath -or
                $normalizedArtifactPath.EndsWith(
                    "/" + $blockedPath,
                    [StringComparison]::OrdinalIgnoreCase)
            ) {
                throw "Artifact contains inventory asset that is not exportEligible: $blockedPath"
            }
        }
    }
}

function Assert-NativeArtifactExcludesAgentArenaPreview {
    param([Parameter(Mandatory)][string[]]$ArtifactRelativePaths)

    $prohibitedPreviewPatterns = @(
        "(^|/)VibeSnake\.Agent(?:Play|Viewer|Host)(?:[._/-]|$)",
        "(^|/)(?:integrations/)?vibesnake-agent-(?:plugin|knowledge)(?:/|$)",
        "(^|/)integrations/agent-interop-baseline\.json$",
        "(^|/)skills/play-vibesnake(?:/|$)",
        "(^|/)mcp\.json$"
    )

    foreach ($artifactPath in $ArtifactRelativePaths) {
        $normalized = ([string]$artifactPath).Replace("\", "/")
        foreach ($pattern in $prohibitedPreviewPatterns) {
            if ($normalized -match $pattern) {
                throw "Supported artifact contains Agent Arena preview content: $normalized"
            }
        }
    }
}

function Assert-NativeArtifactPayloadExcludesAgentArenaPreview {
    param(
        [Parameter(Mandatory)][byte[]]$Bytes,
        [Parameter(Mandatory)][string]$RelativePath
    )

    $text = [Text.Encoding]::UTF8.GetString($Bytes)
    $previewNeedles = @(
        "VibeSnake.AgentPlay",
        "VibeSnake.AgentViewer",
        "VibeSnake.AgentHost",
        "--agent-watch-",
        "vibesnake-agent-plugin",
        "vibesnake-agent-knowledge",
        "integrations/agent-interop-baseline.json"
    )
    foreach ($needle in $previewNeedles) {
        if ($text.Contains($needle, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Supported artifact payload contains Agent Arena preview content in $RelativePath."
        }
    }
}
