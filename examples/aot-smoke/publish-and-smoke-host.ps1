#!/usr/bin/env pwsh
# Native AOT smoke for a composed host on the Sql engine: publishes Pragmatic.Aot.Host with PublishAot=true and every warning an error,
# runs the native binary on a SQLite file, and asserts that every check passed. The binary is the generated host entry point
# (PragmaticApp.RunAsync): it applies the schema at start, serves the Notes module's endpoints, and calls them itself as an authenticated
# caller, an anonymous one and one without the permission. Its exit code is the number of checks that failed. Requires the platform native
# toolchain (on Windows: a VS C++ workload / Developer environment).
$ErrorActionPreference = 'Stop'
# The target this run publishes on (#931): the gate sets PragmaticAotSmokeTarget for its net11.0 pass, MSBuild reads it as a property.
$smokeTarget = if ($env:PragmaticAotSmokeTarget) { $env:PragmaticAotSmokeTarget } else { 'net10.0' }

# The ILC link step shells out to vswhere to find the MSVC linker; see publish-and-smoke.ps1.
if ($IsWindows -or $env:OS -eq 'Windows_NT') {
    if (-not (Get-Command vswhere -ErrorAction SilentlyContinue)) {
        $installer = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer'
        if (Test-Path (Join-Path $installer 'vswhere.exe')) { $env:PATH = "$installer;$env:PATH" }
    }
}

$proj = Join-Path $PSScriptRoot 'Pragmatic.Aot.Host/Pragmatic.Aot.Host.csproj'
$rid = if ($IsWindows -or $env:OS -eq 'Windows_NT') { 'win-x64' } elseif ($IsMacOS) { 'osx-x64' } else { 'linux-x64' }

Write-Host "Publishing $proj for $rid (Native AOT)..."
# The warnings below are read from the publish's output, and an up-to-date publish compiles nothing and prints none: the host's own
# Release output goes first, so the compiler and ILC run every time and the count is a measurement, not an absence.
foreach ($dir in @('obj/Release', 'bin/Release')) {
    $path = Join-Path $PSScriptRoot "Pragmatic.Aot.Host/$dir"
    if (Test-Path $path) { Remove-Item -Recurse -Force $path }
}
# Keeps the referenced projects' committed lock files untouched (publish-and-smoke.ps1).
$publish = dotnet publish $proj -c Release -r $rid -p:PragmaticAotSmokeRestore=true 2>&1
$published = $LASTEXITCODE
$publish | ForEach-Object { Write-Host $_ }
if ($published -ne 0) { throw "AOT publish failed" }
if (-not ($publish | Where-Object { "$_" -match 'Generating native code' })) { throw "ILC did not run: the warning count would measure nothing" }

# The trim and AOT warnings this host's publish raises, each counted once (ILC repeats a warning per call site it reaches). Not zero yet,
# so a budget that can only fall, as the gate's other ratchets: a warning above it is a regression, one below it is a fix that lowers the
# number here. README.md ("One composed host") says where the warnings it once had came from, and the issue that removed each.
$budget = 0
$warnings = @($publish | Where-Object { "$_" -match 'warning IL(2026|3050)' -and "$_" -match 'Pragmatic\.Aot\.Host\.csproj' } | Sort-Object -Unique)
Write-Host "IL2026/IL3050 warnings in the publish: $($warnings.Count) (budget $budget)"
if ($warnings.Count -gt $budget) {
    $warnings | ForEach-Object { Write-Host $_ }
    throw "AOT composed-host smoke FAILED: $($warnings.Count) trim/AOT warnings, over the budget of $budget"
}
if ($warnings.Count -lt $budget) {
    throw "AOT composed-host smoke: $($warnings.Count) trim/AOT warnings, under the budget of $budget. Lower the budget to $($warnings.Count)."
}

$exeName = if ($rid -like 'win-*') { 'Pragmatic.Aot.Host.exe' } else { 'Pragmatic.Aot.Host' }
$exe = Get-ChildItem -Recurse -Path (Join-Path $PSScriptRoot 'Pragmatic.Aot.Host/bin') -Filter $exeName |
    Where-Object { $_.FullName -match 'publish' -and $_.FullName -match "[\\/]$smokeTarget[\\/]" } | Select-Object -First 1
if (-not $exe) { throw "Native binary not found" }

Write-Host "Running $($exe.FullName)..."
$output = & $exe.FullName 2>&1
$code = $LASTEXITCODE
# The host logs in Development; the checks and the verdict are the lines that say what happened.
Write-Host (($output | Where-Object { "$_" -match '^(ok  |FAIL)|AOT-HOST-OK|failed$|Unhandled|Exception' }) -join "`n")
if ($code -ne 0 -or ($output -join "`n") -notmatch 'AOT-HOST-OK') {
    throw "AOT composed-host smoke FAILED (exit $code)"
}
Write-Host "AOT composed-host smoke PASSED."
