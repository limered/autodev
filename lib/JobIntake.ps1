#!/usr/bin/env powershell
#Requires -Version 5.1
<#
.SYNOPSIS
  Pure Job-intake mapping behind one Seam (lib/JobIntake.ps1).

.DESCRIPTION
  Dot-source this file — no side effects on load:
    . (Join-Path $RepoRoot "lib/JobIntake.ps1")
  All functions are pure (no network, no VM, no clock except defaults) so
  Pester checks run without secrets or multipass.
#>

function ConvertTo-IssueSnapshot {
    param([array]$Issues)
    # Pipeline output (enumerated): callers needing an array wrap with @().
    foreach ($i in $Issues) {
        if ($null -eq $i) { continue }
        @{
            id        = $i.id
            number    = $i.number
            title     = $i.title
            htmlUrl   = $i.html_url
            labels    = @($i.labels | ForEach-Object { $_.name })
            body      = $i.body
            state     = $i.state
            updatedAt = $i.updated_at
        }
    }
}

function Get-StaleSeconds {
    param([long]$VmNow, [long]$ReferenceEpoch)
    return ($VmNow - $ReferenceEpoch)
}

function ConvertTo-OwnerRepo {
    param([string]$RepoUrl)
    if ($RepoUrl -notmatch 'github\.com[:/]([^/]+/[^/]+?)(\.git)?$') {
        throw "Cannot parse owner/name from RepoUrl: $RepoUrl"
    }
    return $Matches[1]
}

function Wait-ForPullRequest {
    param([string]$Repo, [string]$Owner, [string]$Branch, [scriptblock]$Poll, [int]$MaxAttempts = 10, [int]$SleepSeconds = 3)
    for ($i = 0; $i -lt $MaxAttempts; $i++) {
        $prs = @(& $Poll $Repo $Owner $Branch)
        if ($prs.Count -gt 0) { return $prs[0] }
        if ($SleepSeconds -gt 0) { Start-Sleep -Seconds $SleepSeconds }
    }
    throw "No open pull request found for branch $Branch"
}

function ConvertTo-IssueNumber {
    param([string]$Issue)
    if ($Issue -match '/issues/(\d+)') { return $Matches[1] }
    if ($Issue -match '^#?(\d+)$') { return $Matches[1] }
    throw "Issue must be a GitHub issue number, '#N', or issue URL (.../issues/N); got: $Issue"
}

function New-IssueBranchName {
    param([string]$IssueNumber, [string]$Timestamp, [string]$RandomSuffix)
    if (-not $Timestamp) { $Timestamp = Get-Date -Format 'yyyyMMdd-HHmmss' }
    if (-not $RandomSuffix) { $RandomSuffix = Get-Random -Maximum 9999 }
    return "factory/issue-$IssueNumber-$Timestamp-$RandomSuffix"
}

# Maps a dispatch claim to the single-Job launcher arguments. Resume fields
# ride the claim only when the queue row carries them (a same-branch restart
# prepared via PrepareResume); ordinary claims omit them so the launcher
# defaults (fresh branch, full pipeline) stay untouched. The workflow pick
# the claim froze at claim time always rides along, even when blank: the
# launcher rechecks it at start and blank means a stale pick, not a default.
function Get-ClaimedJobArgs {
    param([object]$Claim, [string]$RepoRoot, [string]$Isolator = 'multipass')
    $jobArgs = [ordered]@{
        RunId       = $Claim.runId
        RepoUrl     = $Claim.repoUrl
        Spec        = $Claim.spec
        RepoRoot    = $RepoRoot
        Isolator    = $Isolator
        CatalogJson = "$($Claim.catalogContent)"
        CatalogSha  = "$($Claim.catalogSha)"
        Workflow    = "$($Claim.workflow)"
    }
    if (-not [string]::IsNullOrWhiteSpace("$($Claim.branch)")) {
        $jobArgs['Branch'] = "$($Claim.branch)"
    }
    if (-not [string]::IsNullOrWhiteSpace("$($Claim.resumeStage)")) {
        $jobArgs['ResumeStage'] = "$($Claim.resumeStage)"
        $jobArgs['ResumeBranch'] = "$($Claim.branch)"
    }
    if (-not [string]::IsNullOrWhiteSpace("$($Claim.parentRunId)")) {
        $jobArgs['ParentRunId'] = "$($Claim.parentRunId)"
    }
    return $jobArgs
}
