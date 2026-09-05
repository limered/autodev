#!/usr/bin/env powershell
#Requires -Version 5.1
<#
.SYNOPSIS
  Disposable-VM lifecycle behind one Seam (lib/HostVm.ps1).

.DESCRIPTION
  Dot-source this file — it has no side effects on load:
    . (Join-Path $RepoRoot "lib/HostVm.ps1")
  Native multipass calls hide behind an injectable -Executor Seam so Pester
  checks record args without a live VM. Prod callers omit -Executor.
#>

function Write-Step {
    param([string]$Message)
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Invoke-Multipass {
    param(
        [Parameter(ValueFromRemainingArguments = $true)]
        [string[]]$Arguments,
        [scriptblock]$Executor
    )
    if ($Executor) {
        $code = & $Executor $Arguments
        if ($null -ne $code -and $code -ne 0) {
            throw "multipass failed with exit code ${code}: multipass $Arguments"
        }
        return
    }
    $prev = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    & multipass @Arguments 2>&1 | ForEach-Object { "$_" }
    $ErrorActionPreference = $prev
    if ($LASTEXITCODE -ne 0) {
        throw "multipass failed with exit code ${LASTEXITCODE}: multipass $Arguments"
    }
}

function Invoke-MultipassOutput {
    param(
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [scriptblock]$Executor
    )
    if ($Executor) {
        return (& $Executor $Arguments)
    }
    $prev = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    $output = & multipass @Arguments 2>&1
    $exitCode = $LASTEXITCODE
    $ErrorActionPreference = $prev
    if ($exitCode -ne 0) {
        throw "multipass failed with exit code ${exitCode}: multipass $Arguments : $output"
    }
    return ($output | ForEach-Object { "$_" }) -join "`n"
}

function Remove-Vm {
    param([string]$Name, [scriptblock]$Executor)
    Write-Step "Destroying VM $Name"
    Invoke-Multipass delete $Name --purge -Executor $Executor
}

function Invoke-VmCapture {
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
    param([string]$Name, [hashtable]$JobParams, [string]$RepoRoot)
    Write-Step "Capturing freeze snapshot from $Name before teardown"
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $dir = Join-Path $RepoRoot ".scratch/freezes/$Name-$stamp"
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
