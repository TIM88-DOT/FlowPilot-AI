#Requires -Version 5.1
# FlowPilot AI - detached dev boot for autonomous QA.
# Unlike start.ps1 (which blocks tailing logs), this starts docker + API + Workers + Web
# in the background, waits until they're reachable, writes PIDs to .dev-logs\pids.txt
# (so stop.ps1 can clean up), then RETURNS so an agent can run tests.
#
# Usage:   powershell -ExecutionPolicy Bypass -File ./qa-up.ps1
# Teardown: powershell -ExecutionPolicy Bypass -File ./stop.ps1
#
# Notes: ngrok is intentionally skipped — QA hits localhost directly.

$ErrorActionPreference = 'Stop'

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $Root

$LogDir = Join-Path $Root '.dev-logs'
New-Item -ItemType Directory -Force -Path $LogDir | Out-Null

$ApiPort = 5216
$WebPort = 5173
$PidFile = Join-Path $LogDir 'pids.txt'

function Wait-ForPort {
    param([int]$Port, [string]$Label, [int]$TimeoutSec = 120)
    Write-Host "[qa-up] waiting for $Label on :$Port ..."
    for ($i = 0; $i -lt $TimeoutSec; $i++) {
        $listening = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue
        if ($listening) { Write-Host "[qa-up] $Label is up."; return $true }
        Start-Sleep -Seconds 1
    }
    Write-Host "[qa-up] TIMEOUT waiting for $Label on :$Port" -ForegroundColor Red
    return $false
}

function Start-Detached {
    param(
        [Parameter(Mandatory)][string]$Name,
        [Parameter(Mandatory)][string]$FilePath,
        [string[]]$ArgumentList = @(),
        [string]$WorkingDirectory = $Root,
        [hashtable]$EnvVars = @{}
    )
    $logOut = Join-Path $LogDir "$Name.log"
    $logErr = Join-Path $LogDir "$Name.err.log"
    '' | Set-Content -Path $logOut
    '' | Set-Content -Path $logErr
    foreach ($k in $EnvVars.Keys) {
        [Environment]::SetEnvironmentVariable($k, $EnvVars[$k], 'Process')
    }
    $p = Start-Process -FilePath $FilePath -ArgumentList $ArgumentList `
        -WorkingDirectory $WorkingDirectory `
        -RedirectStandardOutput $logOut -RedirectStandardError $logErr `
        -NoNewWindow -PassThru
    Add-Content -Path $PidFile -Value $p.Id
    Write-Host "[qa-up] started $Name (PID $($p.Id)) -> .dev-logs\$Name.log"
}

# Fresh PID file each boot
Remove-Item $PidFile -ErrorAction SilentlyContinue

# Already running? Skip re-boot of app processes but still ensure infra.
$apiAlready = [bool](Get-NetTCPConnection -LocalPort $ApiPort -State Listen -ErrorAction SilentlyContinue)
$webAlready = [bool](Get-NetTCPConnection -LocalPort $WebPort -State Listen -ErrorAction SilentlyContinue)

Write-Host "[qa-up] starting docker (postgres + seq)..."
docker compose up -d | Out-Null

Write-Host "[qa-up] waiting for postgres to be healthy..."
$status = ''
for ($i = 0; $i -lt 30; $i++) {
    $status = (docker inspect -f '{{.State.Health.Status}}' flowpilot_db 2>$null)
    if ($status -eq 'healthy') { Write-Host "[qa-up] postgres ready."; break }
    Start-Sleep -Seconds 1
}
if ($status -ne 'healthy') { throw "Postgres did not become healthy in time." }

Write-Host "[qa-up] building solution..."
$buildLog = Join-Path $LogDir 'build.log'
& dotnet build FlowPilot.sln --nologo -v minimal *> $buildLog
if ($LASTEXITCODE -ne 0) {
    Get-Content $buildLog -Tail 40
    throw "dotnet build failed"
}

Write-Host "[qa-up] applying database migrations..."
$migrationLog = Join-Path $LogDir 'migrate.log'
& dotnet ef database update --project src/FlowPilot.Infrastructure --startup-project src/FlowPilot.Api --no-build *> $migrationLog
if ($LASTEXITCODE -ne 0) {
    Get-Content $migrationLog -Tail 40
    throw "dotnet ef database update failed"
}

if (-not $apiAlready) {
    Start-Detached -Name 'api' -FilePath 'dotnet' `
        -ArgumentList @('run','--project','src/FlowPilot.Api','--no-launch-profile','--no-build') `
        -EnvVars @{ ASPNETCORE_ENVIRONMENT = 'Development'; ASPNETCORE_URLS = "http://localhost:$ApiPort" }

    Start-Detached -Name 'workers' -FilePath 'dotnet' `
        -ArgumentList @('run','--project','src/FlowPilot.Workers','--no-build') `
        -EnvVars @{ DOTNET_ENVIRONMENT = 'Development'; ASPNETCORE_ENVIRONMENT = 'Development' }
} else {
    Write-Host "[qa-up] API already listening on :$ApiPort — not re-starting API/Workers."
}

if (-not $webAlready) {
    Start-Detached -Name 'web' -FilePath 'npm.cmd' `
        -ArgumentList @('run','dev','--','--port',"$WebPort") `
        -WorkingDirectory (Join-Path $Root 'src/FlowPilot.Web')
} else {
    Write-Host "[qa-up] Web already listening on :$WebPort — not re-starting Web."
}

$apiUp = Wait-ForPort -Port $ApiPort -Label 'API'
$webUp = Wait-ForPort -Port $WebPort -Label 'Web'

Write-Host ""
if ($apiUp -and $webUp) {
    Write-Host "[qa-up] READY:" -ForegroundColor Green
    Write-Host "  API  http://localhost:$ApiPort"
    Write-Host "  Web  http://localhost:$WebPort"
    Write-Host "  Seq  http://localhost:5341"
    Write-Host "[qa-up] run QA, then: powershell -ExecutionPolicy Bypass -File ./stop.ps1"
    exit 0
} else {
    Write-Host "[qa-up] NOT READY — check .dev-logs\api.log and .dev-logs\web.log" -ForegroundColor Red
    exit 1
}
