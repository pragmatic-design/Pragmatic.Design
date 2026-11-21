#!/usr/bin/env pwsh
# Native AOT smoke for the in-memory message bus: publishes the messaging sample with PublishAot=true, runs the native binary, and
# asserts that a message dispatched untyped reached its generated [MessageHandler].
# Requires the platform native toolchain (on Windows: a VS C++ workload / Developer environment).
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

$proj = Join-Path $PSScriptRoot 'Pragmatic.Aot.Messaging/Pragmatic.Aot.Messaging.csproj'
$rid = if ($IsWindows -or $env:OS -eq 'Windows_NT') { 'win-x64' } elseif ($IsMacOS) { 'osx-x64' } else { 'linux-x64' }

Write-Host "Publishing $proj for $rid (Native AOT)..."
# The warnings below are read from the publish's output, and an up-to-date publish prints none: the sample's own Release output goes
# first, so ILC runs every time and the count is a measurement, not an absence (publish-and-smoke-host.ps1).
foreach ($dir in @('obj/Release', 'bin/Release')) {
    $path = Join-Path $PSScriptRoot "Pragmatic.Aot.Messaging/$dir"
    if (Test-Path $path) { Remove-Item -Recurse -Force $path }
}
# Keeps the referenced projects' committed lock files untouched (publish-and-smoke.ps1).
$publish = dotnet publish $proj -c Release -r $rid -p:PragmaticAotSmokeRestore=true 2>&1
$published = $LASTEXITCODE
$publish | ForEach-Object { Write-Host $_ }
if ($published -ne 0) { throw "AOT publish failed" }
if (-not ($publish | Where-Object { "$_" -match 'Generating native code' })) { throw "ILC did not run: the warning count would measure nothing" }

# The trim and AOT warnings of the application's publish, each counted once. Zero: the bus's untyped dispatch once fell back to C#'s
# dynamic binder unguarded, which compiled the binder in with six IL3050 warnings (#869).
$budget = 0
$warnings = @($publish | Where-Object { "$_" -match 'warning IL(2026|3050)' -and "$_" -match 'Pragmatic\.Aot\.Messaging\.csproj' } | Sort-Object -Unique)
Write-Host "IL2026/IL3050 warnings in the publish: $($warnings.Count) (budget $budget)"
if ($warnings.Count -gt $budget) {
    $warnings | ForEach-Object { Write-Host $_ }
    throw "AOT messaging smoke FAILED: $($warnings.Count) trim/AOT warnings, over the budget of $budget"
}

$exeName = if ($rid -like 'win-*') { 'Pragmatic.Aot.Messaging.exe' } else { 'Pragmatic.Aot.Messaging' }
$exe = Get-ChildItem -Recurse -Path (Join-Path $PSScriptRoot 'Pragmatic.Aot.Messaging/bin') -Filter $exeName |
    Where-Object { $_.FullName -match 'publish' -and $_.FullName -match "[\\/]$smokeTarget[\\/]" } | Select-Object -First 1
if (-not $exe) { throw "Native binary not found" }

Write-Host "Running $($exe.FullName)..."
# Joined: on an array of lines -notmatch returns the lines that do not match, which is truthy whenever there is more than one.
$output = (& $exe.FullName 2>&1) -join "`n"
Write-Host $output
if ($LASTEXITCODE -ne 0 -or $output -notmatch 'AOT-MESSAGING-OK') {
    throw "AOT messaging smoke FAILED (exit $LASTEXITCODE): $output"
}
Write-Host "AOT messaging smoke PASSED."
