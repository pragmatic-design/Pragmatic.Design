#!/usr/bin/env pwsh
# Native AOT smoke for the Sql engine under Pragmatic's contracts: publishes Pragmatic.Aot.SqlEngine with PublishAot=true and every
# warning an error, runs the native binary on SQLite in memory, and asserts that every check passed (the binary's exit code is the
# number that failed). Requires the platform native toolchain (on Windows: a VS C++ workload / Developer environment).
$ErrorActionPreference = 'Stop'

# The ILC link step shells out to vswhere to find the MSVC linker; see publish-and-smoke.ps1.
if ($IsWindows -or $env:OS -eq 'Windows_NT') {
    if (-not (Get-Command vswhere -ErrorAction SilentlyContinue)) {
        $installer = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer'
        if (Test-Path (Join-Path $installer 'vswhere.exe')) { $env:PATH = "$installer;$env:PATH" }
    }
}

$proj = Join-Path $PSScriptRoot 'Pragmatic.Aot.SqlEngine/Pragmatic.Aot.SqlEngine.csproj'
$rid = if ($IsWindows -or $env:OS -eq 'Windows_NT') { 'win-x64' } elseif ($IsMacOS) { 'osx-x64' } else { 'linux-x64' }

Write-Host "Publishing $proj for $rid (Native AOT)..."
# The project treats every warning as an error, trim and AOT ones included.
# Keeps the referenced projects' committed lock files untouched (publish-and-smoke.ps1).
dotnet publish $proj -c Release -r $rid -p:PragmaticAotSmokeRestore=true
if ($LASTEXITCODE -ne 0) { throw "AOT publish failed" }

$exeName = if ($rid -like 'win-*') { 'Pragmatic.Aot.SqlEngine.exe' } else { 'Pragmatic.Aot.SqlEngine' }
$exe = Get-ChildItem -Recurse -Path (Join-Path $PSScriptRoot 'Pragmatic.Aot.SqlEngine/bin') -Filter $exeName |
    Where-Object { $_.FullName -match 'publish' } | Select-Object -First 1
if (-not $exe) { throw "Native binary not found" }

Write-Host "Running $($exe.FullName)..."
$output = & $exe.FullName 2>&1
Write-Host ($output -join "`n")
if ($LASTEXITCODE -ne 0 -or ($output -join "`n") -notmatch 'all checks passed') {
    throw "AOT Sql engine smoke FAILED (exit $LASTEXITCODE)"
}
Write-Host "AOT Sql engine smoke PASSED."
