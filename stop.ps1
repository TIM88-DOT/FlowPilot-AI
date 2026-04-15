#Requires -Version 5.1
# FlowPilot AI - dev teardown
# Kills API (:5216), Web (:5173), ngrok, and stops docker containers.

$ErrorActionPreference = 'Continue'

$Root    = Split-Path -Parent $MyInvocation.MyCommand.Path
$LogDir  = Join-Path $Root '.dev-logs'
$PidFile = Join-Path $LogDir 'pids.txt'

$ApiPort = 5216
$WebPort = 5173

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

function Stop-ByPid {
    param([int]$TargetPid, [string]$Label)
    $proc = Get-Process -Id $TargetPid -ErrorAction SilentlyContinue
    if ($null -eq $proc) {
        Write-Host "[flowpilot] $Label (PID $TargetPid) - already gone."
        return
    }
    Write-Host "[flowpilot] $Label - killing PID $TargetPid ($($proc.ProcessName))"
    Stop-Tree -ParentId $TargetPid
    Stop-Process -Id $TargetPid -Force -ErrorAction SilentlyContinue
}

function Stop-ByPort {
    param([int]$Port, [string]$Label)
    $conns = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue
    if (-not $conns) {
        Write-Host "[flowpilot] $Label (:$Port) - nothing listening."
        return
    }
    foreach ($c in $conns) {
        Stop-ByPid -TargetPid $c.OwningProcess -Label $Label
    }
}

function Stop-ByName {
    param([string]$Name, [string]$Label)
    $procs = Get-Process -Name $Name -ErrorAction SilentlyContinue
    if (-not $procs) {
        Write-Host "[flowpilot] $Label - not running."
        return
    }
    foreach ($p in $procs) {
        Write-Host "[flowpilot] $Label - killing PID $($p.Id)"
        Stop-Tree -ParentId $p.Id
        Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
    }
}

Write-Host "[flowpilot] stopping dev services..."

if (Test-Path $PidFile) {
    Get-Content $PidFile | ForEach-Object {
        if ($_ -match '^\d+$') { Stop-ByPid -TargetPid ([int]$_) -Label "tracked" }
    }
    Remove-Item $PidFile -ErrorAction SilentlyContinue
}

Stop-ByPort -Port $ApiPort -Label 'API'
Stop-ByPort -Port $WebPort -Label 'Web'
Stop-ByName -Name 'ngrok'  -Label 'Ngrok'

Write-Host "[flowpilot] stopping docker containers..."
Push-Location $Root
try {
    docker compose down | Out-Null
} finally {
    Pop-Location
}

Write-Host "[flowpilot] done."
