#!/usr/bin/env pwsh
# Native AOT smoke for the documents engine's MongoDB driver: publishes Pragmatic.Aot.DocumentsMongo with PublishAot=true and every
# warning an error, starts MongoDB 8 in Docker with a root user, runs the native binary against it, and asserts MONGO-AOT-OK. The
# container is removed whatever happens. Requires the platform native toolchain and Docker: without either it fails, it does not pass.
$ErrorActionPreference = 'Stop'

# The ILC link step shells out to vswhere to find the MSVC linker; see publish-and-smoke.ps1.
if ($IsWindows -or $env:OS -eq 'Windows_NT') {
    if (-not (Get-Command vswhere -ErrorAction SilentlyContinue)) {
        $installer = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer'
        if (Test-Path (Join-Path $installer 'vswhere.exe')) { $env:PATH = "$installer;$env:PATH" }
    }
}

$proj = Join-Path $PSScriptRoot 'Pragmatic.Aot.DocumentsMongo/Pragmatic.Aot.DocumentsMongo.csproj'
$rid = if ($IsWindows -or $env:OS -eq 'Windows_NT') { 'win-x64' } elseif ($IsMacOS) { 'osx-x64' } else { 'linux-x64' }

Write-Host "Publishing $proj for $rid (Native AOT)..."
# Keeps the referenced projects' committed lock files untouched (publish-and-smoke.ps1).
dotnet publish $proj -c Release -r $rid -p:PragmaticAotSmokeRestore=true
if ($LASTEXITCODE -ne 0) { throw "AOT publish failed" }

$exeName = if ($rid -like 'win-*') { 'Pragmatic.Aot.DocumentsMongo.exe' } else { 'Pragmatic.Aot.DocumentsMongo' }
$exe = Get-ChildItem -Recurse -Path (Join-Path $PSScriptRoot 'Pragmatic.Aot.DocumentsMongo/bin') -Filter $exeName |
    Where-Object { $_.FullName -match 'publish' } | Select-Object -First 1
if (-not $exe) { throw "Native binary not found" }

$user = 'root'
$password = 'aot-Pa55'
$name = 'pragmatic-aot-mongo-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
docker run -d --name $name -p 127.0.0.1::27017 -e "MONGO_INITDB_ROOT_USERNAME=$user" -e "MONGO_INITDB_ROOT_PASSWORD=$password" mongo:8 | Out-Null
if ($LASTEXITCODE -ne 0) { throw "MongoDB did not start in Docker (is Docker running?)" }
try {
    # The image's entrypoint runs a first mongod on localhost only to create the user, then the real one: ready at the second
    # "Waiting for connections" (the documents suite's MongoServer waits the same way).
    $deadline = (Get-Date).AddMinutes(2)
    while (([regex]::Matches((docker logs $name 2>&1 | Out-String), 'Waiting for connections')).Count -lt 2) {
        if ((Get-Date) -gt $deadline) { throw "MongoDB was not ready within 2 minutes" }
        Start-Sleep -Milliseconds 500
    }

    $port = ((docker port $name 27017) | Select-Object -First 1).Split(':')[-1]
    $env:PRAGMATIC_AOT_MONGO = "mongodb://${user}:${password}@127.0.0.1:${port}/?authSource=admin"

    Write-Host "Running $($exe.FullName)..."
    $output = & $exe.FullName 2>&1
    Write-Host ($output -join "`n")
    if ($LASTEXITCODE -ne 0 -or ($output -join "`n") -notmatch 'MONGO-AOT-OK') {
        throw "AOT documents engine smoke FAILED (exit $LASTEXITCODE)"
    }
}
finally {
    docker rm -f $name | Out-Null
}
Write-Host "AOT documents engine smoke PASSED."
