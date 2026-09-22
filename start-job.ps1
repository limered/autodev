#!/usr/bin/env powershell
#Requires -Version 5.1
<#
.SYNOPSIS
  Run an opencode feature-builder job against a GitHub repo in a disposable VM.

.DESCRIPTION
  Launches a fresh multipass VM from the project blueprint, provisions it with
  the bot SSH key, GitHub PAT, opencode API key, and the local .opencode config,
  clones the target repo, runs the opencode agent headlessly against the given
  spec, polls GitHub for the resulting branch/PR, prints the PR URL, and destroys
  the VM. Emits a result object; exits 1 on failure.

.PARAMETER RepoUrl
  Target repo HTTPS URL, e.g. https://github.com/owner/name.git.

.PARAMETER Spec
  Feature description / spec the agent should implement.

.PARAMETER Model
  opencode model id. Defaults to the default_agent's model in .opencode/opencode.json.

.PARAMETER Branch
  Branch the agent should create. A unique default is generated.

.PARAMETER VmName
  multipass VM name. A unique default is generated.

.PARAMETER RepoRoot
  Path to the slop-factory repo root (source of .opencode, blueprint, secrets).

.PARAMETER RunId
  Optional run identity used for all lifecycle events. Defaults to a fresh GUID.

.PARAMETER KeepVmOnFailure
  Leave the VM running if the job fails, for debugging.

.PARAMETER Isolator
  Guest backend: 'multipass' (Windows VM, default) or 'container'
  (ephemeral podman/docker container on a Linux host).

.PARAMETER Image
  Container image for -Isolator container.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$RepoUrl,
    [Parameter(Mandatory = $true)][string]$Spec,
    [string]$Model,
    [string]$Branch = "factory/job-$(Get-Date -Format 'yyyyMMdd-HHmmss')-$(Get-Random -Maximum 9999)",
    [string]$VmName = "factory-job-$(Get-Date -Format 'yyyyMMdd-HHmmss')-$(Get-Random -Maximum 9999)",
    [string]$RepoRoot = $PSScriptRoot,
    [string]$RunId = [guid]::NewGuid().ToString(),
    [ValidateSet('multipass', 'container')][string]$Isolator = $(if ($env:OS -eq 'Windows_NT') { 'multipass' } else { 'container' }),
    [string]$Image = 'slop-factory-runner:latest',
    [switch]$KeepVmOnFailure
)

$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "lib/JobIntake.ps1")
. (Join-Path $PSScriptRoot "lib/AgentsConfig.ps1")
. (Join-Path $PSScriptRoot "lib/RuntimeEnvironment.ps1")

$Repo = ConvertTo-OwnerRepo -RepoUrl $RepoUrl

# Default the run-level model to the default_agent's model.
if (-not $Model) {
    $configPath = Join-Path $RepoRoot ".opencode/opencode.json"
    $defaultAgent = (Get-Content -LiteralPath $configPath -Raw | ConvertFrom-Json).default_agent
    $Model = Get-AgentModel -Agent $defaultAgent -RepoRoot $RepoRoot
}

$seedConfig = Read-AgentsConfig -Path (Join-Path $RepoRoot "agents.json")
$agentModels = Get-AgentModelMap -Config $seedConfig -RepoRoot $RepoRoot
$categoryLookup = { param($agent, $iteration) Get-StepCategory -Agent "$agent" -Iteration ([int]$iteration) -Config $seedConfig }.GetNewClosure()
$stages = @(ConvertTo-SeededStages -Config $seedConfig -ModelMap $agentModels)

# The run id is either supplied by the dispatch client or defaulted above to a
# fresh GUID; it is the identity carried on every dashboard event.

# Fire-and-forget dashboard reporting (no-op if .secrets/ config is absent).
. (Join-Path $RepoRoot "factory-report.ps1")
Initialize-FactoryReport -RepoRoot $RepoRoot
Send-FactoryEvent -RunId $RunId -Type "run-started" -Fields @{
    repo   = $Repo
    branch = $Branch
    spec   = $Spec
    model  = $Model
    stages = $stages
}

