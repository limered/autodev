#!/usr/bin/env powershell
#Requires -Version 5.1
<#
.SYNOPSIS
  End-to-end smoke test of the feature-builder job launcher.

.DESCRIPTION
  Thin wrapper over start-job.ps1: runs a tiny sample spec against
  limered/autodev and lets start-job.ps1 launch/provision/run/verify/destroy.

.PARAMETER Model
  opencode model id. Defaults to start-job.ps1's default.
#>
[CmdletBinding()]
param(
    [string]$Model = "opencode-go/kimi-k2.7-code"
)

$ErrorActionPreference = "Stop"

$spec = "Add a one-line note to AGENTS.md stating this repo is managed by the AI Software Factory."

$result = & (Join-Path $PSScriptRoot "start-job.ps1") `
    -RepoUrl "https://github.com/limered/autodev.git" `
    -Spec $spec `
    -Model $Model

if ($LASTEXITCODE -ne 0) {
    Write-Host "TEST FAILED" -ForegroundColor Red
    exit 1
}

Write-Host "`nSUCCESS: feature-builder pushed $($result.Branch) and created $($result.PrUrl)" -ForegroundColor Green
