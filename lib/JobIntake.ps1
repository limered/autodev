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
