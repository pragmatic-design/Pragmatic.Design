#!/usr/bin/env pwsh
# Native AOT smoke for the managed codecs (Pragmatic.Imaging.Codecs): publishes the sample with
# PublishAot=true, runs the native binary, and asserts the upload path works with the AVX2 paths compiled in.
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

$proj = Join-Path $PSScriptRoot 'Pragmatic.Aot.Codecs/Pragmatic.Aot.Codecs.csproj'
$rid = if ($IsWindows -or $env:OS -eq 'Windows_NT') { 'win-x64' } elseif ($IsMacOS) { 'osx-x64' } else { 'linux-x64' }

Write-Host "Publishing $proj for $rid (Native AOT)..."
# IL2026/IL3050 as errors: the codecs need no unreferenced or dynamic code, and a warning here would be the
# first sign they started to.
# Keeps the referenced projects' committed lock files untouched (publish-and-smoke.ps1).
dotnet publish $proj -c Release -r $rid -warnaserror:IL2026,IL3050 -p:PragmaticAotSmokeRestore=true
if ($LASTEXITCODE -ne 0) { throw "AOT publish failed" }

$exeName = if ($rid -like 'win-*') { 'Pragmatic.Aot.Codecs.exe' } else { 'Pragmatic.Aot.Codecs' }
$exe = Get-ChildItem -Recurse -Path (Join-Path $PSScriptRoot 'Pragmatic.Aot.Codecs/bin') -Filter $exeName |
    Where-Object { $_.FullName -match 'publish' -and $_.FullName -match "[\\/]$smokeTarget[\\/]" } | Select-Object -First 1
if (-not $exe) { throw "Native binary not found" }

# Pragmatic.Imaging's project reference copies its Rust library into the output. The smoke takes it out
# before running: what it then exercises of the module (PNG, JPEG, the operations, QR codes) must not need it.
Get-ChildItem -Path $exe.DirectoryName -Filter '*Pragmatic.Imaging.Native*' | Remove-Item
Get-ChildItem -Path $exe.DirectoryName -Filter '*pragmatic_imaging_native*' | Remove-Item

Write-Host "Running $($exe.FullName)..."
# Joined: on an array of lines -notmatch returns the lines that do not match, which is truthy whenever there is more than one.
$output = (& $exe.FullName 2>&1) -join "`n"
Write-Host $output
if ($LASTEXITCODE -ne 0 -or $output -notmatch 'AOT-CODECS-OK') {
    throw "AOT codecs smoke FAILED (exit $LASTEXITCODE): $output"
}
Write-Host "AOT codecs smoke PASSED."
