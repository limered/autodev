#!/usr/bin/env powershell
#Requires -Version 5.1
<#
.SYNOPSIS
  Container lifecycle behind the same Seam shape as lib/HostVm.ps1.

.DESCRIPTION
  Dot-source this file — it has no side effects on load:
    . (Join-Path $RepoRoot "lib/HostContainer.ps1")
  Container CLI calls hide behind an injectable -Executor Seam so Pester
  checks record args without a live container. Prod callers omit -Executor.
  The CLI is podman when present (native on Nobara/Fedora), else docker;
  both speak the same run/exec/cp/rm verbs used here.
#>

function Get-ContainerCli {
    if (Get-Command podman -ErrorAction SilentlyContinue) { return 'podman' }
    if (Get-Command docker -ErrorAction SilentlyContinue) { return 'docker' }
    throw "No container runtime found: install podman or docker"
}

function Invoke-Container {
    param(
        [Parameter(ValueFromRemainingArguments = $true, Position = 0)]
        [string[]]$Arguments,
        [scriptblock]$Executor,
        [string]$Cli
    )
    if ($Executor) {
        $code = & $Executor $Arguments
        if ($null -ne $code -and $code -ne 0) {
            throw "container failed with exit code ${code}: $Arguments"
        }
        return
    }
    if (-not $Cli) { $Cli = Get-ContainerCli }
    $prev = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    & $Cli @Arguments 2>&1 | ForEach-Object { "$_" }
    $ErrorActionPreference = $prev
    if ($LASTEXITCODE -ne 0) {
        throw "container failed with exit code ${LASTEXITCODE}: $Cli $Arguments"
    }
}

function Invoke-ContainerOutput {
    param(
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [scriptblock]$Executor,
        [string]$Cli,
        [int]$TimeoutSeconds = 20
    )
    if ($Executor) {
        return (& $Executor $Arguments)
    }
    if (-not $Cli) { $Cli = Get-ContainerCli }
    $job = Start-Job -ScriptBlock {
        param($Cli, $Arguments)
        $out = & $Cli @Arguments 2>&1 | ForEach-Object { "$_" }
        [PSCustomObject]@{ Code = $LASTEXITCODE; Out = ($out -join "`n") }
    } -ArgumentList $Cli, (,$Arguments)
    if (-not (Wait-Job -Job $job -Timeout $TimeoutSeconds)) {
        Stop-Job -Job $job -ErrorAction SilentlyContinue
        Remove-Job -Job $job -Force -ErrorAction SilentlyContinue
        throw "container timed out after ${TimeoutSeconds}s: $Cli $Arguments"
    }
    $r = Receive-Job -Job $job
    Remove-Job -Job $job -Force -ErrorAction SilentlyContinue
    if ($null -eq $r) {
        throw "container failed with no result: $Cli $Arguments"
    }
    if ($r.Code -ne 0) {
        throw "container failed with exit code $($r.Code): $Cli $Arguments : $($r.Out)"
    }
    return $r.Out
}

function New-ContainerFromImage {
    param(
        [string]$Name,
        [string]$Image = 'slop-factory-runner:latest',
        [string]$SignalDir,
        [string[]]$ExtraMounts,
        [hashtable]$Environment,
        [string]$Cli,
        [scriptblock]$Executor
    )
    $runArgs = @('run', '-d', '--init', '--name', $Name)
    if ($SignalDir) {
        $runArgs += @('-v', "${SignalDir}:/tmp:z")
    }
    foreach ($m in @($ExtraMounts)) {
        if ($m) { $runArgs += @('-v', $m) }
    }
    if ($Environment) {
        foreach ($k in @($Environment.Keys)) {
            $runArgs += @('-e', "$k=$($Environment[$k])")
        }
    }
    $runArgs += @($Image, 'sleep', 'infinity')
    Invoke-Container @runArgs -Cli $Cli -Executor $Executor
}

