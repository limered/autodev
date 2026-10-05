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
  current-phase/category markers, the /tmp/factory-done exit code, and the
  opencode stream-error count/message as key=value lines, then returns the
  sample, the stall verdict, and completion together. VM reads hide behind
  an injectable -Executor Seam so Pester drives scripted poll outputs
  without a live VM. Prod callers omit -Executor.

  Output-progress semantics (ADR 002): the marker is touched only when the
  agent emits a new output line, so a silent agent lets it go stale. A long
  silent model turn emits nothing for minutes, so the stall threshold
  defaults to 600s (10 minutes) — long-silence tolerance explicit in the
  sampler, not in the marker.
#>

. (Join-Path $PSScriptRoot "RuntimeEnvironment.ps1")
. (Join-Path $PSScriptRoot "JobIntake.ps1")

# Single probe run inside the guest. One exec, seven key=value lines:
# now=<epoch> hb=<epoch|MISSING> phase=<name|MISSING>
# category=<name|MISSING> done=<exit|MISSING>
# err=<stream-error count, 0 when no log> errmsg=<last stream-error line|MISSING>
# err/errmsg come from the newest opencode log (same discovery as the
# freeze snapshot) and feed the stream-error retry verdict: a dead model
# stream fails in minutes instead of riding the full stall threshold,
# while a hiccup the agent recovers from never fires.
function Get-HeartbeatPollScript {
    return 'now=$(date +%s); ' +
        'if hb=$(stat -c %Y /tmp/heartbeat 2>/dev/null); then :; else hb=MISSING; fi; ' +
        'if ph=$(cat /tmp/current-phase 2>/dev/null); then :; else ph=MISSING; fi; ' +
        'if cg=$(cat /tmp/current-category 2>/dev/null); then :; else cg=MISSING; fi; ' +
        'if dn=$(cat /tmp/factory-done 2>/dev/null); then :; else dn=MISSING; fi; ' +
        'err=0; em=MISSING; ' +
        'elog=$(ls -t ~/.local/share/opencode/log/*.log 2>/dev/null | head -1); ' +
        'if [ -n "$elog" ]; then c=$(grep -c "stream error" "$elog" 2>/dev/null); ' +
        'case "$c" in ""|*[!0-9]*) ;; *) err="$c";; esac; ' +
        'm=$(grep "stream error" "$elog" 2>/dev/null | tail -n 1 | cut -c1-240); ' +
        'if [ -n "$m" ]; then em="$m"; fi; fi; ' +
        'printf ''now=%s\nhb=%s\nphase=%s\ncategory=%s\ndone=%s\nerr=%s\nerrmsg=%s\n'' "$now" "$hb" "$ph" "$cg" "$dn" "$err" "$em"'
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
    # err=/errmsg= are absent on probes predating the stream-error lines:
    # unknown stays null and the retry verdict skips the poll.
    $streamErrorCount = $null
    if ($map.ContainsKey('err') -and $map['err'] -ne 'MISSING' -and $map['err'] -ne '') {
        $ec = 0
        if ([long]::TryParse($map['err'], [ref]$ec)) { $streamErrorCount = $ec }
    }
    $streamErrorMessage = $null
    if ($map.ContainsKey('errmsg') -and $map['errmsg'] -ne 'MISSING' -and $map['errmsg'] -ne '') {
        $streamErrorMessage = $map['errmsg']
    }
    return [PSCustomObject]@{
        VmNow            = $vmNow
        HeartbeatEpoch   = $heartbeatEpoch
        CurrentPhase     = $currentPhase
        CurrentCategory  = $currentCategory
        DoneExit         = $doneExit
        StreamErrorCount = $streamErrorCount
        StreamErrorMessage = $streamErrorMessage
    }
}

# One poll = one VM read. Returns the sample, the stall verdict, and
# completion together so the loop learns everything from a single call.
function Get-HeartbeatPoll {
    param([string]$Name, [scriptblock]$Executor, [int]$StallThresholdSeconds = 600, $VmStartEpoch = $null)
    $output = Invoke-RuntimeVmOutput -Arguments @('exec', $Name, '--', 'bash', '-c', (Get-HeartbeatPollScript)) -Executor $Executor
    $sample = ConvertFrom-HeartbeatPollOutput -Content $output
    $verdict = Test-HeartbeatStall -VmNow $sample.VmNow -HeartbeatEpoch $sample.HeartbeatEpoch -VmStartEpoch $VmStartEpoch -StallThresholdSeconds $StallThresholdSeconds
    return [PSCustomObject]@{
        VmNow            = $sample.VmNow
        HeartbeatEpoch   = $sample.HeartbeatEpoch
        CurrentPhase     = $sample.CurrentPhase
        CurrentCategory  = $sample.CurrentCategory
        DoneExit         = $sample.DoneExit
        StreamErrorCount = $sample.StreamErrorCount
        StreamErrorMessage = $sample.StreamErrorMessage
        StaleSeconds     = $verdict.StaleSeconds
        IsStalled        = $verdict.IsStalled
        ReferenceEpoch   = $verdict.ReferenceEpoch
        VmStartEpoch     = $verdict.VmStartEpoch
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

# Pure retry verdict for opencode stream errors. Any heartbeat advance
# since the last poll means the agent recovered: re-baseline and never
# fire, so a transient hiccup (reconnect, blip) is invisible. Errors with
# no output since get GraceSeconds to recover before the run fails, so a
# dead stream dies in minutes, not at the full stall threshold. A dropped
# error count (log rotation) re-baselines instead of firing; a null count
# (probe predates err=) keeps state and has no opinion.
function Test-StreamErrorStall {
    param([long]$VmNow, $ErrorCount, [long]$ErrorBaseline = 0, $ErrorFirstSeenEpoch = $null, [bool]$HeartbeatAdvanced = $false, [int]$GraceSeconds = 180)
    $baseline = $ErrorBaseline
    $firstSeen = $ErrorFirstSeenEpoch
    if ($ErrorCount -eq $null) {
        # Unknown count: keep state, never fire.
    }
    elseif ($HeartbeatAdvanced -or $ErrorCount -le $baseline) {
        $baseline = $ErrorCount
        $firstSeen = $null
    }
    else {
        if ($firstSeen -eq $null) { $firstSeen = $VmNow }
        $waited = $VmNow - $firstSeen
        if ($waited -ge $GraceSeconds) {
            $delta = $ErrorCount - $baseline
            return [PSCustomObject]@{
                ErrorBaseline = $baseline
                ErrorFirstSeenEpoch = $firstSeen
                IsStalled = $true
                StallReason = "opencode stream error unrecovered for ${waited}s (grace ${GraceSeconds}s; ${delta} error(s) since last agent output)"
            }
        }
    }
    return [PSCustomObject]@{
        ErrorBaseline = $baseline
        ErrorFirstSeenEpoch = $firstSeen
        IsStalled = $false
        StallReason = $null
    }
}
