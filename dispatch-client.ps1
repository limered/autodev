#!/usr/bin/env powershell
#Requires -Version 5.1
<#
.SYNOPSIS
  Foreground dispatch client that syncs eligible issues and runs the next queued job.

.DESCRIPTION
  Runs an infinite loop on the host PC:

  1. On a slow cadence, query GitHub for each configured repo's issues labeled
     'ready-for-agent' and sync them to the backend via PUT /issues/{repo}.
  2. On a fast cadence, call POST /queue/claim-next. When a claim is returned,
     invoke start-job.ps1 synchronously with the claimed run id, repo URL, and
     spec. The blocking call enforces the one-at-a-time rule; when it finishes,
     the loop resumes.

  Configuration is read from a local, gitignored JSON file (default
  dispatch-client.config.json next to this script). The GitHub PAT and backend
  shared token are read from the existing .secrets location, never from config.

.PARAMETER RepoRoot
  Path to the slop-factory repo root (source of start-job.ps1 and .secrets).
  Defaults to this script's directory.

.PARAMETER ConfigPath
  Path to the local dispatch client config JSON. Defaults to
  <RepoRoot>\dispatch-client.config.json.
#>
[CmdletBinding()]
param(
    [string]$RepoRoot = $PSScriptRoot,
    [string]$ConfigPath = (Join-Path $PSScriptRoot "dispatch-client.config.json"),
    [ValidateSet('multipass', 'container')][string]$Isolator = $(if ($env:OS -eq 'Windows_NT') { 'multipass' } else { 'container' })
)

$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "lib/JobIntake.ps1")
. (Join-Path $PSScriptRoot "lib/HttpJson.ps1")

function Read-Config {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Config file not found: $Path"
    }
    try {
        return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    }
    catch {
        throw "Failed to parse config file ${Path}: $_"
    }
}

function Get-GitHubIssues {
    param(
        [string]$OwnerRepo,
        [string]$Token
    )
    $uri = "https://api.github.com/repos/$OwnerRepo/issues?state=open&labels=ready-for-agent"
    $headers = @{
        Authorization = "Bearer $Token"
        Accept = "application/vnd.github.v3+json"
        "User-Agent" = "slop-factory-dispatch-client"
    }
    $response = Invoke-RestMethod -Uri $uri -Headers $headers -TimeoutSec 30
    # Exclude pull requests; the GitHub issues endpoint returns both.
    return $response | Where-Object { -not $_.PSObject.Properties['pull_request'] }
}

function Sync-IssuesToBackend {
    param(
        [string]$BackendUrl,
        [string]$FactoryToken,
        [string]$OwnerRepo,
        [array]$Issues
    )
    $snapshot = ConvertTo-IssueSnapshot -Issues $Issues
    # @(...) forces array output: ConvertTo-Json collapses a single item to a
    # bare object and an empty set to null, both of which fail the backend's
    # List<IssueSnapshot> binding (400). Where-Object drops the $null that
    # ForEach-Object yields for an empty issue list, so zero issues sends [].
    $body = ConvertTo-Utf8JsonBody @($snapshot | Where-Object { $_ }) -AsArray
    $uri = "$BackendUrl/issues/$OwnerRepo"
    Invoke-JsonRequest -Method Put -Uri $uri -Body $body `
        -Headers @{ "X-Factory-Token" = $FactoryToken } | Out-Null
}

function Invoke-ClaimNext {
    param(
        [string]$BackendUrl,
        [string]$FactoryToken
    )
    $uri = "$BackendUrl/queue/claim-next"
    try {
        $response = Invoke-RestMethod -Method Post -Uri $uri `
            -ContentType "application/json" `
            -Headers @{ "X-Factory-Token" = $FactoryToken } `
            -TimeoutSec 30
        return $response
    }
    catch {
        # A 204 NoContent (or any error) means nothing to claim.
        return $null
    }
}

