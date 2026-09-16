#!/usr/bin/env powershell
#Requires -Version 5.1
<#
.SYNOPSIS
  Heartbeat staleness behind one Seam (lib/Heartbeat.ps1).

.DESCRIPTION
  Dot-source this file — no side effects on load:
    . (Join-Path $RepoRoot "lib/Heartbeat.ps1")
  One poll is one VM read: Get-HeartbeatPoll runs a single `bash -c` probe
  inside the guest that prints the VM clock, the /tmp/heartbeat mtime, the
  current-phase/category markers, and the /tmp/factory-done exit code as
  key=value lines, then returns the sample, the stall verdict, and completion
  together. VM reads hide behind an injectable -Executor Seam so Pester
  drives scripted poll outputs without a live VM. Prod callers omit
  -Executor.

  Output-progress semantics (ADR 002): the marker is touched only when the
  agent emits a new output line, so a silent agent lets it go stale. A long
  silent model turn emits nothing for minutes, so the stall threshold
  defaults to 600s (10 minutes) — long-silence tolerance explicit in the
  sampler, not in the marker.
#>

. (Join-Path $PSScriptRoot "HostVm.ps1")
. (Join-Path $PSScriptRoot "JobIntake.ps1")

# Single probe run inside the guest. One exec, five key=value lines:
# now=<epoch> hb=<epoch|MISSING> phase=<name|MISSING>
# category=<name|MISSING> done=<exit|MISSING>
function Get-HeartbeatPollScript {
    return 'now=$(date +%s); ' +
        'if hb=$(stat -c %Y /tmp/heartbeat 2>/dev/null); then :; else hb=MISSING; fi; ' +
        'if ph=$(cat /tmp/current-phase 2>/dev/null); then :; else ph=MISSING; fi; ' +
        'if cg=$(cat /tmp/current-category 2>/dev/null); then :; else cg=MISSING; fi; ' +
        'if dn=$(cat /tmp/factory-done 2>/dev/null); then :; else dn=MISSING; fi; ' +
        'printf ''now=%s\nhb=%s\nphase=%s\ncategory=%s\ndone=%s\n'' "$now" "$hb" "$ph" "$cg" "$dn"'
}

# Parses one poll output into a sample. First occurrence per key wins so a
# noisy guest cannot smuggle a second value past the probe.
function ConvertFrom-HeartbeatPollOutput {
    param([string]$Content)
    $map = @{}
    foreach ($line in ("$Content" -split "`r?`n")) {
        $trimmed = "$line".Trim()
        if (-not $trimmed) { continue }
        $eq = $trimmed.IndexOf('=')
        if ($eq -lt 0) { continue }
        $key = $trimmed.Substring(0, $eq).Trim().ToLowerInvariant()
        if (-not $key -or $map.ContainsKey($key)) { continue }
        $map[$key] = $trimmed.Substring($eq + 1).Trim()
    }
    if (-not $map.ContainsKey('now')) { throw "heartbeat poll missing VM clock (now=)" }
    $vmNow = 0
    if (-not [long]::TryParse($map['now'], [ref]$vmNow)) { throw "heartbeat poll has non-numeric VM clock: $($map['now'])" }

    $heartbeatEpoch = $null
    if ($map.ContainsKey('hb') -and $map['hb'] -ne 'MISSING' -and $map['hb'] -ne '') {
        $hb = 0
        if ([long]::TryParse($map['hb'], [ref]$hb)) { $heartbeatEpoch = $hb }
    }
    $currentPhase = $null
    if ($map.ContainsKey('phase') -and $map['phase'] -ne 'MISSING' -and $map['phase'] -ne '') {
        $currentPhase = $map['phase']
    }
    $currentCategory = $null
    if ($map.ContainsKey('category') -and $map['category'] -ne 'MISSING' -and $map['category'] -ne '') {
        $currentCategory = $map['category']
    }
    $doneExit = $null
    if ($map.ContainsKey('done') -and $map['done'] -ne 'MISSING' -and $map['done'] -ne '') {
        $code = 0
        if ([int]::TryParse($map['done'], [ref]$code)) { $doneExit = $code }
    }
    return [PSCustomObject]@{
        VmNow           = $vmNow
        HeartbeatEpoch  = $heartbeatEpoch
        CurrentPhase    = $currentPhase
        CurrentCategory = $currentCategory
        DoneExit        = $doneExit
    }
}

# One poll = one VM read. Returns the sample, the stall verdict, and
# completion together so the loop learns everything from a single call.
function Get-HeartbeatPoll {
    param([string]$Name, [scriptblock]$Executor, [int]$StallThresholdSeconds = 600, $VmStartEpoch = $null)
    $output = Invoke-MultipassOutput -Arguments @('exec', $Name, '--', 'bash', '-c', (Get-HeartbeatPollScript)) -Executor $Executor
    $sample = ConvertFrom-HeartbeatPollOutput -Content $output
    $verdict = Test-HeartbeatStall -VmNow $sample.VmNow -HeartbeatEpoch $sample.HeartbeatEpoch -VmStartEpoch $VmStartEpoch -StallThresholdSeconds $StallThresholdSeconds
    return [PSCustomObject]@{
        VmNow           = $sample.VmNow
        HeartbeatEpoch  = $sample.HeartbeatEpoch
        CurrentPhase    = $sample.CurrentPhase
        CurrentCategory = $sample.CurrentCategory
        DoneExit        = $sample.DoneExit
        StaleSeconds    = $verdict.StaleSeconds
        IsStalled       = $verdict.IsStalled
        ReferenceEpoch  = $verdict.ReferenceEpoch
        VmStartEpoch    = $verdict.VmStartEpoch
    }
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
