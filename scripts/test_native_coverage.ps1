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
        "RepositoryChecks",
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

function Invoke-NativeCoverageTestProcess {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Executable,

        [Parameter(Mandatory = $true)]
        [string[]]$CommandArguments,

        [Parameter(Mandatory = $true)]
        [ref]$Result
    )

    $testsPassed = $false
    $coverletTruncated = $false
    $instrumentedModuleReportedZeroCoverage = $false
    & $Executable @CommandArguments 2>&1 | ForEach-Object {
        $testLine = "$_"
        Write-Output $testLine
        if ($testLine -match 'Passed!\s+-\s+Failed:\s+0,') {
            $testsPassed = $true
        }
        if ($testLine -match 'Unable to read beyond the end of the stream') {
            $coverletTruncated = $true
        }
        if ($testLine -match '^\|\s+(?:RepositoryChecks|ValidateCreatorContent|VibeSnake\.(?:AgentHost|AgentPlay|AgentViewer|Persistence|Rules))\s+\|\s+0(?:\.0+)?%\s+\|\s+0(?:\.0+)?%\s+\|') {
            $instrumentedModuleReportedZeroCoverage = $true
        }
    }
    $testExit = $LASTEXITCODE
    $Result.Value = [pscustomobject]@{
        ExitCode = $testExit
        TestsPassed = $testsPassed
        CoverletTruncated = $coverletTruncated
        InstrumentedModuleReportedZeroCoverage = $instrumentedModuleReportedZeroCoverage
    }
}

function Get-NativeCoverageAttemptAction {
    param(
        [Parameter(Mandatory = $true)]
        [pscustomobject]$Result
    )

    if ($Result.ExitCode -eq 0) {
        return "validate"
    }
    if ($Result.TestsPassed -and (
        $Result.CoverletTruncated -or
        $Result.InstrumentedModuleReportedZeroCoverage)) {
        return "retry"
    }

    return "fail"
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

if ($MyInvocation.InvocationName -eq ".") {
    return
}

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
        $testResult = $null
        Invoke-NativeCoverageTestProcess `
            -Executable "dotnet" `
            -CommandArguments $testArguments `
            -Result ([ref]$testResult)
        $attemptAction = Get-NativeCoverageAttemptAction -Result $testResult
        if ($attemptAction -eq "fail") {
            throw "Native tests failed; a coverage-report retry cannot hide a test failure."
        }

        try {
            if ($attemptAction -eq "retry") {
                if ($testResult.CoverletTruncated) {
                    throw "Coverlet truncated a hit stream after a green test run."
                }

                throw "Coverlet reported zero hits for an instrumented module after a green test run."
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
