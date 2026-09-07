#!/usr/bin/env powershell
#Requires -Version 5.1
<#
.SYNOPSIS
  Fire-and-forget reporting of job-run lifecycle events to the factory dashboard.

.DESCRIPTION
  Dot-source this file to get Send-FactoryEvent. Config (backend URL + shared
  token) is read once from .secrets/ under the given RepoRoot:
    .secrets/factory-dashboard-url.txt   e.g. https://factory-dashboard.onrender.com
    .secrets/factory-dashboard-token.txt shared secret matching the backend FACTORY_TOKEN
  If either file is absent, reporting is disabled and every Send-FactoryEvent is
  a silent no-op, so the calling script still runs standalone.

  Send-FactoryEvent NEVER throws and never blocks a real job: a dashboard outage,
  bad URL, or timeout is swallowed. Requests are bounded by a short timeout.
#>

$script:FactoryReportUrl = $null
$script:FactoryReportToken = $null
$script:FactoryReportInit = $false

. (Join-Path $PSScriptRoot "lib/HttpJson.ps1")
. (Join-Path $PSScriptRoot "lib/PhaseSteps.ps1")

function Initialize-FactoryReport {
    param([string]$RepoRoot)
    $script:FactoryReportInit = $true
    try {
        $urlFile = Join-Path $RepoRoot ".secrets\factory-dashboard-url.txt"
        $tokenFile = Join-Path $RepoRoot ".secrets\factory-dashboard-token.txt"
        if ((Test-Path -LiteralPath $urlFile) -and (Test-Path -LiteralPath $tokenFile)) {
            $script:FactoryReportUrl = (Get-Content -LiteralPath $urlFile -Raw).Trim().TrimEnd('/')
            $script:FactoryReportToken = (Get-Content -LiteralPath $tokenFile -Raw).Trim()
        }
    }
    catch {
        # Any config read error simply disables reporting.
        $script:FactoryReportUrl = $null
        $script:FactoryReportToken = $null
    }
    if ($script:FactoryReportUrl) {
        Write-Host "==> Factory reporting enabled -> $script:FactoryReportUrl" -ForegroundColor DarkGray
    }
    else {
        Write-Host "==> Factory reporting disabled (no .secrets/factory-dashboard-*.txt)" -ForegroundColor DarkGray
    }
}

function Send-FactoryEvent {
    param(
        [Parameter(Mandatory = $true)][string]$RunId,
        [Parameter(Mandatory = $true)][string]$Type,
        [hashtable]$Fields
    )
    # No-op if reporting is not configured.
    if (-not $script:FactoryReportUrl -or -not $script:FactoryReportToken) { return }

    try {
        $body = @{
            type = $Type
            at   = (Get-Date).ToUniversalTime().ToString("o")
        }
        if ($Fields) { foreach ($k in $Fields.Keys) { $body[$k] = $Fields[$k] } }

        $bytes = ConvertTo-Utf8JsonBody $body -Depth 10
        $uri = "$script:FactoryReportUrl/runs/$RunId/events"
        Invoke-RestMethod -Method Post -Uri $uri -Body $bytes `
            -ContentType "application/json; charset=utf-8" `
            -Headers @{ "X-Factory-Token" = $script:FactoryReportToken } `
            -TimeoutSec 5 | Out-Null
    }
    catch {
        # Fire-and-forget: a reporting failure must never break a real job.
        Write-Host "WARN: factory report ($Type) failed: $_" -ForegroundColor DarkGray
    }
}

<#
.SYNOPSIS
  Relay per-phase timing + tokens as phase-finished events.

.DESCRIPTION
  V1 pull, not streaming: the caller invokes this once the in-VM work is done
  (or failed) and the VM is still up, before Remove-Vm and before the terminal
  run-finished/run-failed event — steps must land first or the backend's
  terminal guard drops them. For every candidate phase the VM meta sidecar
  (durationMs + status) and NDJSON (token usage) are pulled via multipass-cat;
  phases that never ran are skipped. The model is attached here on the host
  via -ModelLookup (existing Get-AgentModel over the agent frontmatter) and
  a model-less step is never sent — the backend drops it as a no-op. The
  event clock is the host clock (Send-FactoryEvent's default `at`).

  Best-effort throughout: a missing file, an unknown model, or a reporting
  outage skips that step, never the job. Pass -Executor to drive scripted VM
  contents and -Relay to observe payloads in tests; prod callers omit both.
#>
function Send-PhaseFinishedSteps {
    param(
        [Parameter(Mandatory = $true)][string]$RunId,
        [Parameter(Mandatory = $true)][string]$VmName,
        [scriptblock]$ModelLookup,
        [scriptblock]$Executor,
        [scriptblock]$Relay
    )
    foreach ($candidate in (Get-PhaseStepCandidates)) {
        try {
            $meta = Get-VmPhaseMeta -VmName $VmName -Agent $candidate.Agent -Iteration $candidate.Iteration -Executor $Executor
            if ($null -eq $meta) { continue } # phase never ran (or VM gone): skip
            $tokens = Get-VmPhaseTokens -VmName $VmName -Agent $candidate.Agent -Iteration $candidate.Iteration -Executor $Executor
            if ($null -eq $tokens) {
                $tokens = [PSCustomObject]@{ InputTokens = [long]0; OutputTokens = [long]0; Cost = $null }
            }
            $model = $null
            if ($ModelLookup) {
                try { $model = & $ModelLookup $candidate.Agent } catch { $model = $null }
            }
            if (-not $model) { continue } # never send model-less: the backend would drop it
            $fields = @{
                agent        = $candidate.Agent
                iteration    = $candidate.Iteration
                durationMs   = $meta.DurationMs
                inputTokens  = $tokens.InputTokens
                outputTokens = $tokens.OutputTokens
                status       = $meta.Status
                model        = $model
            }
            if ($null -ne $tokens.Cost) { $fields['cost'] = $tokens.Cost }
            if ($Relay) {
                & $Relay $fields
            }
            else {
                Send-FactoryEvent -RunId $RunId -Type "phase-finished" -Fields $fields
            }
        }
        catch {
            Write-Host "WARN: phase-finished relay ($($candidate.Agent)/$($candidate.Iteration)) skipped: $_" -ForegroundColor DarkGray
        }
    }
}
