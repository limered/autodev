#!/usr/bin/env powershell
#Requires -Version 5.1
<#
.SYNOPSIS
  Prove the freeze-snapshot-on-stall path end to end against a real VM.

.DESCRIPTION
  Launches a throwaway multipass VM, starts a job that deliberately hangs and
  never touches /tmp/heartbeat, runs the real watch-heartbeat.ps1 stall
  detector with a tiny threshold, and on the resulting stall throw runs the
  real Save-FreezeSnapshot from start-job.ps1. Then asserts the manifest landed
  on the host, survives VM destruction, and contains the required fields.
#>
[CmdletBinding()]
param(
    [string]$RepoRoot = $PSScriptRoot,
    [switch]$KeepVm
)

$ErrorActionPreference = "Stop"
. (Join-Path $RepoRoot "lib/HostVm.ps1")
$VmName = "freeze-test-$(Get-Date -Format 'yyyyMMdd-HHmmss')-$(Get-Random -Maximum 9999)"

$watch = Join-Path $RepoRoot "watch-heartbeat.ps1"

$vmCreated = $false
$snapshotPath = $null
try {
    Write-Step "Launching test VM $VmName"
    New-VmFromBlueprint -Name $VmName -Cpus '1' -Memory '1G' -Disk '5G' -NoWait
    $vmCreated = $true

    # Deliberate stall: sleep long, never touch /tmp/heartbeat.
    $hangJob = Start-Job -ScriptBlock {
        param($VmName)
        & multipass exec $VmName -- bash -c "sleep 600" 2>&1
    } -ArgumentList $VmName

    Write-Host "==> Watching for stall (threshold 15s)" -ForegroundColor Cyan
    try {
        & $watch -VmName $VmName -Job $hangJob -StallThresholdSeconds 15 -PollIntervalSeconds 5
        throw "TEST FAIL: watcher did not detect a stall"
    }
    catch {
        if ("$_" -notmatch "stalled") { throw }
        Write-Host "==> Stall detected as expected: $_" -ForegroundColor Green
    }

    # Real capture from the still-alive VM.
    Save-FreezeSnapshot -Name $VmName -RepoRoot $RepoRoot -JobParams @{
        repo = "test/repo"; branch = "freeze-test"; spec = "hang the vm"; model = "none"
    }
    $snapshotPath = (Get-ChildItem (Join-Path $RepoRoot ".scratch\freezes\$VmName-*") | Select-Object -First 1).FullName
}
finally {
    if ($vmCreated -and -not $KeepVm) {
        Remove-Vm -Name $VmName
    }
}

# Assertions — run after teardown to prove the snapshot survives it.
$manifest = Join-Path $snapshotPath "manifest.json"
if (-not (Test-Path $manifest)) { throw "TEST FAIL: manifest missing at $manifest" }
$j = Get-Content -Raw $manifest | ConvertFrom-Json
$checks = @{
    "jobParams.repo" = $j.jobParams.repo -eq "test/repo"
    "psAux non-empty" = $j.psAux -match "PID"
    "freeM non-empty" = $j.freeM -match "Mem"
    "dfH non-empty"   = $j.dfH -match "Filesystem"
    "markerMtime"     = [bool]$j.markerMtime
    "agentLogTail"    = [bool]$j.agentLogTail
}
$failed = $checks.GetEnumerator() | Where-Object { -not $_.Value }
if ($failed) { $failed | ForEach-Object { Write-Host "FAIL: $($_.Key)" -ForegroundColor Red }; throw "TEST FAIL" }

Write-Host "==> PASS: freeze snapshot survived teardown at $manifest" -ForegroundColor Green
$j.jobParams