function Start-ClaimedJob {
    param(
        [object]$Claim,
        [string]$RepoRoot,
        [string]$Isolator = 'multipass'
    )
    $startJob = Join-Path $RepoRoot "start-job.ps1"
    if (-not (Test-Path $startJob)) {
        throw "start-job.ps1 not found at $startJob"
    }

    & $startJob -RunId $Claim.runId -RepoUrl $Claim.repoUrl -Spec $Claim.spec -RepoRoot $RepoRoot -Isolator $Isolator `
        -CatalogJson "$($Claim.catalogContent)" -CatalogSha "$($Claim.catalogSha)"
    if ($LASTEXITCODE -ne 0) {
        throw "start-job.ps1 failed with exit code $LASTEXITCODE"
    }
}

# -----------------------------------------------------------------------------
# Bootstrap
# -----------------------------------------------------------------------------
$secretsDir = Join-Path $RepoRoot ".secrets"
$githubToken = Read-SecretFile (Join-Path $secretsDir "github-pat.txt")
$factoryToken = Read-SecretFile (Join-Path $secretsDir "factory-dashboard-token.txt")
$config = Read-Config $ConfigPath

if (-not $githubToken) { throw "GitHub PAT not found in .secrets/github-pat.txt" }
if (-not $factoryToken) { throw "Factory token not found in .secrets/factory-dashboard-token.txt" }
if (-not $config.backendUrl) { throw "backendUrl is required in $ConfigPath" }
if (-not $config.repos -or $config.repos.Count -eq 0) { throw "At least one repo is required in $ConfigPath" }

$backendUrl = $config.backendUrl.Trim().TrimEnd('/')
$syncIntervalSeconds = if ($config.syncIntervalSeconds) { $config.syncIntervalSeconds } else { 60 }
$claimIntervalSeconds = if ($config.claimIntervalSeconds) { $config.claimIntervalSeconds } else { 5 }

Write-Host "==> Dispatch client starting" -ForegroundColor Cyan
Write-Host "    Backend: $backendUrl" -ForegroundColor DarkGray
$repoNames = @($config.repos | ForEach-Object { if ($_.ownerRepo) { $_.ownerRepo } else { "$_" } })
Write-Host "    Repos: $($repoNames -join ', ')" -ForegroundColor DarkGray
Write-Host "    Isolator: $Isolator" -ForegroundColor DarkGray
Write-Host "    Sync interval: ${syncIntervalSeconds}s" -ForegroundColor DarkGray
Write-Host "    Claim interval: ${claimIntervalSeconds}s" -ForegroundColor DarkGray

$lastSync = [DateTime]::MinValue

while ($true) {
    # Slow cadence: sync issues from GitHub to backend.
    if ((Get-Date) - $lastSync -ge [TimeSpan]::FromSeconds($syncIntervalSeconds)) {
        foreach ($repoEntry in $config.repos) {
            $ownerRepo = $repoEntry.ownerRepo
            if (-not $ownerRepo) { continue }
            try {
                Write-Host "==> Syncing issues for $ownerRepo" -ForegroundColor Cyan
                $issues = Get-GitHubIssues -OwnerRepo $ownerRepo -Token $githubToken
                Sync-IssuesToBackend -BackendUrl $backendUrl -FactoryToken $factoryToken -OwnerRepo $ownerRepo -Issues $issues
                Write-Host "    Synced $($issues.Count) issue(s)" -ForegroundColor DarkGray
            }
            catch {
                # Invoke-RestMethod hides the backend's error body; read it so a 400
                # names the offending field (e.g. a null non-nullable property).
                $detail = if ($_.ErrorDetails.Message) { $_.ErrorDetails.Message } else { "$_" }
                $resp = $_.Exception.Response
                if (-not $detail -and $resp) {
                    try {
                        $stream = $resp.GetResponseStream()
                        $reader = New-Object System.IO.StreamReader($stream)
                        $detail = $reader.ReadToEnd()
                    }
                    catch {}
                }
                Write-Warning "Issue sync failed for ${ownerRepo}: $detail"
            }
        }
        $lastSync = Get-Date
    }

    # Fast cadence: claim and run the next queued item.
    try {
        $claim = Invoke-ClaimNext -BackendUrl $backendUrl -FactoryToken $factoryToken
        if ($claim) {
            Write-Host "==> Claimed run $($claim.runId); starting job" -ForegroundColor Cyan
            Start-ClaimedJob -Claim $claim -RepoRoot $RepoRoot -Isolator $Isolator
            Write-Host "==> Job $($claim.runId) finished" -ForegroundColor Cyan
        }
    }
    catch {
        Write-Warning "Claim/run phase failed: $_"
    }

    Start-Sleep -Seconds $claimIntervalSeconds
}
