#Requires -Version 5.1
# FlowPilot AI - one-shot dev starter
# Boots: docker (postgres + seq) -> API -> Workers -> ngrok -> Web
# Ctrl+C stops everything cleanly.

$ErrorActionPreference = 'Stop'

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $Root

$LogDir = Join-Path $Root '.dev-logs'
New-Item -ItemType Directory -Force -Path $LogDir | Out-Null

$NgrokUrl = 'curiously-funky-stud.ngrok-free.app'
$ApiPort  = 5216
$WebPort  = 5173

$PidFile = Join-Path $LogDir 'pids.txt'
Remove-Item $PidFile -ErrorAction SilentlyContinue

$script:Processes = @()
$script:TailJobs  = @()

function Start-DevService {
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

    $p = Start-Process -FilePath $FilePath `
        -ArgumentList $ArgumentList `
        -WorkingDirectory $WorkingDirectory `
        -RedirectStandardOutput $logOut `
        -RedirectStandardError  $logErr `
        -NoNewWindow `
        -PassThru

    $script:Processes += $p
    Add-Content -Path $PidFile -Value $p.Id
    Write-Host "[flowpilot] started $Name (PID $($p.Id)) -> .dev-logs\$Name.log"
}

function Stop-Tree {
    param([int]$ParentId)
    try {
        Get-CimInstance Win32_Process -Filter "ParentProcessId=$ParentId" -ErrorAction SilentlyContinue |
            ForEach-Object {
                Stop-Tree -ParentId $_.ProcessId
                Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue
            }
    } catch {}
}

function Stop-All {
    Write-Host ""
    Write-Host "[flowpilot] shutting down..."
    if ($script:TailJobs) {
        $script:TailJobs | Stop-Job       -ErrorAction SilentlyContinue
        $script:TailJobs | Remove-Job -Force -ErrorAction SilentlyContinue
    }
    foreach ($p in $script:Processes) {
        if ($p -and -not $p.HasExited) {
            Stop-Tree -ParentId $p.Id
            Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
        }
    }
    Write-Host "[flowpilot] stopped."
}

try {
    Write-Host "[flowpilot] starting docker (postgres + seq)..."
    docker compose up -d | Out-Null

    Write-Host "[flowpilot] waiting for postgres to be healthy..."
    for ($i = 0; $i -lt 30; $i++) {
        $status = (docker inspect -f '{{.State.Health.Status}}' flowpilot_db 2>$null)
        if ($status -eq 'healthy') {
            Write-Host "[flowpilot] postgres ready."
            break
        }
        Start-Sleep -Seconds 1
    }

    if ($status -ne 'healthy') {
        throw "Postgres did not become healthy in time."
    }

    Write-Host "[flowpilot] building solution (prevents API/Workers MSBuild race on FlowPilot.Shared)..."
    $buildLog = Join-Path $LogDir 'build.log'
    & dotnet build FlowPilot.sln --nologo -v minimal *> $buildLog
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[flowpilot] build FAILED - see .dev-logs\build.log" -ForegroundColor Red
        Get-Content $buildLog -Tail 40
        throw "dotnet build failed"
    }
    Write-Host "[flowpilot] build ok."

    Write-Host "[flowpilot] applying database migrations..."
    $migrationLog = Join-Path $LogDir 'migrate.log'
    & dotnet ef database update --project src/FlowPilot.Infrastructure --startup-project src/FlowPilot.Api --no-build *> $migrationLog
    if ($LASTEXITCODE -ne 0) {
        Write-Host "[flowpilot] migration FAILED - see .dev-logs\migrate.log" -ForegroundColor Red
        Get-Content $migrationLog -Tail 40
        throw "dotnet ef database update failed"
    }
    Write-Host "[flowpilot] database up to date."

    Start-DevService -Name 'api' -FilePath 'dotnet' `
        -ArgumentList @('run','--project','src/FlowPilot.Api','--no-launch-profile','--no-build') `
        -EnvVars @{
            ASPNETCORE_ENVIRONMENT = 'Development'
            ASPNETCORE_URLS        = "http://localhost:$ApiPort"
        }

    Start-DevService -Name 'workers' -FilePath 'dotnet' `
        -ArgumentList @('run','--project','src/FlowPilot.Workers','--no-build') `
        -EnvVars @{
            DOTNET_ENVIRONMENT     = 'Development'
            ASPNETCORE_ENVIRONMENT = 'Development'
        }

    Start-DevService -Name 'ngrok' -FilePath 'ngrok' `
        -ArgumentList @('http', "--url=$NgrokUrl", "$ApiPort", '--log=stdout')

    Start-DevService -Name 'web' -FilePath 'npm.cmd' `
        -ArgumentList @('run','dev','--','--port',"$WebPort") `
        -WorkingDirectory (Join-Path $Root 'src/FlowPilot.Web')

    Write-Host ""
    Write-Host "[flowpilot] all services launched:"
    Write-Host "  API       http://localhost:$ApiPort"
    Write-Host "  Web       http://localhost:$WebPort"
    Write-Host "  Ngrok     https://$NgrokUrl  ->  :$ApiPort"
    Write-Host "  Seq       http://localhost:5341"
    Write-Host "  Postgres  localhost:5432  (flowpilot / flowpilot_dev_pass)"
    Write-Host ""
    Write-Host "[flowpilot] tailing logs - press Ctrl+C to stop everything."
    Write-Host ""

    $logs = @(
        @{ Label = 'api    '; Path = (Join-Path $LogDir 'api.log') },
        @{ Label = 'workers'; Path = (Join-Path $LogDir 'workers.log') },
        @{ Label = 'ngrok  '; Path = (Join-Path $LogDir 'ngrok.log') },
        @{ Label = 'web    '; Path = (Join-Path $LogDir 'web.log') }
    )
    foreach ($log in $logs) {
        if (-not (Test-Path $log.Path)) { New-Item -ItemType File -Path $log.Path | Out-Null }
        $script:TailJobs += Start-Job -ScriptBlock {
            param($path, $label)
            Get-Content -Path $path -Wait -Tail 0 | ForEach-Object { "[$label] $_" }
        } -ArgumentList $log.Path, $log.Label
    }

    while ($true) {
        $script:TailJobs | Receive-Job
        Start-Sleep -Milliseconds 400
    }
}
finally {
    Stop-All
}
