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
  Target repo SSH URL, e.g. git@github.com:owner/name.git.

.PARAMETER Spec
  Feature description / spec the agent should implement.

.PARAMETER Model
  opencode model id. Defaults to opencode-go/kimi-k2.7-code.

.PARAMETER Branch
  Branch the agent should create. A unique default is generated.

.PARAMETER VmName
  multipass VM name. A unique default is generated.

.PARAMETER RepoRoot
  Path to the autodev repo root (source of .opencode, blueprint, secrets).

.PARAMETER KeepVmOnFailure
  Leave the VM running if the job fails, for debugging.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$RepoUrl,
    [Parameter(Mandatory = $true)][string]$Spec,
    [string]$Model = "opencode-go/kimi-k2.7-code",
    [string]$Branch = "factory/job-$(Get-Date -Format 'yyyyMMdd-HHmmss')-$(Get-Random -Maximum 9999)",
    [string]$VmName = "factory-job-$(Get-Date -Format 'yyyyMMdd-HHmmss')-$(Get-Random -Maximum 9999)",
    [string]$RepoRoot = $PSScriptRoot,
    [switch]$KeepVmOnFailure
)

$ErrorActionPreference = "Stop"

# owner/name from git@github.com:owner/name.git  (or https://github.com/owner/name(.git))
if ($RepoUrl -notmatch 'github\.com[:/]([^/]+/[^/]+?)(\.git)?$') {
    throw "Cannot parse owner/name from RepoUrl: $RepoUrl"
}
$Repo = $Matches[1]

$cloudInit = Join-Path $RepoRoot "infrastructure\multipass\cloud-init.yaml"
$testScript = Join-Path $RepoRoot "infrastructure\multipass\test-feature-builder.sh"
$secretsDir = Join-Path $RepoRoot ".secrets"
$sshKey = Join-Path $secretsDir "bot-github"
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

$vmCreated = $false
$jobFailed = $false
$prUrl = $null
$pr = $null

try {
    if (-not (Test-Path $cloudInit)) { throw "cloud-init not found: $cloudInit" }
    if (-not (Test-Path $testScript)) { throw "test script not found: $testScript" }
    if (-not (Test-Path $sshKey)) { throw "SSH private key not found: $sshKey" }
    if (-not (Test-Path $patFile)) { throw "PAT file not found: $patFile" }
    if (-not (Test-Path $apiKeyFile)) { throw "opencode API key file not found: $apiKeyFile" }
    if (-not (Test-Path $opencodeDir)) { throw ".opencode directory not found: $opencodeDir" }

    Write-Step "Launching VM $VmName"
    Invoke-Multipass launch 24.04 --name $VmName --cpus 4 --memory 8G --disk 40G --cloud-init $cloudInit
    $vmCreated = $true

    Write-Step "Waiting for cloud-init provisioning to complete"
    Invoke-Multipass exec $VmName '--' cloud-init status --wait

    Write-Step "Transferring bot SSH key, PAT, opencode API key, .opencode config, and test script into VM"
    Invoke-Multipass transfer $sshKey "$($VmName):/tmp/bot-github"
    $sshPubKey = "$sshKey.pub"
    if (Test-Path $sshPubKey) {
        Invoke-Multipass transfer $sshPubKey "$($VmName):/tmp/bot-github.pub"
    }
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
        & multipass exec $VmName '--' env "MODEL=$Model" bash /tmp/test-feature-builder.sh "$Branch" "$Spec" "$Repo" 2>&1 | ForEach-Object { "$_" }
        if ($LASTEXITCODE -ne 0) {
            throw "multipass exec failed with exit code ${LASTEXITCODE}"
        }
    }
    $vmJob = Start-Job -ScriptBlock $vmJobScript -ArgumentList $VmName, $Model, $Branch, $Spec, $Repo

    $watchScript = Join-Path $PSScriptRoot "watch-heartbeat.ps1"
    & $watchScript -VmName $VmName -Job $vmJob

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
}
catch {
    $jobFailed = $true
    Write-Host "ERROR: $_" -ForegroundColor Red
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
