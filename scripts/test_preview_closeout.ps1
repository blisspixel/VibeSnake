# Isolated command fixtures prove orchestration without builds, commits, or edits
# to the real checkout. Invoke directly with PowerShell 7.
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$temporaryBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$fixtureRoot = Join-Path $temporaryBase ("vibesnake-preview-closeout-{0}" -f [Guid]::NewGuid())
$originalPath = $env:PATH
$fixtureVariables = @("VIBESNAKE_CLOSEOUT_LOG", "VIBESNAKE_CLOSEOUT_FAILURE", "VIBESNAKE_CLOSEOUT_STAGED")
$previousVariables = @{}
foreach ($name in $fixtureVariables) { $previousVariables[$name] = [Environment]::GetEnvironmentVariable($name, "Process") }
try {
    $scriptsRoot = Join-Path $fixtureRoot "scripts"
    $binRoot = Join-Path $fixtureRoot "commands"
    New-Item -ItemType Directory -Path $scriptsRoot, $binRoot | Out-Null
    $helper = Join-Path $scriptsRoot "close_agent_preview.ps1"
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot "close_agent_preview.ps1") -Destination $helper
    $envFile = Join-Path $fixtureRoot ".envrc"
    [IO.File]::WriteAllText($envFile, "preserve local configuration")
    $junkFile = Join-Path $fixtureRoot "_aa07_runner.cmd"
    [IO.File]::WriteAllText($junkFile, "preserve unrelated file")
    $runner = @'
