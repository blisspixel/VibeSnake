[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repositoryRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$coveragePath = Join-Path $repositoryRoot "TestResults/native/coverage.cobertura.xml"

function Invoke-Dotnet {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$CommandArguments
    )

    & dotnet @CommandArguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet command failed: dotnet $($CommandArguments -join ' ')"
    }
}

function Assert-NativeCoverageReport {
    if (-not (Test-Path -LiteralPath $coveragePath -PathType Leaf)) {
        throw "Native coverage report was not produced."
    }

    [xml]$coverage = Get-Content -LiteralPath $coveragePath -Raw
    foreach ($requiredModule in @(
        "VibeSnake.Rules",
        "VibeSnake.Persistence",
        "VibeSnake.AgentPlay",
        "VibeSnake.AgentViewer",
        "VibeSnake.AgentHost")) {
        $module = @($coverage.coverage.packages.package | Where-Object {
            $_.name -eq $requiredModule
        })
        if ($module.Count -ne 1) {
            throw "Native coverage report must contain exactly one $requiredModule package."
        }

        $lineRate = [double]::Parse(
            $module[0].GetAttribute("line-rate"),
            [System.Globalization.CultureInfo]::InvariantCulture)
        $branchRate = [double]::Parse(
            $module[0].GetAttribute("branch-rate"),
            [System.Globalization.CultureInfo]::InvariantCulture)
        if ($lineRate -lt 0.9 -or $branchRate -lt 0.85) {
            throw "$requiredModule coverage is below the 90 percent line or 85 percent branch floor."
        }
    }
}

$testArguments = @(
    "test",
    "native/tests/VibeSnake.Rules.Tests/VibeSnake.Rules.Tests.csproj",
    "--configuration",
    "Release",
    "--no-build",
    "--no-restore",
    "-p:CollectCoverage=true",
    "-p:CoverletOutput=../../../TestResults/native/",
    "-p:CoverletOutputFormat=cobertura",
    "-p:Threshold=90%2c85",
    "-p:ThresholdType=line%2cbranch",
    "-p:ThresholdStat=minimum",
    "-p:ExcludeByFile=**/Properties/AssemblyInfo.cs"
)

Push-Location $repositoryRoot
try {
    Invoke-Dotnet -CommandArguments @(
        "build",
        "native/tests/VibeSnake.Rules.Tests/VibeSnake.Rules.Tests.csproj",
        "--configuration",
        "Release",
        "--no-restore"
    )
    $coverageAccepted = $false
    for ($attempt = 1; $attempt -le 2; $attempt++) {
        if (Test-Path -LiteralPath $coveragePath -PathType Leaf) {
            Remove-Item -LiteralPath $coveragePath -Force
        }
        $testLines = & dotnet @testArguments 2>&1
        $testExit = $LASTEXITCODE
        $testLines | ForEach-Object { Write-Output "$_" }
        $joined = ($testLines | ForEach-Object { "$_" }) -join [Environment]::NewLine
        $testsPassed = $joined -match 'Passed!\s+-\s+Failed:\s+0,'
        $coverletTruncated = $joined -match 'Unable to read beyond the end of the stream'
        if ($testExit -ne 0 -and -not ($testsPassed -and $coverletTruncated)) {
            throw "Native tests failed; a coverage-report retry cannot hide a test failure."
        }

        try {
            if ($testExit -ne 0) {
                throw "Coverlet truncated a hit stream after a green test run."
            }
            Assert-NativeCoverageReport
            $coverageAccepted = $true
            Write-Output "Native tests with coverage passed on attempt $attempt."
            break
        }
        catch {
            if ($attempt -ge 2) {
                throw
            }

            Write-Output "Native coverage report attempt $attempt failed; rebuilding and retrying once. $_"
            Invoke-Dotnet -CommandArguments @("build-server", "shutdown")
            Invoke-Dotnet -CommandArguments @(
                "build",
                "native/tests/VibeSnake.Rules.Tests/VibeSnake.Rules.Tests.csproj",
                "--configuration",
                "Release",
                "--no-restore"
            )
        }
    }
    if (-not $coverageAccepted) {
        throw "Native coverage report was not accepted."
    }
}
finally {
    Pop-Location
}
