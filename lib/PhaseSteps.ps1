#!/usr/bin/env powershell
#Requires -Version 5.1
<#
.SYNOPSIS
  Per-phase timing + token relay behind one Seam (lib/PhaseSteps.ps1).

.DESCRIPTION
  Dot-source this file — no side effects on load:
    . (Join-Path $RepoRoot "lib/PhaseSteps.ps1")
  The VM writes one NDJSON file per agent phase
  (/tmp/phase-<agent>-<iteration>.jsonl, opencode `run --format json` output)
  plus a meta sidecar (/tmp/phase-<agent>-<iteration>.meta.json with
  durationMs + status). VM reads hide behind an injectable -Executor Seam
  (passed through to Invoke-MultipassOutput) so Pester drives scripted file
  contents without a live VM. Prod callers omit -Executor.

  Pure converters (ConvertTo-PhaseTokens, ConvertTo-PhaseMeta,
  Get-PhaseStepCandidates) take strings and need no VM at all.
  Get-PhaseTurnUsage is the single place that decides which opencode shape
  won for tokens and for cost on one step_finish turn.
#>

. (Join-Path $PSScriptRoot "HostVm.ps1")

# The v1 relay order: every phase in execution order, one entry per loop
# iteration. Iteration is 0 for single-run phases, 1..3 for quality-loop
# scan/fix passes; the phase-4 test re-run is test-runner/1 so it lands as
# its own row instead of overwriting the phase-2 step (the backend keys
# steps on agent + iteration, last-write-wins).
function Get-PhaseStepCandidates {
    return @(
        [PSCustomObject]@{ Agent = 'feature-builder'; Iteration = 0 },
        [PSCustomObject]@{ Agent = 'test-runner'; Iteration = 0 },
        [PSCustomObject]@{ Agent = 'static-analysis'; Iteration = 1 },
        [PSCustomObject]@{ Agent = 'feature-builder'; Iteration = 1 },
        [PSCustomObject]@{ Agent = 'static-analysis'; Iteration = 2 },
        [PSCustomObject]@{ Agent = 'feature-builder'; Iteration = 2 },
        [PSCustomObject]@{ Agent = 'static-analysis'; Iteration = 3 },
        [PSCustomObject]@{ Agent = 'feature-builder'; Iteration = 3 },
        [PSCustomObject]@{ Agent = 'test-runner'; Iteration = 1 },
        [PSCustomObject]@{ Agent = 'agentic-review'; Iteration = 0 },
        [PSCustomObject]@{ Agent = 'pr-author'; Iteration = 0 }
    )
}

# First present numeric field wins; absent or non-numeric reads as 0.
function Get-PhaseNumericField {
    param($Object, [string[]]$Names)
    foreach ($name in $Names) {
        $prop = $Object.PSObject.Properties[$name]
        if ($null -eq $prop) { continue }
        try { return [long]$prop.Value } catch { continue }
    }
    return 0
}

# Decides which shape won for one step_finish turn. TokensShape is one of
# usage, step.usage, event, unknown; CostShape is one of usage, event, none.
# A turn counts as parsed when either side won, skipped when neither did.
function Get-PhaseTurnUsage {
    param($Turn)
    $inputNames = @('inputTokens', 'input_tokens', 'input')
    $outputNames = @('outputTokens', 'output_tokens', 'output')
    $holder = $null
    $tokensShape = 'unknown'
    if ($null -ne $Turn.usage) {
        $holder = $Turn.usage
        $tokensShape = 'usage'
    }
    elseif ($null -ne $Turn.step -and $null -ne $Turn.step.usage) {
        $holder = $Turn.step.usage
        $tokensShape = 'step.usage'
    }
    else {
        foreach ($name in ($inputNames + $outputNames)) {
            if ($null -ne $Turn.PSObject.Properties[$name]) {
                $holder = $Turn
                $tokensShape = 'event'
                break
            }
        }
    }
    $inputTokens = [long]0
    $outputTokens = [long]0
    if ($null -ne $holder) {
        $inputTokens = Get-PhaseNumericField $holder $inputNames
        $outputTokens = Get-PhaseNumericField $holder $outputNames
    }
    $cost = $null
    $costShape = 'none'
    if ($null -ne $holder -and -not [object]::ReferenceEquals($holder, $Turn)) {
        $costProp = $holder.PSObject.Properties['cost']
        if ($null -ne $costProp -and $null -ne $costProp.Value) {
            try { $cost = [double]$costProp.Value; $costShape = 'usage' } catch { }
        }
    }
    if ($costShape -eq 'none' -and $null -ne $Turn) {
        $costProp = $Turn.PSObject.Properties['cost']
        if ($null -ne $costProp -and $null -ne $costProp.Value) {
            try { $cost = [double]$costProp.Value; $costShape = 'event' } catch { }
        }
    }
    return [PSCustomObject]@{
        InputTokens  = $inputTokens
        OutputTokens = $outputTokens
        Cost         = $cost
        TokensShape  = $tokensShape
        CostShape    = $costShape
    }
}

