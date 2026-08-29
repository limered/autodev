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

        $json = $body | ConvertTo-Json -Compress -Depth 10
        $uri = "$script:FactoryReportUrl/runs/$RunId/events"
        Invoke-RestMethod -Method Post -Uri $uri -Body $json `
            -ContentType "application/json" `
            -Headers @{ "X-Factory-Token" = $script:FactoryReportToken } `
            -TimeoutSec 5 | Out-Null
    }
    catch {
        # Fire-and-forget: a reporting failure must never break a real job.
        Write-Host "WARN: factory report ($Type) failed: $_" -ForegroundColor DarkGray
    }
}
