#!/usr/bin/env powershell
#Requires -Version 5.1
<#
.SYNOPSIS
  Watch the VM /tmp/heartbeat marker while a background job runs and fail if it stalls.

.DESCRIPTION
  Polls the /tmp/heartbeat file inside the VM via multipass exec. Staleness is
  measured against the VM's own clock (date +%s). If the marker is stale for
  more than the configured threshold, the background job is stopped and this
  script throws. If the job finishes before the threshold, its output is
  returned and the script exits cleanly.

  Before the first heartbeat is written, the staleness clock starts from the
  first VM clock sample taken while watching, so the job is not failed
  immediately.

.PARAMETER VmName
  multipass VM name to poll.

.PARAMETER Job
  Background PowerShell job running the in-VM work.

.PARAMETER StallThresholdSeconds
  Heartbeat staleness threshold in seconds. Defaults to 300 (5 minutes).

.PARAMETER PollIntervalSeconds
  Seconds between polls. Defaults to 10.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$VmName,
    [Parameter(Mandatory = $true)][System.Management.Automation.Job]$Job,
    [int]$StallThresholdSeconds = 300,
    [int]$PollIntervalSeconds = 10,
    [string]$RunId,
    [string]$RepoRoot
)

$ErrorActionPreference = "Stop"

# Dashboard reporting is optional: only if a RunId + RepoRoot were passed AND
# the .secrets/ config exists. Send-FactoryEvent is a no-op otherwise.
if ($RunId -and $RepoRoot) {
    . (Join-Path $RepoRoot "factory-report.ps1")
    Initialize-FactoryReport -RepoRoot $RepoRoot
}

function Get-VmEpochSeconds {
    param([string]$Name)
    $prev = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    $output = & multipass exec $Name -- date +%s 2>&1
    $exitCode = $LASTEXITCODE
    $ErrorActionPreference = $prev
    if ($exitCode -ne 0) {
        throw "Could not read VM clock: $output"
    }
    return [int]::Parse($output.Trim())
}

function Get-HeartbeatEpochSeconds {
    param([string]$Name)
    $prev = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    $output = & multipass exec $Name -- stat -c %Y /tmp/heartbeat 2>&1
    $exitCode = $LASTEXITCODE
    $ErrorActionPreference = $prev
    if ($exitCode -eq 0) {
        return [int]::Parse($output.Trim())
    }
    return $null
}

try {
    $vmStartEpoch = $null
    $lastReportedHeartbeat = $null

    while ($Job.State -eq "Running") {
        $vmNow = Get-VmEpochSeconds -Name $VmName
        $heartbeatEpoch = Get-HeartbeatEpochSeconds -Name $VmName

        # Report a heartbeat only when the marker mtime actually advanced since
        # last reported — an idle agent simply stops reporting.
        if ($RunId -and $heartbeatEpoch -ne $null -and $heartbeatEpoch -ne $lastReportedHeartbeat) {
            $lastReportedHeartbeat = $heartbeatEpoch
            $at = [DateTimeOffset]::FromUnixTimeSeconds($heartbeatEpoch).UtcDateTime.ToString("o")
            Send-FactoryEvent -RunId $RunId -Type "heartbeat" -Fields @{ at = $at }
        }

        $referenceEpoch = if ($heartbeatEpoch -ne $null) {
            $heartbeatEpoch
        }
        else {
            if ($vmStartEpoch -eq $null) { $vmStartEpoch = $vmNow }
            $vmStartEpoch
        }

        $staleSeconds = $vmNow - $referenceEpoch
        if ($staleSeconds -gt $StallThresholdSeconds) {
            $stallReason = "Heartbeat stale for ${staleSeconds}s (threshold ${StallThresholdSeconds}s); job appears stalled"
            if ($RunId) {
                Send-FactoryEvent -RunId $RunId -Type "stall-detected" -Fields @{ failureReason = $stallReason }
            }
            throw $stallReason
        }

        Start-Sleep -Seconds $PollIntervalSeconds
    }

    Receive-Job -Job $Job -Wait -AutoRemoveJob
}
catch {
    Stop-Job -Job $Job -ErrorAction SilentlyContinue
    Remove-Job -Job $Job -Force -ErrorAction SilentlyContinue
    throw
}