# Sums per-phase token usage from opencode `--format json` NDJSON: one
# `step_finish` object per turn, usage carried on the event (or a nested
# step object, or the event itself for older shapes). Anything that is not
# a step_finish JSON object — human stderr lines, progress lines, truncated
# tails — is skipped, so a noisy file still yields its usage.
# TurnCount is the step_finish turns seen; ParsedTurnCount won a shape,
# SkippedTurnCount won neither, so callers tell parsed zeros apart from
# drift to an unrecognized shape.
function ConvertTo-PhaseTokens {
    param([string]$Content)
    $result = [PSCustomObject]@{ InputTokens = [long]0; OutputTokens = [long]0; Cost = $null; TurnCount = 0; ParsedTurnCount = 0; SkippedTurnCount = 0 }
    if ([string]::IsNullOrWhiteSpace($Content)) { return $result }
    $hasCost = $false
    $cost = 0.0
    foreach ($line in ($Content -split "`r?`n")) {
        $trimmed = "$line".Trim()
        if (-not $trimmed.StartsWith('{')) { continue }
        $obj = $null
        try { $obj = $trimmed | ConvertFrom-Json -ErrorAction Stop } catch { continue }
        if ($null -eq $obj -or $obj.type -ne 'step_finish') { continue }
        $result.TurnCount++
        $turn = Get-PhaseTurnUsage $obj
        $result.InputTokens += $turn.InputTokens
        $result.OutputTokens += $turn.OutputTokens
        if ($turn.TokensShape -ne 'unknown' -or $turn.CostShape -ne 'none') {
            $result.ParsedTurnCount++
        }
        else {
            $result.SkippedTurnCount++
        }
        if ($turn.CostShape -ne 'none') {
            $cost += $turn.Cost
            $hasCost = $true
        }
    }
    if ($hasCost) { $result.Cost = $cost }
    return $result
}

# Reads the in-VM meta sidecar written by run_agent_phase. Returns $null when
# the content is not a meta object, so callers skip phases that never ran.
function ConvertTo-PhaseMeta {
    param([string]$Content)
    $obj = $null
    try { $obj = $Content | ConvertFrom-Json -ErrorAction Stop } catch { return $null }
    if ($null -eq $obj -or $null -eq $obj.durationMs) { return $null }
    $durationMs = 0
    try { $durationMs = [long]$obj.durationMs } catch { return $null }
    $status = 'done'
    if ($obj.status -eq 'failed') { $status = 'failed' }
    return [PSCustomObject]@{ DurationMs = $durationMs; Status = $status }
}

# Multipass-cat seam: pulls one phase's NDJSON and sums its usage. Returns
# $null when the file is absent (phase never ran) or unreadable, so the
# relay skips it.
function Get-VmPhaseTokens {
    param([string]$VmName, [string]$Agent, [int]$Iteration, [scriptblock]$Executor)
    $path = "/tmp/phase-$Agent-$Iteration.jsonl"
    try {
        $content = Invoke-MultipassOutput -Arguments @('exec', $VmName, '--', 'cat', $path) -Executor $Executor -TimeoutSeconds 15
    }
    catch {
        return $null
    }
    if ([string]::IsNullOrWhiteSpace("$content")) { return $null }
    return ConvertTo-PhaseTokens -Content $content
}

# Multipass-cat seam for the meta sidecar. Returns $null when the file is
# absent or unreadable.
function Get-VmPhaseMeta {
    param([string]$VmName, [string]$Agent, [int]$Iteration, [scriptblock]$Executor)
    $path = "/tmp/phase-$Agent-$Iteration.meta.json"
    try {
        $content = Invoke-MultipassOutput -Arguments @('exec', $VmName, '--', 'cat', $path) -Executor $Executor -TimeoutSeconds 15
    }
    catch {
        return $null
    }
    if ([string]::IsNullOrWhiteSpace("$content")) { return $null }
    return ConvertTo-PhaseMeta -Content $content
}
