#!/usr/bin/env powershell
#Requires -Version 5.1
<#
.SYNOPSIS
  Run an opencode feature-builder job against a GitHub repo in a disposable VM.

.DESCRIPTION
  Launches a fresh multipass VM from the project blueprint, provisions it with
  the bot SSH key, GitHub PAT, opencode API key, and the local .opencode config,
  clones the target repo, runs the opencode agent headlessly against the given
  spec, polls GitHub for the resulting branch/PR, prints the PR URL, and destroys
  the VM. Emits a result object; exits 1 on failure.

.PARAMETER RepoUrl
  Target repo HTTPS URL, e.g. https://github.com/owner/name.git.

.PARAMETER Spec
  Feature description / spec the agent should implement.

.PARAMETER Model
  opencode model id. Defaults to the default_agent's model in .opencode/opencode.json.

.PARAMETER Branch
  Branch the agent should create. A unique default is generated.

.PARAMETER VmName
  multipass VM name. A unique default is generated.

.PARAMETER RepoRoot
  Path to the autodev repo root (source of .opencode, blueprint, secrets).

.PARAMETER RunId
  Optional run identity used for all lifecycle events. Defaults to a fresh GUID.

.PARAMETER KeepVmOnFailure
  Leave the VM running if the job fails, for debugging.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$RepoUrl,
    [Parameter(Mandatory = $true)][string]$Spec,
    [string]$Model,
    [string]$Branch = "factory/job-$(Get-Date -Format 'yyyyMMdd-HHmmss')-$(Get-Random -Maximum 9999)",
    [string]$VmName = "factory-job-$(Get-Date -Format 'yyyyMMdd-HHmmss')-$(Get-Random -Maximum 9999)",
    [string]$RepoRoot = $PSScriptRoot,
    [string]$RunId = [guid]::NewGuid().ToString(),
    [switch]$KeepVmOnFailure
)

$ErrorActionPreference = "Stop"

# owner/name from git@github.com:owner/name.git  (or https://github.com/owner/name(.git))
if ($RepoUrl -notmatch 'github\.com[:/]([^/]+/[^/]+?)(\.git)?$') {
    throw "Cannot parse owner/name from RepoUrl: $RepoUrl"
}
$Repo = $Matches[1]

# Read an agent's model from its definition frontmatter (.opencode/agents/<name>.md,
# the `model:` field) — the single source of truth opencode headless honors.
# opencode.json no longer carries per-agent models.
function Get-AgentModel {
    param([string]$Agent, [string]$RepoRoot)
    $agentPath = Join-Path $RepoRoot ".opencode\agents\$Agent.md"
    if (-not (Test-Path -LiteralPath $agentPath)) { throw "Agent definition not found: $agentPath" }
    $match = (Get-Content -LiteralPath $agentPath -Raw | Select-String -Pattern '(?m)^model:\s*(\S+)')
    $model = $match.Matches.Groups[1].Value
    if (-not $model) { throw "No model found in frontmatter of $agentPath" }
    return $model
}

# Default the run-level model to the default_agent's model.
if (-not $Model) {
    $configPath = Join-Path $RepoRoot ".opencode\opencode.json"
    $defaultAgent = (Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json).default_agent
    $Model = Get-AgentModel -Agent $defaultAgent -RepoRoot $RepoRoot
}

# ponytail: the phase order is hardcoded here and is expected to change under the
# in-progress agent split/refactor; update this list when the phases settle.
$phaseOrder = @('feature-builder', 'test-runner', 'pr-author')
$stages = @(foreach ($agent in $phaseOrder) {
    [ordered]@{ agent = $agent; model = (Get-AgentModel -Agent $agent -RepoRoot $RepoRoot) }
})

# The run id is either supplied by the dispatch client or defaulted above to a
# fresh GUID; it is the identity carried on every dashboard event.

# Fire-and-forget dashboard reporting (no-op if .secrets/ config is absent).
. (Join-Path $RepoRoot "factory-report.ps1")
Initialize-FactoryReport -RepoRoot $RepoRoot
Send-FactoryEvent -RunId $RunId -Type "run-started" -Fields @{
    repo   = $Repo
    branch = $Branch
    spec   = $Spec
    model  = $Model
    stages = $stages
}

$cloudInit = Join-Path $RepoRoot "infrastructure\multipass\cloud-init.yaml"
$testScript = Join-Path $RepoRoot "infrastructure\multipass\test-feature-builder.sh"
$secretsDir = Join-Path $RepoRoot ".secrets"
$patFile = Join-Path $secretsDir "github-pat.txt"
$apiKeyFile = Join-Path $secretsDir "opencode-api-key.txt"
$opencodeDir = Join-Path $RepoRoot ".opencode"

