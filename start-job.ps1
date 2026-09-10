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

. (Join-Path $PSScriptRoot "lib/JobIntake.ps1")
. (Join-Path $PSScriptRoot "lib/AgentsConfig.ps1")

$Repo = ConvertTo-OwnerRepo -RepoUrl $RepoUrl

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

# Seeded categories come from the repo-root agents.json stages map: one stage
# per category in map order, each carrying the model of its first member.
# Models resolve once into a table up front; the lookup closure captures data,
# never a script function, so it survives nested invocation via the call operator.
$seedConfig = Read-AgentsConfig -Path (Join-Path $RepoRoot "agents.json")
$agentModels = @{}
foreach ($member in @($seedConfig | ForEach-Object { @($_.Agents) })) {
    if (-not $agentModels.ContainsKey($member)) {
        $agentModels[$member] = Get-AgentModel -Agent $member -RepoRoot $RepoRoot
    }
}
$modelLookup = { param($agent) $agentModels["$agent"] }.GetNewClosure()
$categoryLookup = { param($agent, $iteration) Get-StepCategory -Agent "$agent" -Iteration ([int]$iteration) -Config $seedConfig }.GetNewClosure()
$stages = @(ConvertTo-SeededStages -Config $seedConfig -ModelLookup $modelLookup)

# The run id is either supplied by the dispatch client or defaulted above to a
# fresh GUID; it is the identity carried on every dashboard event.

# Fire-and-forget dashboard reporting (no-op if .secrets/ config is absent).
. (Join-Path $RepoRoot "factory-report.ps1")
. (Join-Path $RepoRoot "lib/HostVm.ps1")
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
$agentsJson = Join-Path $RepoRoot "agents.json"
$secretsDir = Join-Path $RepoRoot ".secrets"
$patFile = Join-Path $secretsDir "github-pat.txt"
$apiKeyFile = Join-Path $secretsDir "opencode-api-key.txt"
$opencodeDir = Join-Path $RepoRoot ".opencode"

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

    New-VmFromBlueprint -Name $VmName -CloudInit $cloudInit
    $vmCreated = $true

    Write-Step "Transferring PAT, opencode API key, .opencode config, and test script into VM"
    Invoke-Multipass transfer $patFile "$($VmName):/tmp/github-pat.txt"
    Invoke-Multipass transfer $apiKeyFile "$($VmName):/tmp/opencode-api-key.txt"
    Invoke-Multipass transfer $testScript "$($VmName):/tmp/test-feature-builder.sh"
    # The category set travels with the job: the VM builds its category list on
    # startup from this file and reports liveness by category over heartbeat.
    # It was already read for seeding above, so it must exist here.
    Invoke-Multipass transfer $agentsJson "$($VmName):/tmp/agents.json"
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

    # V1 per-step pull (not streaming): the VM is still up and no terminal
    # event has been sent, so every phase-finished lands before run-finished.
    # The model rides from the host-side agent frontmatter via Get-AgentModel.
    Write-Step "Relaying per-phase timing + tokens to the dashboard"
    try {
        Send-PhaseFinishedSteps -RunId $RunId -VmName $VmName -ModelLookup $modelLookup -CategoryLookup $categoryLookup
    }
    catch {
        Write-Host "WARN: per-phase step relay failed: $_" -ForegroundColor Yellow
    }

    Write-Step "Polling GitHub for the pull request on branch $Branch"
    $pat = (Get-Content -LiteralPath $patFile -Raw).Trim()
    $owner = $Repo.Split('/')[0]
    $headers = @{
        Authorization = "Bearer $pat"
        Accept = "application/vnd.github.v3+json"
    }
    # Push+PR creation is done by the time the agent exits, but GitHub's interface can
    # lag a beat. Poll a few times before giving up.
    $poll = {
        param($Repo, $Owner, $Branch)
        Invoke-RestMethod -Uri "https://api.github.com/repos/$Repo/pulls?state=open&head=$($Owner):$Branch" -Headers $headers
    }.GetNewClosure()
    $pr = Wait-ForPullRequest -Repo $Repo -Owner $owner -Branch $Branch -Poll $poll
    $prUrl = $pr.html_url
    Write-Step "Verified PR: $prUrl"
    Send-FactoryEvent -RunId $RunId -Type "pr-verified" -Fields @{ prUrl = $prUrl }
    Send-FactoryEvent -RunId $RunId -Type "run-finished"
}
catch {
    $jobFailed = $true
    $failureReason = "$_"
    Write-Host "ERROR: $failureReason" -ForegroundColor Red
    if (-not $vmCreated -and -not $KeepVmOnFailure) {
        # A launch that times out on cloud-init still leaves the VM behind;
        # purge it best-effort so retries don't pile up orphans.
        try { Invoke-Multipass delete $VmName --purge } catch {}
    }
    if ($vmCreated) {
        # Best-effort step relay before the terminal event: phases that
        # finished before the failure still land (re-emits are safe,
        # last-write-wins); steps must precede run-failed or they are dropped.
        try {
            Send-PhaseFinishedSteps -RunId $RunId -VmName $VmName -ModelLookup $modelLookup -CategoryLookup $categoryLookup
        }
        catch {
            Write-Host "WARN: per-phase step relay failed: $_" -ForegroundColor Yellow
        }
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
