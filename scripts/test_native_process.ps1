# Isolated process regressions, without a game build or editor installation.
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "native_process_policy.ps1")
$temporaryBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
$fixtureRoot = Join-Path $temporaryBase ("vibesnake-native-process-{0}" -f [Guid]::NewGuid())
$shell = (Get-Process -Id $PID).Path
$ownedChildIds = [Collections.Generic.List[int]]::new()
try {
    New-Item -ItemType Directory -Path $fixtureRoot | Out-Null
    $fixture = Join-Path $fixtureRoot "fixture with spaces.ps1"
    [IO.File]::WriteAllText($fixture, @'
param([string]$Mode, [string]$Root)
switch ($Mode) {
    "arguments" {
        [Console]::WriteLine(($args | ConvertTo-Json -Compress))
        [Console]::Error.WriteLine("stderr-preserved")
        exit 7
    }
    "both-streams" {
        for ($i = 0; $i -lt 4096; $i++) {
            [Console]::Out.WriteLine("output-" + $i)
            [Console]::Error.WriteLine("error-" + $i)
        }
        exit 0
    }
    "overflow" {
        [Console]::Write([string]::new('x', 262145))
        [Console]::Error.WriteLine("ERROR: after the output limit")
        exit 0
    }
    { $_ -in "tree", "inherited-pipe" } {
        $start = [Diagnostics.ProcessStartInfo]::new()
        $start.FileName = (Get-Process -Id $PID).Path
        $start.UseShellExecute = $false
        $start.CreateNoWindow = $true
        foreach ($argument in @("-NoLogo", "-NoProfile", "-NonInteractive", "-Command", "[Threading.Thread]::Sleep(120000)")) {
            $start.ArgumentList.Add($argument)
        }
        $child = [Diagnostics.Process]::Start($start)
        [IO.File]::WriteAllText((Join-Path $Root ($Mode + ".pid")), [string]$child.Id)
        $child.Dispose()
        [Console]::WriteLine("child-started")
        if ($Mode -eq "tree") { [Threading.Thread]::Sleep(120000) }
        exit 0
    }
}
'@, [Text.UTF8Encoding]::new($false))
    $values = @("space inside", 'quote"inside', 'literal$()&;|', "", "trailing\")
    $result = Invoke-BoundedNativeProcess -Executable $fixture -Arguments (@("arguments", $fixtureRoot) + $values) `
        -WorkingDirectory $fixtureRoot -TimeoutMilliseconds 60000 -Operation "argument fixture"
    $actual = @($result.StandardOutput.Trim() | ConvertFrom-Json)
    if ($result.ExitCode -ne 7 -or $result.StandardError.Trim() -cne "stderr-preserved" -or $actual.Count -ne $values.Count) {
        throw "Native process changed arguments, stderr, or nonzero exit."
    }
    for ($index = 0; $index -lt $values.Count; $index++) {
        if ($actual[$index] -cne $values[$index]) { throw "Native process changed argument $index." }
    }
    $result = Invoke-BoundedNativeProcess -Executable $fixture -Arguments @("both-streams", $fixtureRoot) `
        -WorkingDirectory $fixtureRoot -TimeoutMilliseconds 60000 -Operation "concurrent pipe fixture"
    if ($result.ExitCode -ne 0 -or $result.StandardOutput -notmatch "output-4095" -or $result.StandardError -notmatch "error-4095") {
        throw "Native process failed to drain both pipes concurrently."
    }
    $overflow = [VibeSnake.NativeProcessPolicy]::Run($shell, @("-NoProfile", "-File", $fixture, "overflow", $fixtureRoot), $fixtureRoot, 60000)
    if (-not $overflow.Truncated -or $overflow.ExitCode -ne -1 -or $overflow.StandardOutput.Length -ne 262144 -or
        $overflow.StandardError -notmatch "ERROR: after the output limit") { throw "Output limit did not fail closed." }
    try {
        Invoke-BoundedNativeProcess -Executable $fixture -Arguments @("overflow", $fixtureRoot) `
            -WorkingDirectory $fixtureRoot -TimeoutMilliseconds 60000 -Operation "output limit fixture" 6>$null | Out-Null
        throw "Output limit wrapper accepted incomplete diagnostics."
    } catch {
        if ($_.Exception.Message -notlike "output limit fixture failed bounded process qualification: OutputLimit.*") { throw }
    }
    $nativeExecutable = if ($IsWindows) { (Get-Command cmd.exe).Source } else { "/bin/sh" }
    $nativeArguments = if ($IsWindows) { @("/d", "/c", "ping -n 120 127.0.0.1 >nul") } else { @("-c", "sleep 120") }
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $timeout = [VibeSnake.NativeProcessPolicy]::Run($nativeExecutable, $nativeArguments, $fixtureRoot, 250)
    if (-not $timeout.TimedOut -or $timeout.ExitCode -ne -1 -or $watch.Elapsed.TotalSeconds -gt 12) {
        throw "Native process timeout or bounded termination failed."
    }
    foreach ($mode in @("tree", "inherited-pipe")) {
        $watch.Restart()
        $probe = [VibeSnake.NativeProcessPolicy]::Run($shell, @("-NoProfile", "-File", $fixture, $mode, $fixtureRoot), $fixtureRoot, 30000)
        $pidPath = Join-Path $fixtureRoot ($mode + ".pid")
        if (-not (Test-Path -LiteralPath $pidPath -PathType Leaf)) { throw "Descendant fixture did not start inside its generous startup budget." }
        $childId = [int][IO.File]::ReadAllText($pidPath)
        $ownedChildIds.Add($childId)
        if ($mode -eq "tree") {
            # Child termination can finish after its parent exits. Observe the
            # exact fixture child within the existing overall termination budget.
            $childAlive = $true
            while ($watch.Elapsed.TotalSeconds -lt 42) {
                $observedChild = Get-Process -Id $childId -ErrorAction SilentlyContinue
                if (-not $observedChild) { $childAlive = $false; break }
                try { $childAlive = -not $observedChild.HasExited } finally { $observedChild.Dispose() }
                if (-not $childAlive) { break }
                Start-Sleep -Milliseconds 50
            }
            if (-not $probe.TimedOut -or $probe.ExitCode -ne -1 -or $watch.Elapsed.TotalSeconds -gt 42 -or
                $childAlive) { throw "Live-parent process-tree timeout failed." }
        } else {
            if ($probe.TimedOut -or $probe.OutputComplete -or $probe.ExitCode -ne -1 -or $watch.Elapsed.TotalSeconds -gt 42) {
                throw "Inherited pipes did not fail closed inside their drain budget."
            }
        }
    }
    # Loading the helper twice in the same process must not redefine its type.
    . (Join-Path $PSScriptRoot "native_process_policy.ps1")
    Write-Output "VIBESNAKE_NATIVE_PROCESS_OK cases=8"
} finally {
    # Only child identifiers written by this fixture are eligible for cleanup.
    foreach ($childId in $ownedChildIds) {
        $child = Get-Process -Id $childId -ErrorAction SilentlyContinue
        if ($child) {
            try { $child.Kill($true); [void]$child.WaitForExit(5000) } finally { $child.Dispose() }
        }
    }
    $prefix = $temporaryBase.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    $comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    if (-not [IO.Path]::GetFullPath($fixtureRoot).StartsWith($prefix, $comparison)) { throw "Unsafe process fixture cleanup path." }
    if (Test-Path -LiteralPath $fixtureRoot) { Remove-Item -LiteralPath $fixtureRoot -Recurse -Force }
}
