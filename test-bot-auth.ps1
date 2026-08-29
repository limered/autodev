#!/usr/bin/env powershell
#Requires -Version 5.1
<#
.SYNOPSIS
  Verifies bot Git authentication inside a fresh AI Software Factory multipass VM.

.DESCRIPTION
  Launches a disposable Ubuntu 24.04 VM using the existing multipass blueprint,
  injects the bot SSH key and GitHub PAT, runs the authentication test script
  inside the VM, reports the result, and destroys the VM.
#>
[CmdletBinding()]
param(
    [string]$VmName = "factory-auth-test-$(Get-Date -Format 'yyyyMMdd-HHmmss')",
    [string]$RepoRoot = $PSScriptRoot
)

$ErrorActionPreference = "Stop"

$cloudInit = Join-Path $RepoRoot "infrastructure\multipass\cloud-init.yaml"
$testScript = Join-Path $RepoRoot "infrastructure\multipass\test-bot-auth.sh"
$secretsDir = Join-Path $RepoRoot ".secrets"
$sshKey = Join-Path $secretsDir "bot-github"
$sshPubKey = Join-Path $secretsDir "bot-github.pub"
$patFile = Join-Path $secretsDir "github-pat.txt"

function Write-Step {
    param([string]$Message)
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Invoke-Multipass {
    param(
        [Parameter(ValueFromRemainingArguments = $true)]
        [string[]]$Arguments
    )
    & multipass @Arguments
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

try {
    if (-not (Test-Path $cloudInit)) { throw "cloud-init not found: $cloudInit" }
    if (-not (Test-Path $testScript)) { throw "test script not found: $testScript" }
    if (-not (Test-Path $sshKey)) { throw "SSH private key not found: $sshKey" }
    if (-not (Test-Path $patFile)) { throw "PAT file not found: $patFile" }

    Write-Step "Launching VM $VmName"
    Invoke-Multipass launch 24.04 --name $VmName --cpus 4 --memory 4G --disk 40G --cloud-init $cloudInit
    $vmCreated = $true

    Write-Step "Waiting for cloud-init provisioning to complete"
    Invoke-Multipass exec $VmName '--' cloud-init status --wait

    Write-Step "Transferring bot SSH key, public key, PAT, and test script into VM"
    Invoke-Multipass transfer $sshKey "$($VmName):/tmp/bot-github"
    Invoke-Multipass transfer $sshPubKey "$($VmName):/tmp/bot-github.pub"
    Invoke-Multipass transfer $patFile "$($VmName):/tmp/github-pat.txt"
    Invoke-Multipass transfer $testScript "$($VmName):/tmp/test-bot-auth.sh"

    Write-Step "Running authentication test inside VM"
    Invoke-Multipass exec $VmName '--' bash /tmp/test-bot-auth.sh

    Write-Step "Authentication test passed"
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
