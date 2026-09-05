#!/usr/bin/env powershell
#Requires -Version 5.1
<#
.SYNOPSIS
  Launch a factory job for a tracked issue.

.DESCRIPTION
  Accepts a GitHub issue number (e.g. 8) and a target repo URL. It derives a
  unique branch name from the issue number and invokes the existing start-job.ps1
  launcher, passing the issue number through as the run spec.

  The host never reads the issue body; the number alone crosses into the VM,
  where the feature-builder agent fetches the issue body from the GitHub API.

.PARAMETER Issue
  GitHub issue number, e.g. 8. A bare `#8`, `8`, or a full issue URL is accepted.

.PARAMETER RepoUrl
  Target repo HTTPS URL, e.g. https://github.com/owner/name.git.

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

. (Join-Path $PSScriptRoot "lib/JobIntake.ps1")

$IssueNumber = ConvertTo-IssueNumber -Issue $Issue
$Branch = New-IssueBranchName -IssueNumber $IssueNumber

$startJob = Join-Path $RepoRoot "start-job.ps1"
if (-not (Test-Path $startJob)) {
    throw "start-job.ps1 not found at $startJob"
}

$invokeArgs = @{
    RepoUrl = $RepoUrl
    Spec    = $IssueNumber
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