function Copy-IntoContainer {
    param([string]$Name, [string]$Source, [string]$Dest, [string]$Cli, [scriptblock]$Executor)
    Invoke-Container -Arguments @('cp', $Source, "${Name}:${Dest}") -Cli $Cli -Executor $Executor
}

function Remove-Container {
    param([string]$Name, [string]$Cli, [scriptblock]$Executor)
    Invoke-Container -Arguments @('rm', '-f', $Name) -Cli $Cli -Executor $Executor
}

function Get-HostTempDir {
    if ($env:TEMP) { return $env:TEMP }
    return [System.IO.Path]::GetTempPath()
}

function Get-ContainerSocketMount {
    try {
        if (-not $env:XDG_RUNTIME_DIR) { return $null }
        $sock = Join-Path $env:XDG_RUNTIME_DIR 'podman/podman.sock'
        if (Test-Path -LiteralPath $sock) {
            return "${sock}:/var/run/docker.sock:z"
        }
    }
    catch {
    }
    return $null
}

function New-ContainerFileExecutor {
    param([string]$SignalDir)
    $dir = $SignalDir
    return {
        param($Arguments)
        $tail = @($Arguments | Select-Object -Skip 3)
        if ($tail.Count -eq 0) { throw "unsupported container exec: $Arguments" }
        # Combined heartbeat poll (one exec): answer the same key=value
        # probe the VM answers via bash -c, reading the signal dir that is
        # mounted as the guest's /tmp.
        if ($tail[0] -eq 'bash') {
            $now = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds().ToString()
            $hb = 'MISSING'
            $hbPath = Join-Path $dir 'heartbeat'
            if (Test-Path -LiteralPath $hbPath) {
                $hb = ([DateTimeOffset](Get-Item -LiteralPath $hbPath).LastWriteTimeUtc).ToUnixTimeSeconds().ToString()
            }
            $ph = 'MISSING'
            $phPath = Join-Path $dir 'current-phase'
            if (Test-Path -LiteralPath $phPath) {
                $raw = (Get-Content -LiteralPath $phPath -Raw)
                $first = ("$raw" -split "`r?`n")[0].Trim()
                if ($first -ne '') { $ph = $first }
            }
            $cg = 'MISSING'
            $cgPath = Join-Path $dir 'current-category'
            if (Test-Path -LiteralPath $cgPath) {
                $raw = (Get-Content -LiteralPath $cgPath -Raw)
                $first = ("$raw" -split "`r?`n")[0].Trim()
                if ($first -ne '') { $cg = $first }
            }
            $dn = 'MISSING'
            $dnPath = Join-Path $dir 'factory-done'
            if (Test-Path -LiteralPath $dnPath) {
                $raw = (Get-Content -LiteralPath $dnPath -Raw)
                $first = ("$raw" -split "`r?`n")[0].Trim()
                if ($first -ne '') { $dn = $first }
            }
            return ("now=$now`nhb=$hb`nphase=$ph`ncategory=$cg`ndone=$dn`n")
        }
        if ($tail[0] -eq 'date') {
            return [DateTimeOffset]::UtcNow.ToUnixTimeSeconds().ToString()
        }
        if ($tail[0] -eq 'stat') {
            $p = Join-Path $dir 'heartbeat'
            if (-not (Test-Path -LiteralPath $p)) { throw "missing heartbeat" }
            $mtime = (Get-Item -LiteralPath $p).LastWriteTimeUtc
            return ([DateTimeOffset]$mtime).ToUnixTimeSeconds().ToString()
        }
        if ($tail[0] -eq 'cat') {
            $name = Split-Path -Leaf $tail[-1]
            $p = Join-Path $dir $name
            if (-not (Test-Path -LiteralPath $p)) { throw "missing $name" }
            return (Get-Content -LiteralPath $p -Raw)
        }
        throw "unsupported container exec: $Arguments"
    }.GetNewClosure()
}
