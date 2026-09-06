#!/usr/bin/env powershell
#Requires -Version 5.1
<#
.SYNOPSIS
  Heartbeat staleness behind one Seam (lib/Heartbeat.ps1).

.DESCRIPTION
  Dot-source this file — no side effects on load:
    . (Join-Path $RepoRoot "lib/Heartbeat.ps1")
  VM reads hide behind an injectable -Executor Seam so Pester drives scripted
  epoch sequences without a live VM. Prod callers omit -Executor.
#>

. (Join-Path $PSScriptRoot "HostVm.ps1")
. (Join-Path $PSScriptRoot "JobIntake.ps1")

function Get-VmEpochSeconds {
    param([string]$Name, [scriptblock]$Executor)
    $output = Invoke-MultipassOutput -Arguments @('exec', $Name, '--', 'date', '+%s') -Executor $Executor
    return [int]::Parse($output.Trim())
}

function Get-HeartbeatEpochSeconds {
    param([string]$Name, [scriptblock]$Executor)
    try {
        $output = Invoke-MultipassOutput -Arguments @('exec', $Name, '--', 'stat', '-c', '%Y', '/tmp/heartbeat') -Executor $Executor
        return [int]::Parse($output.Trim())
    }
    catch {
        return $null
    }
}

function Get-VmCurrentPhase {
    param([string]$Name, [scriptblock]$Executor)
    try {
        $output = Invoke-MultipassOutput -Arguments @('exec', $Name, '--', 'cat', '/tmp/current-phase') -Executor $Executor
        if ($output) { return ($output.Trim()) }
        return $null
    }
    catch {
        return $null
    }
}

function Get-HeartbeatSample {
    param([string]$Name, [scriptblock]$Executor)
    $vmNow = Get-VmEpochSeconds -Name $Name -Executor $Executor
    $heartbeatEpoch = Get-HeartbeatEpochSeconds -Name $Name -Executor $Executor
    $currentPhase = $null
    if ($heartbeatEpoch -ne $null) {
        $currentPhase = Get-VmCurrentPhase -Name $Name -Executor $Executor
    }
    return [PSCustomObject]@{ VmNow = $vmNow; HeartbeatEpoch = $heartbeatEpoch; CurrentPhase = $currentPhase }
}

function Test-HeartbeatStall {
    param([long]$VmNow, $HeartbeatEpoch, $VmStartEpoch, [int]$StallThresholdSeconds)
    $referenceEpoch = if ($HeartbeatEpoch -ne $null) { $HeartbeatEpoch } else {
        if ($VmStartEpoch -eq $null) { $VmStartEpoch = $VmNow }
        $VmStartEpoch
    }
    $staleSeconds = Get-StaleSeconds -VmNow $VmNow -ReferenceEpoch $referenceEpoch
    return [PSCustomObject]@{
        StaleSeconds = $staleSeconds
        IsStalled = ($staleSeconds -gt $StallThresholdSeconds)
        ReferenceEpoch = $referenceEpoch
        VmStartEpoch = $VmStartEpoch
    }
}
