#!/usr/bin/env pwsh
# W3 end-to-end: publishes the sample whose JsonSerializerContext is EMITTED by the Pragmatic SG (opt-in
# via [assembly: PragmaticGenerateJsonContext]), then runs the native binary. Proves the generated
# context serializes AOT-safely with the reflection fallback disabled. Same native-toolchain requirements
# as publish-and-smoke.ps1 (Windows: VS C++ workload / Developer environment).
$ErrorActionPreference = 'Stop'

# The ILC link step shells out to vswhere to find the MSVC linker, and vswhere ships with the Visual
# Studio installer — which is not on PATH by default. Without this a plain shell fails at the very
# last step with "vswhere.exe is not recognized", which reads like a broken sample rather than a
# missing directory.
if ($IsWindows -or $env:OS -eq 'Windows_NT') {
    if (-not (Get-Command vswhere -ErrorAction SilentlyContinue)) {
        $installer = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer'
        if (Test-Path (Join-Path $installer 'vswhere.exe')) { $env:PATH = "$installer;$env:PATH" }
    }
}

$proj = Join-Path $PSScriptRoot 'Pragmatic.Aot.GeneratedContext/Pragmatic.Aot.GeneratedContext.csproj'
$rid = if ($IsWindows -or $env:OS -eq 'Windows_NT') { 'win-x64' } elseif ($IsMacOS) { 'osx-x64' } else { 'linux-x64' }

Write-Host "Publishing $proj for $rid (Native AOT, SG-generated context)..."
dotnet publish $proj -c Release -r $rid
if ($LASTEXITCODE -ne 0) { throw "AOT publish failed" }

$exeName = if ($rid -like 'win-*') { 'Pragmatic.Aot.GeneratedContext.exe' } else { 'Pragmatic.Aot.GeneratedContext' }
$exe = Get-ChildItem -Recurse -Path (Join-Path $PSScriptRoot 'Pragmatic.Aot.GeneratedContext/bin') -Filter $exeName |
    Where-Object { $_.FullName -match 'publish' } | Select-Object -First 1
if (-not $exe) { throw "Native binary not found" }

Write-Host "Running $($exe.FullName)..."
$output = & $exe.FullName
Write-Host $output
if ($LASTEXITCODE -ne 0 -or $output -notmatch 'AOT-GEN-OK') {
    throw "AOT generated-context smoke FAILED (exit $LASTEXITCODE): $output"
}
Write-Host "AOT generated-context smoke PASSED."
