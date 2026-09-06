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
    [string]$RepoRoot,
    [scriptblock]$Executor
)

$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "lib/Heartbeat.ps1")

# Dashboard reporting is optional: only if a RunId + RepoRoot were passed AND
# the .secrets/ config exists. Send-FactoryEvent is a no-op otherwise.
if ($RunId -and $RepoRoot) {
    . (Join-Path $RepoRoot "factory-report.ps1")
    Initialize-FactoryReport -RepoRoot $RepoRoot
}

try {
    $vmStartEpoch = $null
    $lastReportedHeartbeat = $null

    while ($Job.State -eq "Running") {
        $sample = Get-HeartbeatSample -Name $VmName -Executor $Executor
        $vmNow = $sample.VmNow
        $heartbeatEpoch = $sample.HeartbeatEpoch

        # Report a heartbeat only when the marker mtime actually advanced since
        # last reported — an idle agent simply stops reporting. The current-phase
        # marker is read alongside it and attached as currentPhase so the phase
        # name advances through the event stream as the run progresses.
        if ($RunId -and $heartbeatEpoch -ne $null -and $heartbeatEpoch -ne $lastReportedHeartbeat) {
            $lastReportedHeartbeat = $heartbeatEpoch
            $at = [DateTimeOffset]::FromUnixTimeSeconds($heartbeatEpoch).UtcDateTime.ToString("o")
            $fields = @{ at = $at }
            if ($sample.CurrentPhase) { $fields["currentPhase"] = $sample.CurrentPhase }
            Send-FactoryEvent -RunId $RunId -Type "heartbeat" -Fields $fields
        }

        $verdict = Test-HeartbeatStall -VmNow $vmNow -HeartbeatEpoch $heartbeatEpoch -VmStartEpoch $vmStartEpoch -StallThresholdSeconds $StallThresholdSeconds
        $vmStartEpoch = $verdict.VmStartEpoch
        $staleSeconds = $verdict.StaleSeconds
        if ($verdict.IsStalled) {
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
