#!/usr/bin/env pwsh
# Native AOT smoke for declared redaction: publishes the logging sample with PublishAot=true, runs the
# native binary, and asserts the [PersonalData] member came out masked rather than in clear or not at all.
# Requires the platform native toolchain (on Windows: a VS C++ workload / Developer environment).
$ErrorActionPreference = 'Stop'

# The ILC link step shells out to vswhere to find the MSVC linker; see publish-and-smoke.ps1.
if ($IsWindows -or $env:OS -eq 'Windows_NT') {
    if (-not (Get-Command vswhere -ErrorAction SilentlyContinue)) {
        $installer = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer'
        if (Test-Path (Join-Path $installer 'vswhere.exe')) { $env:PATH = "$installer;$env:PATH" }
    }
}

$proj = Join-Path $PSScriptRoot 'Pragmatic.Aot.Logging/Pragmatic.Aot.Logging.csproj'
$rid = if ($IsWindows -or $env:OS -eq 'Windows_NT') { 'win-x64' } elseif ($IsMacOS) { 'osx-x64' } else { 'linux-x64' }

Write-Host "Publishing $proj for $rid (Native AOT)..."
dotnet publish $proj -c Release -r $rid
if ($LASTEXITCODE -ne 0) { throw "AOT publish failed" }

$exeName = if ($rid -like 'win-*') { 'Pragmatic.Aot.Logging.exe' } else { 'Pragmatic.Aot.Logging' }
$exe = Get-ChildItem -Recurse -Path (Join-Path $PSScriptRoot 'Pragmatic.Aot.Logging/bin') -Filter $exeName |
    Where-Object { $_.FullName -match 'publish' } | Select-Object -First 1
if (-not $exe) { throw "Native binary not found" }

Write-Host "Running $($exe.FullName)..."
$output = & $exe.FullName 2>&1
Write-Host $output
if ($LASTEXITCODE -ne 0 -or $output -notmatch 'AOT-LOGGING-OK') {
    throw "AOT logging smoke FAILED (exit $LASTEXITCODE): $output"
}
Write-Host "AOT logging smoke PASSED."
