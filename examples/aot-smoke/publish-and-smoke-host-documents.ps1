#!/usr/bin/env pwsh
# Native AOT smoke for a composed host on the documents engine: publishes Pragmatic.Aot.HostDocuments with PublishAot=true and every
# warning an error, starts MongoDB 9 in Docker with a root user, runs the native binary against it, and asserts that every check passed.
# The binary is the generated host entry point (PragmaticApp.RunAsync): it applies the boundary's documents schema at start, serves the
# Notes module's endpoints, and calls them itself as an authenticated caller, an anonymous one and one without the permission, as the Sql
# host's smoke does (publish-and-smoke-host.ps1). Its exit code is the number of checks that failed. The container is removed whatever
# happens. Requires the platform native toolchain and Docker: without either it fails, it does not pass.
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

$proj = Join-Path $PSScriptRoot 'Pragmatic.Aot.HostDocuments/Pragmatic.Aot.HostDocuments.csproj'
$rid = if ($IsWindows -or $env:OS -eq 'Windows_NT') { 'win-x64' } elseif ($IsMacOS) { 'osx-x64' } else { 'linux-x64' }

Write-Host "Publishing $proj for $rid (Native AOT)..."
# An up-to-date publish compiles nothing and prints no warning: the host's own Release output goes first, so the count is a measurement
# (publish-and-smoke-host.ps1).
foreach ($dir in @('obj/Release', 'bin/Release')) {
    $path = Join-Path $PSScriptRoot "Pragmatic.Aot.HostDocuments/$dir"
    if (Test-Path $path) { Remove-Item -Recurse -Force $path }
}
# Keeps the referenced projects' committed lock files untouched (publish-and-smoke.ps1).
$publish = dotnet publish $proj -c Release -r $rid -p:PragmaticAotSmokeRestore=true 2>&1
$published = $LASTEXITCODE
$publish | ForEach-Object { Write-Host $_ }
if ($published -ne 0) { throw "AOT publish failed" }
if (-not ($publish | Where-Object { "$_" -match 'Generating native code' })) { throw "ILC did not run: the warning count would measure nothing" }

# The trim and AOT warnings this host's publish raises, each counted once. A budget that can only fall, as the Sql host's: 0.
$budget = 0
$warnings = @($publish | Where-Object { "$_" -match 'warning IL(2026|3050)' -and "$_" -match 'Pragmatic\.Aot\.HostDocuments\.csproj' } | Sort-Object -Unique)
Write-Host "IL2026/IL3050 warnings in the publish: $($warnings.Count) (budget $budget)"
if ($warnings.Count -gt $budget) {
    $warnings | ForEach-Object { Write-Host $_ }
    throw "AOT composed-host smoke on the documents engine FAILED: $($warnings.Count) trim/AOT warnings, over the budget of $budget"
}
if ($warnings.Count -lt $budget) {
    throw "AOT composed-host smoke on the documents engine: $($warnings.Count) trim/AOT warnings, under the budget of $budget. Lower the budget to $($warnings.Count)."
}

$exeName = if ($rid -like 'win-*') { 'Pragmatic.Aot.HostDocuments.exe' } else { 'Pragmatic.Aot.HostDocuments' }
$exe = Get-ChildItem -Recurse -Path (Join-Path $PSScriptRoot 'Pragmatic.Aot.HostDocuments/bin') -Filter $exeName |
    Where-Object { $_.FullName -match 'publish' -and $_.FullName -match "[\\/]$smokeTarget[\\/]" } | Select-Object -First 1
if (-not $exe) { throw "Native binary not found" }

$user = 'root'
$password = 'aot-Pa55'
$name = 'pragmatic-aot-host-mongo-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
docker run -d --name $name -p 127.0.0.1::27017 -e "MONGO_INITDB_ROOT_USERNAME=$user" -e "MONGO_INITDB_ROOT_PASSWORD=$password" mongo:9 | Out-Null
if ($LASTEXITCODE -ne 0) { throw "MongoDB did not start in Docker (is Docker running?)" }
try {
    # Ready at the second "Waiting for connections", as publish-and-smoke-documents.ps1 waits.
    $deadline = (Get-Date).AddMinutes(2)
    while (([regex]::Matches((docker logs $name 2>&1 | Out-String), 'Waiting for connections')).Count -lt 2) {
        if ((Get-Date) -gt $deadline) { throw "MongoDB was not ready within 2 minutes" }
        Start-Sleep -Milliseconds 500
    }

    $port = ((docker port $name 27017) | Select-Object -First 1).Split(':')[-1]
    $env:PRAGMATIC_AOT_MONGO = "mongodb://${user}:${password}@127.0.0.1:${port}/?authSource=admin"

    Write-Host "Running $($exe.FullName)..."
    $output = & $exe.FullName 2>&1
    $code = $LASTEXITCODE
    # The host logs in Development; the checks and the verdict are the lines that say what happened.
    Write-Host (($output | Where-Object { "$_" -match '^(ok  |FAIL)|AOT-HOST-OK|failed$|Unhandled|Exception|schema' }) -join "`n")
    if ($code -ne 0 -or ($output -join "`n") -notmatch 'AOT-HOST-OK') {
        throw "AOT composed-host smoke on the documents engine FAILED (exit $code)"
    }
}
finally {
    docker rm -f $name | Out-Null
}
Write-Host "AOT composed-host smoke on the documents engine PASSED."
