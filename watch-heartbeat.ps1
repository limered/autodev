#!/usr/bin/env powershell
#Requires -Version 5.1
<#
.SYNOPSIS
  Watch the VM /tmp/heartbeat marker while a background job runs and fail if it stalls.

.DESCRIPTION
  Polls the guest with one VM read per iteration (Get-HeartbeatPoll): the
  VM clock, the /tmp/heartbeat mtime, the current-phase/category markers,
  and the /tmp/factory-done exit code arrive together, and the stall verdict
  is computed from the same poll. Staleness is measured against the VM's
  own clock (date +%s). If the marker is stale for more than the configured
  threshold, the background job is stopped and this script throws. If the
  job finishes before the threshold, its output is returned and the script
  exits cleanly.

  Before the first heartbeat is written, the staleness clock starts from the
  first VM clock sample taken while watching, so the job is not failed
  immediately.

  Output-progress semantics (ADR 002): the marker advances only when the
  agent emits a new output line. A long silent model turn emits nothing, so
  the threshold defaults to 600s (10 minutes) — long-silence tolerance
  explicit in the sampler.

.PARAMETER VmName
  multipass VM name to poll.

.PARAMETER Job
  Background PowerShell job running the in-VM work.

.PARAMETER StallThresholdSeconds
  Heartbeat staleness threshold in seconds. Defaults to 600 (10 minutes).

.PARAMETER PollIntervalSeconds
  Seconds between polls. Defaults to 10.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$VmName,
    [Parameter(Mandatory = $true)][System.Management.Automation.Job]$Job,
    [int]$StallThresholdSeconds = 600,
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
        # One VM read per poll: sample, verdict, and completion together.
        $poll = Get-HeartbeatPoll -Name $VmName -Executor $Executor -StallThresholdSeconds $StallThresholdSeconds -VmStartEpoch $vmStartEpoch
        $vmStartEpoch = $poll.VmStartEpoch
        $heartbeatEpoch = $poll.HeartbeatEpoch

        # Report a heartbeat only when the marker mtime actually advanced since
        # last reported — an idle agent simply stops reporting. The current-phase
        # and current-category markers are read alongside it and attached so the
        # worker name and the category slot advance through the event stream as
        # the run progresses.
        if ($RunId -and $heartbeatEpoch -ne $null -and $heartbeatEpoch -ne $lastReportedHeartbeat) {
            $lastReportedHeartbeat = $heartbeatEpoch
            $at = [DateTimeOffset]::FromUnixTimeSeconds($heartbeatEpoch).UtcDateTime.ToString("o")
            $fields = @{ at = $at }
            if ($poll.CurrentPhase) { $fields["currentPhase"] = $poll.CurrentPhase }
            if ($poll.CurrentCategory) { $fields["currentCategory"] = $poll.CurrentCategory }
            Send-FactoryEvent -RunId $RunId -Type "heartbeat" -Fields $fields
        }

        # /tmp/factory-done is written by the VM on EXIT; when present the
        # streaming client hung, so stop it and continue from the marker.
        $doneExit = $poll.DoneExit
        if ($doneExit -ne $null) {
            Stop-Job -Job $Job -ErrorAction SilentlyContinue
            Receive-Job -Job $Job -ErrorAction SilentlyContinue
            Remove-Job -Job $Job -Force -ErrorAction SilentlyContinue
            if ($doneExit -ne 0) {
                throw "VM script exited with code ${doneExit} (streaming client stopped)"
            }
            return
        }

        $staleSeconds = $poll.StaleSeconds
        if ($poll.IsStalled) {
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