function Write-Step {
    param([string]$Message)
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Invoke-Multipass {
    param(
        [Parameter(ValueFromRemainingArguments = $true)]
        [string[]]$Arguments
    )
    # $ErrorActionPreference=Stop turns native stderr writes (e.g. git's benign
    # "Cloning into..." on stderr) into terminating errors. Suspend it for the
    # native call and gate purely on the real exit code.
    $prev = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    & multipass @Arguments 2>&1 | ForEach-Object { "$_" }
    $ErrorActionPreference = $prev
    if ($LASTEXITCODE -ne 0) {
        throw "multipass failed with exit code ${LASTEXITCODE}: multipass $Arguments"
    }
}

function Remove-Vm {
    param([string]$Name)
    Write-Step "Destroying VM $Name"
    multipass delete $Name --purge 2>&1 | Out-Null
}

function Invoke-VmCapture {
    # Bounded in-VM command; a wedged VM cannot re-hang the collector. Returns
    # captured text (stdout+stderr) or a "<unavailable: ...>" marker on failure.
    # The in-VM `timeout` bounds a slow command, but if `multipass exec` itself
    # never returns (VM wedged / tearing down) that's unbounded — so also cap the
    # host side with a job we abandon after HostTimeoutSeconds.
    param([string]$Name, [string]$Command, [int]$TimeoutSeconds = 15, [int]$HostTimeoutSeconds = 25)
    $job = Start-Job -ScriptBlock {
        param($Name, $Command, $TimeoutSeconds)
        $out = & multipass exec $Name -- timeout $TimeoutSeconds bash -c $Command 2>&1 | ForEach-Object { "$_" }
        [PSCustomObject]@{ Code = $LASTEXITCODE; Out = ($out -join "`n") }
    } -ArgumentList $Name, $Command, $TimeoutSeconds
    if (-not (Wait-Job -Job $job -Timeout $HostTimeoutSeconds)) {
        Stop-Job -Job $job -ErrorAction SilentlyContinue
        Remove-Job -Job $job -Force -ErrorAction SilentlyContinue
        return "<unavailable: host exec timed out after ${HostTimeoutSeconds}s>"
    }
    $r = Receive-Job -Job $job
    Remove-Job -Job $job -Force -ErrorAction SilentlyContinue
    if ($null -eq $r) { return "<unavailable: no result>" }
    if ($r.Code -ne 0) { return "<unavailable: exit $($r.Code)> $($r.Out)" }
    return $r.Out
}

function Save-FreezeSnapshot {
    # Collect a debug manifest from the still-alive VM before teardown.
    param([string]$Name, [hashtable]$JobParams, [string]$RepoRoot)
    Write-Step "Capturing freeze snapshot from $Name before teardown"
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $dir = Join-Path $RepoRoot ".scratch\freezes\$Name-$stamp"
    New-Item -ItemType Directory -Path $dir -Force | Out-Null

    $manifest = [ordered]@{
        vm             = $Name
        capturedAtUtc  = (Get-Date).ToUniversalTime().ToString("o")
        jobParams      = $JobParams
        markerMtime    = (Invoke-VmCapture $Name 'stat -c %y /tmp/heartbeat 2>/dev/null || echo missing')
        agentLogTail   = (Invoke-VmCapture $Name 'f=$(ls -t ~/.local/share/opencode/log/*.log 2>/dev/null | head -1); [ -n "$f" ] && tail -n 200 "$f" || echo "<no opencode log>"')
        psAux          = (Invoke-VmCapture $Name 'ps aux')
        freeM          = (Invoke-VmCapture $Name 'free -m')
        dfH            = (Invoke-VmCapture $Name 'df -h')
    }

    $path = Join-Path $dir "manifest.json"
    $manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $path -Encoding UTF8
    Write-Step "Freeze snapshot written to $path"
    return $path
}

$vmCreated = $false
$jobFailed = $false
$prUrl = $null
$pr = $null

try {
    if (-not (Test-Path $cloudInit)) { throw "cloud-init not found: $cloudInit" }
    if (-not (Test-Path $testScript)) { throw "test script not found: $testScript" }
    if (-not (Test-Path $patFile)) { throw "PAT file not found: $patFile" }
    if (-not (Test-Path $apiKeyFile)) { throw "opencode API key file not found: $apiKeyFile" }
    if (-not (Test-Path $opencodeDir)) { throw ".opencode directory not found: $opencodeDir" }

    Write-Step "Launching VM $VmName"
    Invoke-Multipass launch 24.04 --name $VmName --cpus 4 --memory 4G --disk 40G --cloud-init $cloudInit
    $vmCreated = $true

    Write-Step "Waiting for cloud-init provisioning to complete"
    Invoke-Multipass exec $VmName '--' cloud-init status --wait

    Write-Step "Transferring PAT, opencode API key, .opencode config, and test script into VM"
    Invoke-Multipass transfer $patFile "$($VmName):/tmp/github-pat.txt"
    Invoke-Multipass transfer $apiKeyFile "$($VmName):/tmp/opencode-api-key.txt"
    Invoke-Multipass transfer $testScript "$($VmName):/tmp/test-feature-builder.sh"
    # The Windows working copy may be CRLF; strip CR so bash doesn't choke on
    # "set -euo pipefail\r" and friends.
    Invoke-Multipass exec $VmName '--' bash -c "sed -i 's/\r`$//' /tmp/test-feature-builder.sh"

    # Transfer the .opencode directory by tarring it, moving the archive into
    # the VM, and extracting it to /tmp/.opencode.
    $opencodeTar = Join-Path $env:TEMP "opencode-config-$VmName.tar.gz"
    Remove-Item -LiteralPath $opencodeTar -ErrorAction SilentlyContinue
    & tar -czf $opencodeTar -C $RepoRoot ".opencode"
    if ($LASTEXITCODE -ne 0) { throw "tar failed creating .opencode archive" }
    Invoke-Multipass transfer $opencodeTar "$($VmName):/tmp/opencode-config.tar.gz"
    Invoke-Multipass exec $VmName '--' bash -c "rm -rf /tmp/.opencode && tar -xzf /tmp/opencode-config.tar.gz -C /tmp"

    Write-Step "Running feature-builder job inside VM (repo: $Repo, branch: $Branch)"
    $vmJobScript = {
        param($VmName, $Model, $Branch, $Spec, $Repo)
        $ErrorActionPreference = "Continue"
        # Base64 the issue token so spaces/quotes/newlines never reach the env/exec arg
        # boundary (a bare word like "to" was being parsed as the command -> exit 127).
        # The value now carries a GitHub issue number, not a spec body.
        $specB64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($Spec))
        & multipass exec $VmName '--' env "MODEL=$Model" "ISSUE_B64=$specB64" bash /tmp/test-feature-builder.sh "$Branch" "$Repo" 2>&1 | ForEach-Object { "$_" }
        if ($LASTEXITCODE -ne 0) {
            throw "multipass exec failed with exit code ${LASTEXITCODE}"
        }
    }
    $vmJob = Start-Job -ScriptBlock $vmJobScript -ArgumentList $VmName, $Model, $Branch, $Spec, $Repo

    Send-FactoryEvent -RunId $RunId -Type "agent-started" -Fields @{ vmName = $VmName }

    $watchScript = Join-Path $PSScriptRoot "watch-heartbeat.ps1"
    # 10 min, not the 5 min default: a long silent model completion (no interim
    # output line -> no heartbeat touch) was false-killing legit mid-work agents.
    # A dead model never recovers, so the extra 5 min only costs a rare real stall.
    & $watchScript -VmName $VmName -Job $vmJob -RunId $RunId -RepoRoot $RepoRoot -StallThresholdSeconds 600

    Write-Step "Polling GitHub for the pull request on branch $Branch"
    $pat = (Get-Content -LiteralPath $patFile -Raw).Trim()
    $owner = $Repo.Split('/')[0]
    $headers = @{
        Authorization = "Bearer $pat"
        Accept = "application/vnd.github.v3+json"
    }
    # Push+PR creation is done by the time the agent exits, but GitHub's API can
    # lag a beat. Poll a few times before giving up.
    for ($i = 0; $i -lt 10; $i++) {
        $prs = Invoke-RestMethod -Uri "https://api.github.com/repos/$Repo/pulls?state=open&head=$($owner):$Branch" -Headers $headers
        if ($prs.Count -gt 0) { $pr = $prs[0]; break }
        Start-Sleep -Seconds 3
    }
    if (-not $pr) { throw "No open pull request found for branch $Branch" }
    $prUrl = $pr.html_url
    Write-Step "Verified PR: $prUrl"
    Send-FactoryEvent -RunId $RunId -Type "pr-verified" -Fields @{ prUrl = $prUrl }
    Send-FactoryEvent -RunId $RunId -Type "run-finished"
}
catch {
    $jobFailed = $true
    $failureReason = "$_"
    Write-Host "ERROR: $failureReason" -ForegroundColor Red
    if ($vmCreated) {
        try {
            $freezePath = Save-FreezeSnapshot -Name $VmName -RepoRoot $RepoRoot -JobParams @{
                repo   = $Repo
                branch = $Branch
                spec   = $Spec
                model  = $Model
            }
            # Emit freeze-captured before run-failed so the freeze info lands
            # first (ordering is defensive only; the backend tolerates any order).
            if ($freezePath) {
                Send-FactoryEvent -RunId $RunId -Type "freeze-captured" -Fields @{ freezeLocalPath = $freezePath }
            }
        }
        catch {
            Write-Host "WARN: freeze snapshot capture failed: $_" -ForegroundColor Yellow
        }
    }
    Send-FactoryEvent -RunId $RunId -Type "run-failed" -Fields @{ failureReason = $failureReason }
}
finally {
    if ($vmCreated -and -not ($jobFailed -and $KeepVmOnFailure)) {
        Remove-Vm -Name $VmName
    }
    elseif ($vmCreated) {
        Write-Step "Keeping VM $VmName for debugging (job failed, -KeepVmOnFailure set)"
    }
}

if ($jobFailed) { exit 1 }

Write-Host $prUrl
[PSCustomObject]@{
    Repo     = $Repo
    Branch   = $Branch
    PrUrl    = $prUrl
    PrNumber = $pr.number
}
