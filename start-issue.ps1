#!/usr/bin/env powershell
#Requires -Version 5.1
<#
.SYNOPSIS
  Launch a factory job for a tracked issue.

.DESCRIPTION
  Accepts an issue token in the form feature-slug/NN (e.g. vm-phased-agents/04)
  and a target repo URL. It derives a unique branch name from the issue
  reference and invokes the existing start-job.ps1 launcher, passing the issue
  token through as the run spec.

  The host never reads the issue file content; the token alone crosses into the
  VM, where the implement agent resolves .scratch/<feature-slug>/issues/<NN>-*.md.

.PARAMETER Issue
  Issue token: feature-slug/NN.

.PARAMETER RepoUrl
  Target repo SSH URL, e.g. git@github.com:owner/name.git.

.PARAMETER Model
  opencode model id. Defaults to start-job.ps1's default.

.PARAMETER RepoRoot
  Path to the autodev repo root (source of .opencode, blueprint, secrets).
  Defaults to this script's directory.

.PARAMETER KeepVmOnFailure
  Leave the VM running if the job fails, for debugging.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)][string]$Issue,
    [Parameter(Mandatory = $true)][string]$RepoUrl,
    [string]$Model,
    [string]$RepoRoot = $PSScriptRoot,
    [switch]$KeepVmOnFailure
)

$ErrorActionPreference = "Stop"

if ($Issue -notmatch '^[\w-]+/\d+$') {
    throw "Issue token must be feature-slug/NN (e.g. vm-phased-agents/04); got: $Issue"
}

$issueParts = $Issue.Split('/')
$issueBranchId = "$($issueParts[0])-$($issueParts[1])"
$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$randomSuffix = Get-Random -Maximum 9999
$Branch = "factory/$issueBranchId-$timestamp-$randomSuffix"

$startJob = Join-Path $RepoRoot "start-job.ps1"
if (-not (Test-Path $startJob)) {
    throw "start-job.ps1 not found at $startJob"
}

$invokeArgs = @{
    RepoUrl = $RepoUrl
    Spec    = $Issue
    Branch  = $Branch
    RepoRoot = $RepoRoot
}
if ($PSBoundParameters.ContainsKey('Model')) {
    $invokeArgs['Model'] = $Model
}
if ($KeepVmOnFailure) {
    $invokeArgs['KeepVmOnFailure'] = $true
}

& $startJob @invokeArgs
