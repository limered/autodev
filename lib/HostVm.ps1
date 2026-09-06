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
        # Position 0 is load-bearing: without it PowerShell assigns implicit
        # position 0 to $Executor, so the first bare word (e.g. "launch") binds
        # to -Executor and fails [scriptblock] conversion on every prod call
        # that omits -Executor. All bare words must land in $Arguments.
        [Parameter(ValueFromRemainingArguments = $true, Position = 0)]
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

function New-VmFromBlueprint {
    param([string]$Name, [string]$CloudInit, [string]$Cpus = '4', [string]$Memory = '4G', [string]$Disk = '40G', [switch]$NoWait, [scriptblock]$Executor)
    Write-Step "Launching VM $Name"
    if ($CloudInit) {
        Invoke-Multipass launch 24.04 --name $Name --cpus $Cpus --memory $Memory --disk $Disk --cloud-init $CloudInit -Executor $Executor
    }
    else {
        Invoke-Multipass launch 24.04 --name $Name --cpus $Cpus --memory $Memory --disk $Disk -Executor $Executor
    }
    if (-not $NoWait) {
        Write-Step "Waiting for cloud-init provisioning to complete"
        Invoke-Multipass exec $Name '--' cloud-init status --wait -Executor $Executor
    }
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

function Invoke-CaptureSafe {
    param([string]$Name, [string]$Command, [scriptblock]$Capture)
    try {
        if ($Capture) { return (& $Capture $Command) }
        return (Invoke-VmCapture $Name $Command)
    }
    catch {
        return "<unavailable: $_>"
    }
}

function Save-FreezeSnapshot {
    param([string]$Name, [hashtable]$JobParams, [string]$RepoRoot, [scriptblock]$Capture, [string]$Timestamp, [string]$OutDir)
    Write-Step "Capturing freeze snapshot from $Name before teardown"
    if (-not $Timestamp) { $Timestamp = Get-Date -Format 'yyyyMMdd-HHmmss' }
    if (-not $OutDir) { $OutDir = Join-Path $RepoRoot ".scratch/freezes/$Name-$Timestamp" }
    $dir = $OutDir
    New-Item -ItemType Directory -Path $dir -Force | Out-Null

    $manifest = [ordered]@{
        vm             = $Name
        capturedAtUtc  = (Get-Date).ToUniversalTime().ToString("o")
        jobParams      = $JobParams
        markerMtime    = (Invoke-CaptureSafe $Name 'stat -c %y /tmp/heartbeat 2>/dev/null || echo missing' $Capture)
        agentLogTail   = (Invoke-CaptureSafe $Name 'f=$(ls -t ~/.local/share/opencode/log/*.log 2>/dev/null | head -1); [ -n "$f" ] && tail -n 200 "$f" || echo "<no opencode log>"' $Capture)
        psAux          = (Invoke-CaptureSafe $Name 'ps aux' $Capture)
        freeM          = (Invoke-CaptureSafe $Name 'free -m' $Capture)
        dfH            = (Invoke-CaptureSafe $Name 'df -h' $Capture)
    }

    $path = Join-Path $dir "manifest.json"
    $manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $path -Encoding UTF8
    Write-Step "Freeze snapshot written to $path"
    return $path
}
