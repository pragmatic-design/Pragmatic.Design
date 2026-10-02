#!/usr/bin/env pwsh
# The HTTP path under Native AOT: publishes a one-endpoint app whose endpoint is EMITTED by the
# Pragmatic SG, runs the native binary, and has it call itself.
#
# It failed when it was written, and that was the point: it was the standing proof that "Full AOT"
# was not true for Pragmatic's HTTP surface. It passes now — see README.md for what it caught and
# what changed. Do not weaken its assertions; it is the only thing that answers the question.
#
# Needs the native toolchain (Windows: a Visual Studio C++ workload).
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

$proj = Join-Path $PSScriptRoot 'Pragmatic.Aot.WebEndpoint/Pragmatic.Aot.WebEndpoint.csproj'
$rid = if ($IsWindows -or $env:OS -eq 'Windows_NT') { 'win-x64' } elseif ($IsMacOS) { 'osx-x64' } else { 'linux-x64' }

Write-Host "Publishing $proj for $rid (Native AOT, SG-generated endpoint)..."
dotnet publish $proj -c Release -r $rid
if ($LASTEXITCODE -ne 0) { throw "AOT publish failed" }

$exeName = if ($rid -like 'win-*') { 'Pragmatic.Aot.WebEndpoint.exe' } else { 'Pragmatic.Aot.WebEndpoint' }
$exe = Get-ChildItem -Recurse -Path (Join-Path $PSScriptRoot 'Pragmatic.Aot.WebEndpoint/bin') -Filter $exeName |
    Where-Object { $_.FullName -match 'publish' } | Select-Object -First 1
if (-not $exe) { throw "Native binary not found" }

# Both configurations, because they fail differently and the difference is the diagnosis: strict
# (reflection fallback off) throws, lenient returns 200 with a body that is silently wrong.
foreach ($mode in @('strict', 'lenient')) {
    $env:PRAGMATIC_AOT_LENIENT = if ($mode -eq 'lenient') { '1' } else { '0' }
    Write-Host "Running $($exe.FullName) [$mode]..."
    $output = & $exe.FullName 2>&1 | Out-String
    Write-Host $output
    if ($LASTEXITCODE -ne 0 -or $output -notmatch 'AOT-WEB-OK') {
        throw "AOT web-endpoint smoke FAILED in $mode mode (exit $LASTEXITCODE). This is the known state; see README.md."
    }
}
Remove-Item Env:\PRAGMATIC_AOT_LENIENT -ErrorAction SilentlyContinue
Write-Host "AOT web-endpoint smoke PASSED."