$cloudInit = Join-Path $RepoRoot "infrastructure/multipass/cloud-init.yaml"
$testScript = Join-Path $RepoRoot "infrastructure/multipass/test-feature-builder.sh"
$agentsJson = Join-Path $RepoRoot "agents.json"
$secretsDir = Join-Path $RepoRoot ".secrets"
$patFile = Join-Path $secretsDir "github-pat.txt"
$apiKeyFile = Join-Path $secretsDir "opencode-api-key.txt"
$opencodeDir = Join-Path $RepoRoot ".opencode"

$vmCreated = $false
$jobFailed = $false
$prUrl = $null
$pr = $null

try {
    if (-not (Test-Path $testScript)) { throw "test script not found: $testScript" }
    if (-not (Test-Path $agentsJson)) { throw "agents.json not found: $agentsJson" }
    if (-not (Test-Path $patFile)) { throw "PAT file not found: $patFile" }
    if (-not (Test-Path $apiKeyFile)) { throw "opencode API key file not found: $apiKeyFile" }
    if (-not (Test-Path $opencodeDir)) { throw ".opencode directory not found: $opencodeDir" }

    $watchExec = $null
    $freezeCapture = $null
    $signalDir = $null
    $containerCli = $null
    $CopyInitialFiles = $null
    $CopyToRuntime = $null
    $InvokeInRuntime = $null
    $StartGuestJob = $null
    $RemoveRuntime = $null
    if ($Isolator -eq 'container') {
        $containerCli = Get-ContainerCli
        $preSignalDir = Join-Path (Get-HostTempDir) "factory-signals/$VmName"
        $nameForRemove = $VmName
        $cliForRemove = $containerCli
        $dirForRemove = $preSignalDir
        $RemoveRuntime = { Remove-RuntimeContainer -Name $nameForRemove -Cli $cliForRemove -SignalDir $dirForRemove }.GetNewClosure()
        $secretMounts = @(
            "${patFile}:/tmp/github-pat.txt:ro,z",
            "${apiKeyFile}:/tmp/opencode-api-key.txt:ro,z"
        )
        $sockMount = Get-ContainerSocketMount
        if ($sockMount) { $secretMounts += $sockMount }
        $runtimeInfo = New-RuntimeContainer -Name $VmName -Image $Image -ExtraMounts $secretMounts -Cli $containerCli
        $containerCli = $runtimeInfo.Cli
        if (-not $containerCli) { $containerCli = Get-ContainerCli }
        $signalDir = $runtimeInfo.SignalDir
        $watchExec = New-RuntimeHeartbeatExecutorContainer -SignalDir $signalDir
        $freezeCapture = New-RuntimeFreezeCaptureContainer -Name $VmName -Cli $containerCli
        $nameCopy = $VmName
        $cliCopy = $containerCli
        $testScriptCopy = $testScript
        $agentsJsonCopy = $agentsJson
        $CopyToRuntime = { param($Source, $Dest) Copy-ToRuntimeContainer -Name $nameCopy -Source $Source -Dest $Dest -Cli $cliCopy }.GetNewClosure()
        $InvokeInRuntime = { param([string]$Command) Invoke-RuntimeContainer -Arguments @('exec', $nameCopy, 'bash', '-c', $Command) -Cli $cliCopy }.GetNewClosure()
        $CopyInitialFiles = {
            Write-Step "Copying .opencode config and test script into container"
            Copy-ToRuntimeContainer -Name $nameCopy -Source $testScriptCopy -Dest '/tmp/test-feature-builder.sh' -Cli $cliCopy
            Copy-ToRuntimeContainer -Name $nameCopy -Source $agentsJsonCopy -Dest '/tmp/agents.json' -Cli $cliCopy
            Invoke-RuntimeContainer -Arguments @('exec', $nameCopy, 'bash', '-c', "sed -i 's/\r`$//' /tmp/test-feature-builder.sh") -Cli $cliCopy
        }.GetNewClosure()
        $modelCopy = $Model
        $branchCopy = $Branch
        $specCopy = $Spec
        $repoCopy = $Repo
        $StartGuestJob = {
            $jobScript = {
                param($Cli, $InnerName, $InnerModel, $InnerBranch, $InnerSpec, $InnerRepo)
                $ErrorActionPreference = "Continue"
                $specB64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($InnerSpec))
                & $Cli exec $InnerName env "MODEL=$InnerModel" "ISSUE_B64=$specB64" bash /tmp/test-feature-builder.sh "$InnerBranch" "$InnerRepo" 2>&1 | ForEach-Object { "$_" }
                if ($LASTEXITCODE -ne 0) {
                    throw "container exec failed with exit code ${LASTEXITCODE}"
                }
            }
            return (Start-Job -ScriptBlock $jobScript -ArgumentList $cliCopy, $nameCopy, $modelCopy, $branchCopy, $specCopy, $repoCopy)
        }.GetNewClosure()
    }
    else {
        if (-not (Test-Path $cloudInit)) { throw "cloud-init not found: $cloudInit" }
        $nameForRemoveVm = $VmName
        $RemoveRuntime = { Remove-RuntimeVm -Name $nameForRemoveVm }.GetNewClosure()
        New-RuntimeVm -Name $VmName -CloudInit $cloudInit
        $nameCopy = $VmName
        $testScriptCopy = $testScript
        $agentsJsonCopy = $agentsJson
        $patCopy = $patFile
        $apiKeyCopy = $apiKeyFile
        $CopyToRuntime = { param($Source, $Dest) Copy-ToRuntimeVm -Name $nameCopy -Source $Source -Dest $Dest }.GetNewClosure()
        $InvokeInRuntime = { param([string]$Command) Invoke-RuntimeVm exec $nameCopy '--' bash -c $Command }.GetNewClosure()
        $CopyInitialFiles = {
            Write-Step "Transferring PAT, opencode API key, .opencode config, and test script into VM"
            Copy-ToRuntimeVm -Name $nameCopy -Source $patCopy -Dest '/tmp/github-pat.txt'
            Copy-ToRuntimeVm -Name $nameCopy -Source $apiKeyCopy -Dest '/tmp/opencode-api-key.txt'
            Copy-ToRuntimeVm -Name $nameCopy -Source $testScriptCopy -Dest '/tmp/test-feature-builder.sh'
            Copy-ToRuntimeVm -Name $nameCopy -Source $agentsJsonCopy -Dest '/tmp/agents.json'
            Invoke-RuntimeVm exec $nameCopy '--' bash -c "sed -i 's/\r`$//' /tmp/test-feature-builder.sh"
        }.GetNewClosure()
        $modelCopy = $Model
        $branchCopy = $Branch
        $specCopy = $Spec
        $repoCopy = $Repo
        $StartGuestJob = {
            $jobScript = {
                param($InnerName, $InnerModel, $InnerBranch, $InnerSpec, $InnerRepo)
                $ErrorActionPreference = "Continue"
                $specB64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($InnerSpec))
                & multipass exec $InnerName '--' env "MODEL=$InnerModel" "ISSUE_B64=$specB64" bash /tmp/test-feature-builder.sh "$InnerBranch" "$InnerRepo" 2>&1 | ForEach-Object { "$_" }
                if ($LASTEXITCODE -ne 0) {
                    throw "multipass exec failed with exit code ${LASTEXITCODE}"
                }
            }
            return (Start-Job -ScriptBlock $jobScript -ArgumentList $nameCopy, $modelCopy, $branchCopy, $specCopy, $repoCopy)
        }.GetNewClosure()
    }
    $vmCreated = $true

    & $CopyInitialFiles

    # Transfer the .opencode directory by tarring it, moving the archive into
    # the guest, and extracting it to /tmp/.opencode.
    $opencodeTar = Join-Path (Get-HostTempDir) "opencode-config-$VmName.tar.gz"
    Remove-Item -LiteralPath $opencodeTar -ErrorAction SilentlyContinue
    & tar -czf $opencodeTar -C $RepoRoot ".opencode"
    if ($LASTEXITCODE -ne 0) { throw "tar failed creating .opencode archive" }
    & $CopyToRuntime $opencodeTar '/tmp/opencode-config.tar.gz'
    & $InvokeInRuntime 'rm -rf /tmp/.opencode && tar -xzf /tmp/opencode-config.tar.gz -C /tmp'

    Write-Step "Running feature-builder job inside guest (repo: $Repo, branch: $Branch)"
    $vmJob = & $StartGuestJob

    Send-FactoryEvent -RunId $RunId -Type "agent-started" -Fields @{ vmName = $VmName }

    $watchScript = Join-Path $PSScriptRoot "watch-heartbeat.ps1"
    # 10 min, not the 5 min default: a long silent model completion (no interim
    # output line -> no heartbeat touch) was false-killing legit mid-work agents.
    # A dead model never recovers, so the extra 5 min only costs a rare real stall.
    & $watchScript -VmName $VmName -Job $vmJob -RunId $RunId -RepoRoot $RepoRoot -StallThresholdSeconds 600 -Executor $watchExec

    # V1 per-step pull (not streaming): the guest is still up and no terminal
    # event has been sent, so every phase-finished lands before run-finished.
    # The model rides from the shared host-side agent model map.
    Write-Step "Relaying per-phase timing + tokens to the dashboard"
    try {
        Send-PhaseFinishedSteps -RunId $RunId -VmName $VmName -Config $seedConfig -ModelMap $agentModels -CategoryLookup $categoryLookup -Executor $watchExec
    }
    catch {
        Write-Host "WARN: per-phase step relay failed: $_" -ForegroundColor Yellow
    }

    Write-Step "Polling GitHub for the pull request on branch $Branch"
    $pat = (Get-Content -LiteralPath $patFile -Raw).Trim()
    $owner = $Repo.Split('/')[0]
    $headers = @{
        Authorization = "Bearer $pat"
        Accept = "application/vnd.github.v3+json"
    }
    # Push+PR creation is done by the time the agent exits, but GitHub's interface can
    # lag a beat. Poll a few times before giving up.
    $poll = {
        param($Repo, $Owner, $Branch)
        Invoke-RestMethod -Uri "https://api.github.com/repos/$Repo/pulls?state=open&head=$($Owner):$Branch" -Headers $headers
    }.GetNewClosure()
    $pr = Wait-ForPullRequest -Repo $Repo -Owner $owner -Branch $Branch -Poll $poll
    $prUrl = $pr.html_url
    Write-Step "Verified PR: $prUrl"
    Send-FactoryEvent -RunId $RunId -Type "pr-verified" -Fields @{ prUrl = $prUrl }
    Send-FactoryEvent -RunId $RunId -Type "run-finished"
}
catch {
    $jobFailed = $true
    $failureReason = "$_"
    if ($_.ScriptStackTrace) { $failureReason += "`n$($_.ScriptStackTrace)" }
    Write-Host "ERROR: $failureReason" -ForegroundColor Red
    if (-not $vmCreated -and -not $KeepVmOnFailure) {
        # A launch that times out still leaves the guest behind;
        # purge it best-effort so retries don't pile up orphans.
        try { if ($RemoveRuntime) { & $RemoveRuntime } } catch {}
    }
    if ($vmCreated) {
        # Best-effort step relay before the terminal event: phases that
        # finished before the failure still land (re-emits are safe,
        # last-write-wins); steps must precede run-failed or they are dropped.
        try {
            Send-PhaseFinishedSteps -RunId $RunId -VmName $VmName -Config $seedConfig -ModelMap $agentModels -CategoryLookup $categoryLookup -Executor $watchExec
        }
        catch {
            Write-Host "WARN: per-phase step relay failed: $_" -ForegroundColor Yellow
        }
        try {
            $freezePath = Save-FreezeSnapshot -Name $VmName -RepoRoot $RepoRoot -Capture $freezeCapture -JobParams @{
                repo   = $Repo
                branch = $Branch
                spec   = $Spec
                model  = $Model
            }
            # Emit freeze-captured before run-failed so the freeze info lands
            # first (ordering is defensive only; the backend tolerates any order).
            if ($freezePath) {
                Send-FactoryEvent -RunId $RunId -Type "freeze-captured" -Fields @{ freezeLocalPath = $freezePath }
            }
        }
        catch {
            Write-Host "WARN: freeze snapshot capture failed: $_" -ForegroundColor Yellow
        }
    }
    Send-FactoryEvent -RunId $RunId -Type "run-failed" -Fields @{ failureReason = $failureReason }
}
finally {
    if ($vmCreated -and -not ($jobFailed -and $KeepVmOnFailure)) {
        & $RemoveRuntime
    }
    elseif ($vmCreated) {
        Write-Step "Keeping guest $VmName for debugging (job failed, -KeepVmOnFailure set)"
        if ($signalDir) { Write-Step "Signal dir: $signalDir" }
    }
}

if ($jobFailed) { exit 1 }

Write-Host $prUrl
[PSCustomObject]@{
    Repo     = $Repo
    Branch   = $Branch
    PrUrl    = $prUrl
    PrNumber = $pr.number
}
