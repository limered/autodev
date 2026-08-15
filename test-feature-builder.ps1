#!/usr/bin/env powershell
#Requires -Version 5.1
<#
.SYNOPSIS
  End-to-end test of the AI Software Factory feature-builder opencode agent.

.DESCRIPTION
  Launches a disposable Ubuntu 24.04 VM using the existing multipass blueprint,
  injects the bot SSH key, GitHub PAT, opencode API key, and the local .opencode
  configuration, clones limered/autodev, runs opencode headlessly with a tiny
  spec, verifies a branch and pull request were created, and destroys the VM.

.PARAMETER VmName
  Optional multipass VM name. A unique default is generated.

.PARAMETER RepoRoot
  Path to the autodev repository root (defaults to this script's directory).

.PARAMETER Branch
  Optional branch name. A unique default is generated.
#>
[CmdletBinding()]
param(
    [string]$VmName = "factory-feature-test-$(Get-Date -Format 'yyyyMMdd-HHmmss')-$(Get-Random -Maximum 9999)",
    [string]$RepoRoot = $PSScriptRoot,
    [string]$Branch = "factory/test-$(Get-Date -Format 'yyyyMMdd-HHmmss')-$(Get-Random -Maximum 9999)",
    [string]$Model = "opencode-go/kimi-k2.7-code"
)

$ErrorActionPreference = "Stop"

$cloudInit = Join-Path $RepoRoot "infrastructure\multipass\cloud-init.yaml"
$testScript = Join-Path $RepoRoot "infrastructure\multipass\test-feature-builder.sh"
$secretsDir = Join-Path $RepoRoot ".secrets"
$sshKey = Join-Path $secretsDir "bot-github"
$patFile = Join-Path $secretsDir "github-pat.txt"
$apiKeyFile = Join-Path $secretsDir "opencode-api-key.txt"
$opencodeDir = Join-Path $RepoRoot ".opencode"

$spec = "Add a one-line note to AGENTS.md stating this repo is managed by the AI Software Factory."

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
$prUrl = $null

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

    # Transfer the .opencode directory by tarring it, moving the archive into
    # the VM, and extracting it to /tmp/.opencode.
    $opencodeTar = Join-Path $env:TEMP "opencode-config-$VmName.tar.gz"
    Remove-Item -LiteralPath $opencodeTar -ErrorAction SilentlyContinue
    & tar -czf $opencodeTar -C $RepoRoot ".opencode"
    if ($LASTEXITCODE -ne 0) { throw "tar failed creating .opencode archive" }
    Invoke-Multipass transfer $opencodeTar "$($VmName):/tmp/opencode-config.tar.gz"
    Invoke-Multipass exec $VmName '--' bash -c "rm -rf /tmp/.opencode && tar -xzf /tmp/opencode-config.tar.gz -C /tmp"

    Write-Step "Running feature-builder test inside VM (branch: $Branch)"
    Invoke-Multipass exec $VmName '--' env "MODEL=$Model" bash /tmp/test-feature-builder.sh "$Branch" "$spec"

    Write-Step "Verifying branch and pull request on GitHub"
    $pat = (Get-Content -LiteralPath $patFile -Raw).Trim()
    $headers = @{
        Authorization = "Bearer $pat"
        Accept = "application/vnd.github.v3+json"
    }
    $prs = Invoke-RestMethod -Uri "https://api.github.com/repos/limered/autodev/pulls?state=open&head=limered:$Branch" -Headers $headers
    if ($prs.Count -eq 0) {
        throw "No open pull request found for branch $Branch"
    }
    $prUrl = $prs[0].html_url
    Write-Step "Verified PR: $prUrl"
}
catch {
    Write-Host "ERROR: $_" -ForegroundColor Red
    exit 1
}
finally {
    if ($vmCreated) {
        Remove-Vm -Name $VmName
    }
}

Write-Host "`nSUCCESS: feature-builder agent pushed branch $Branch and created $prUrl" -ForegroundColor Green