param()
$command = [IO.Path]::GetFileNameWithoutExtension($MyInvocation.MyCommand.Path)
$commandArguments = @($args)
$entry = @{ command = $command; arguments = $commandArguments; author = $env:GIT_AUTHOR_NAME; authorEmail = $env:GIT_AUTHOR_EMAIL; committer = $env:GIT_COMMITTER_NAME; committerEmail = $env:GIT_COMMITTER_EMAIL }
[IO.File]::AppendAllText($env:VIBESNAKE_CLOSEOUT_LOG, ($entry | ConvertTo-Json -Compress -Depth 4) + "`n")
if ($commandArguments -contains $env:VIBESNAKE_CLOSEOUT_FAILURE) { exit 17 }
if ($command -eq "git" -and $commandArguments -contains "diff") {
    if ($env:VIBESNAKE_CLOSEOUT_FAILURE -eq "diff-error") { exit 19 }
    if ($env:VIBESNAKE_CLOSEOUT_STAGED -eq "yes") { exit 1 }
}
exit 0
'@
    [IO.File]::WriteAllText((Join-Path $binRoot "dotnet.ps1"), $runner)
    [IO.File]::WriteAllText((Join-Path $binRoot "git.ps1"), $runner)
    [IO.File]::WriteAllText((Join-Path $scriptsRoot "test_native.ps1"), @'
[IO.File]::AppendAllText($env:VIBESNAKE_CLOSEOUT_LOG, '{"command":"full","arguments":[]}' + "`n")
if ($env:VIBESNAKE_CLOSEOUT_FAILURE -eq "full") { exit 23 }
exit 0
'@)
    $shell = (Get-Process -Id $PID).Path
    $env:PATH = "$binRoot$([IO.Path]::PathSeparator)$originalPath"
    foreach ($command in @("dotnet", "git")) {
        if ((Get-Command $command).Source -ne (Join-Path $binRoot "$command.ps1")) {
            throw "Fixture command resolution must precede the real $command executable."
        }
    }
    $env:VIBESNAKE_CLOSEOUT_LOG = Join-Path $fixtureRoot "commands.jsonl"
    function Invoke-CloseoutFixture {
        param([string[]]$HelperArguments = @(), [string]$Failure = "", [bool]$Staged = $false)
        [IO.File]::WriteAllText($env:VIBESNAKE_CLOSEOUT_LOG, "")
        $env:VIBESNAKE_CLOSEOUT_FAILURE = $Failure
        $env:VIBESNAKE_CLOSEOUT_STAGED = if ($Staged) { "yes" } else { "no" }
        $output = & $shell -NoProfile -File $helper @HelperArguments 2>&1 | Out-String
        $code = $LASTEXITCODE
        $entries = @([IO.File]::ReadAllLines($env:VIBESNAKE_CLOSEOUT_LOG) | ForEach-Object { $_ | ConvertFrom-Json })
        return @{ code = $code; output = $output; entries = $entries }
    }
    $normal = Invoke-CloseoutFixture
    if ($normal.code -ne 0 -or $normal.entries.Count -ne 5) { throw "Default close-out failed: $($normal.output)" }
    $routes = @($normal.entries | Select-Object -First 4 | ForEach-Object { $_.arguments[-2] }) -join ","
    if ($routes -ne "interop-write,knowledge-write,interop,docs") { throw "Close-out route order changed: $routes" }
    if ($normal.entries[4].arguments -notcontains "FullyQualifiedName~AgentPassport|FullyQualifiedName~AgentExhibitionStory|FullyQualifiedName~AgentQualification|FullyQualifiedName~AgentHostTests") { throw "Focused test filter was not preserved." }
    $failed = Invoke-CloseoutFixture -Failure "knowledge-write" -HelperArguments @("--commit") -Staged $true
    if ($failed.code -ne 17 -or $failed.entries.Count -ne 2) { throw "Close-out did not stop and propagate a failed prerequisite." }
    $unknown = Invoke-CloseoutFixture -HelperArguments @("--typo")
    if ($unknown.code -ne 2 -or $unknown.entries.Count -ne 0 -or $unknown.output -notmatch "Unknown argument") { throw "Unknown argument was not rejected before work." }
    $full = Invoke-CloseoutFixture -HelperArguments @("--full")
    if ($full.code -ne 0 -or $full.entries.Count -ne 6 -or $full.entries[5].command -ne "full") { throw "Full close-out did not run native quality." }
    $fullFailed = Invoke-CloseoutFixture -HelperArguments @("--full", "--commit") -Failure "full" -Staged $true
    if ($fullFailed.code -ne 23 -or $fullFailed.entries.Count -ne 6) { throw "Full-quality failure was not propagated before commit." }
    $empty = Invoke-CloseoutFixture -HelperArguments @("--commit")
    if ($empty.code -ne 0 -or $empty.entries.Count -ne 6 -or $empty.output -notmatch "No staged changes") { throw "Empty index should not create a commit." }
    $committed = Invoke-CloseoutFixture -HelperArguments @("--commit") -Staged $true
    if ($committed.code -ne 0 -or $committed.entries.Count -ne 7) { throw "Staged commit flow failed: $($committed.output)" }
    $commitEntry = $committed.entries[6]
    if ($commitEntry.arguments -notcontains "commit" -or $commitEntry.author -ne "Nick Seal" -or $commitEntry.committer -ne "Nick Seal" -or
        $commitEntry.authorEmail -ne "32712898+blisspixel@users.noreply.github.com" -or $commitEntry.committerEmail -ne $commitEntry.authorEmail) { throw "Commit identity drifted." }
    foreach ($entry in $committed.entries) {
        if ($entry.arguments -contains "add" -or $entry.arguments -contains "push") { throw "Close-out staged or pushed changes." }
    }
    $commitFailed = Invoke-CloseoutFixture -HelperArguments @("--commit") -Failure "commit" -Staged $true
    if ($commitFailed.code -ne 17) { throw "Commit failure was not propagated." }
    $indexFailed = Invoke-CloseoutFixture -HelperArguments @("--commit") -Failure "diff-error" -Staged $true
    if ($indexFailed.code -ne 19 -or $indexFailed.entries.Count -ne 6) { throw "Index failure was not propagated." }
    if ([IO.File]::ReadAllText($envFile) -ne "preserve local configuration" -or [IO.File]::ReadAllText($junkFile) -ne "preserve unrelated file") { throw "Close-out modified unrelated files." }
    $wrapper = Get-Content -LiteralPath (Join-Path (Split-Path -Parent $PSScriptRoot) "close-agent-preview.cmd") -Raw
    if ($wrapper -match "python|close_agent_preview\.py" -or $wrapper -notmatch "close_agent_preview\.ps1.*%\*") { throw "Windows wrapper must preserve native argument forwarding." }
    if ($IsWindows) {
        $fixtureWrapper = Join-Path $fixtureRoot "close-agent-preview.cmd"
        [IO.File]::WriteAllText($fixtureWrapper, $wrapper)
        [IO.File]::WriteAllText($env:VIBESNAKE_CLOSEOUT_LOG, "")
        $env:VIBESNAKE_CLOSEOUT_FAILURE = ""
        $wrapperOutput = & $env:ComSpec /d /c $fixtureWrapper --full 2>&1 | Out-String
        $wrapperCode = $LASTEXITCODE
        $wrapperEntries = @([IO.File]::ReadAllLines($env:VIBESNAKE_CLOSEOUT_LOG) | ForEach-Object { $_ | ConvertFrom-Json })
        if ($wrapperCode -ne 0 -or $wrapperEntries.Count -ne 6 -or $wrapperEntries[5].command -ne "full") { throw "Windows wrapper failed argument forwarding: $wrapperOutput" }
    }
    Write-Output "VIBESNAKE_PREVIEW_CLOSEOUT_OK cases=9 wrapper=$IsWindows"
} finally {
    $env:PATH = $originalPath
    foreach ($name in $fixtureVariables) { [Environment]::SetEnvironmentVariable($name, $previousVariables[$name], "Process") }
    $resolvedFixture = [IO.Path]::GetFullPath($fixtureRoot)
    $safePrefix = $temporaryBase.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if ($resolvedFixture.StartsWith($safePrefix, [StringComparison]::OrdinalIgnoreCase)) {
        Remove-Item -LiteralPath $resolvedFixture -Recurse -Force -ErrorAction SilentlyContinue
    }
}
