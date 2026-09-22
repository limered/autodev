#!/usr/bin/env powershell
#Requires -Version 5.1
<#
.SYNOPSIS
  Runtime Environment lifecycle behind one Seam (lib/RuntimeEnvironment.ps1).

.DESCRIPTION
  Dot-source this file — it has no side effects on load:
    . (Join-Path $RepoRoot "lib/RuntimeEnvironment.ps1")
  Native multipass / container CLI calls hide behind an injectable -Executor
  Seam so Pester checks record args without a live backend. Prod callers omit
  -Executor. The container CLI is podman when present, else docker.
#>

function Write-Step {
    param([string]$Message)
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Get-HostTempDir {
    if ($env:TEMP) { return $env:TEMP }
    return [System.IO.Path]::GetTempPath()
}

function Get-ContainerCli {
    if (Get-Command podman -ErrorAction SilentlyContinue) { return 'podman' }
    if (Get-Command docker -ErrorAction SilentlyContinue) { return 'docker' }
    throw "No container runtime found: install podman or docker"
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

function Invoke-RuntimeVm {
    param(
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

function Invoke-RuntimeContainer {
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

function Wait-RuntimeJob {
    param(
        [Parameter(Mandatory = $true)][System.Management.Automation.Job]$Job,
        [Parameter(Mandatory = $true)][int]$TimeoutSeconds,
        [Parameter(Mandatory = $true)][string]$TimeoutMessage,
        [Parameter(Mandatory = $true)][string]$NoResultMessage
    )
    if (-not (Wait-Job -Job $Job -Timeout $TimeoutSeconds)) {
        Stop-Job -Job $Job -ErrorAction SilentlyContinue
        Remove-Job -Job $Job -Force -ErrorAction SilentlyContinue
        throw $TimeoutMessage
    }
    $r = Receive-Job -Job $Job
    Remove-Job -Job $Job -Force -ErrorAction SilentlyContinue
    if ($null -eq $r) {
        throw $NoResultMessage
    }
    return $r
}

function Invoke-RuntimeVmOutput {
    param(
        [Parameter(Mandatory = $true)][string[]]$Arguments,
        [scriptblock]$Executor,
        [int]$TimeoutSeconds = 20
    )
    if ($Executor) {
        return (& $Executor $Arguments)
    }
    $job = Start-Job -ScriptBlock {
        param($Arguments)
        $out = & multipass @Arguments 2>&1 | ForEach-Object { "$_" }
        [PSCustomObject]@{ Code = $LASTEXITCODE; Out = ($out -join "`n") }
    } -ArgumentList (,$Arguments)
    $r = Wait-RuntimeJob -Job $job -TimeoutSeconds $TimeoutSeconds `
        -TimeoutMessage "multipass timed out after ${TimeoutSeconds}s: multipass $Arguments" `
        -NoResultMessage "multipass failed with no result: multipass $Arguments"
    if ($r.Code -ne 0) {
        throw "multipass failed with exit code $($r.Code): multipass $Arguments : $($r.Out)"
    }
    return $r.Out
}

function Invoke-RuntimeContainerOutput {
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
    $r = Wait-RuntimeJob -Job $job -TimeoutSeconds $TimeoutSeconds `
        -TimeoutMessage "container timed out after ${TimeoutSeconds}s: $Cli $Arguments" `
        -NoResultMessage "container failed with no result: $Cli $Arguments"
    if ($r.Code -ne 0) {
        throw "container failed with exit code $($r.Code): $Cli $Arguments : $($r.Out)"
    }
    return $r.Out
}

function New-RuntimeVm {
    param([string]$Name, [string]$CloudInit, [string]$Cpus = '4', [string]$Memory = '4G', [string]$Disk = '40G', [switch]$NoWait, [scriptblock]$Executor)
    Write-Step "Launching VM $Name"
    if ($CloudInit) {
        Invoke-RuntimeVm launch 24.04 --name $Name --cpus $Cpus --memory $Memory --disk $Disk --timeout 1200 --cloud-init $CloudInit -Executor $Executor
    }
    else {
        Invoke-RuntimeVm launch 24.04 --name $Name --cpus $Cpus --memory $Memory --disk $Disk --timeout 1200 -Executor $Executor
    }
    if (-not $NoWait) {
        Write-Step "Waiting for cloud-init provisioning to complete"
        Invoke-RuntimeVm exec $Name '--' cloud-init status --wait -Executor $Executor
    }
}

function Remove-RuntimeVm {
    param([string]$Name, [scriptblock]$Executor)
    Write-Step "Destroying VM $Name"
    Invoke-RuntimeVm delete $Name --purge -Executor $Executor
}

function Copy-ToRuntimeVm {
    param([string]$Name, [string]$Source, [string]$Dest, [scriptblock]$Executor)
    Invoke-RuntimeVm transfer $Source "$($Name):$Dest" -Executor $Executor
}

function New-RuntimeContainer {
    param(
        [string]$Name,
        [string]$Image = 'slop-factory-runner:latest',
        [string[]]$ExtraMounts,
        [hashtable]$Environment,
        [string]$Cli,
        [scriptblock]$Executor
    )
    if (-not $Cli -and -not $Executor) {
        $Cli = Get-ContainerCli
    }
    $signalDir = Join-Path (Get-HostTempDir) "factory-signals/$Name"
    New-Item -ItemType Directory -Path $signalDir -Force | Out-Null
    $runArgs = @('run', '-d', '--init', '--name', $Name)
    $runArgs += @('-v', "${signalDir}:/tmp:z")
    foreach ($m in @($ExtraMounts)) {
        if ($m) { $runArgs += @('-v', $m) }
    }
    if ($Environment) {
        foreach ($k in @($Environment.Keys)) {
            $runArgs += @('-e', "$k=$($Environment[$k])")
        }
    }
    $runArgs += @($Image, 'sleep', 'infinity')
    Invoke-RuntimeContainer @runArgs -Cli $Cli -Executor $Executor
    return @{ SignalDir = $signalDir; Cli = $Cli }
}

function Copy-ToRuntimeContainer {
    param([string]$Name, [string]$Source, [string]$Dest, [string]$Cli, [scriptblock]$Executor)
    Invoke-RuntimeContainer -Arguments @('cp', $Source, "${Name}:${Dest}") -Cli $Cli -Executor $Executor
}

function Remove-RuntimeContainer {
    param([string]$Name, [string]$Cli, [string]$SignalDir, [scriptblock]$Executor)
    Invoke-RuntimeContainer -Arguments @('rm', '-f', $Name) -Cli $Cli -Executor $Executor
    if ($SignalDir) { Remove-Item -Recurse -Force -LiteralPath $SignalDir -ErrorAction SilentlyContinue }
}

function New-ContainerFileExecutor {
    param([string]$SignalDir)
    $dir = $SignalDir
    return {
        param($Arguments)
        $tail = @($Arguments | Select-Object -Skip 3)
        if ($tail.Count -eq 0) { throw "unsupported container exec: $Arguments" }
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

function New-RuntimeHeartbeatExecutorContainer {
    param([string]$SignalDir)
    return (New-ContainerFileExecutor -SignalDir $SignalDir)
}

function New-RuntimeFreezeCaptureContainer {
    param([string]$Name, [string]$Cli)
    $nameForCapture = $Name
    $cliForCapture = $Cli
    # Captured by value: the closure below outlives this scope, and a closed
    # scriptblock cannot see this file's script-scope functions when the file
    # is dot-sourced into a nested (non-entry) script.
    $invokeContainerOutput = ${function:Invoke-RuntimeContainerOutput}
    return {
        param($Command)
        & $invokeContainerOutput -Arguments @('exec', $nameForCapture, 'bash', '-c', $Command) -Cli $cliForCapture -TimeoutSeconds 15
    }.GetNewClosure()
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
