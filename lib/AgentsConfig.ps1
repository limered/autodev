#!/usr/bin/env powershell
#Requires -Version 5.1
<#
.SYNOPSIS
  Repo-root category configuration behind one Seam (lib/AgentsConfig.ps1).

.DESCRIPTION
  Dot-source this file — no side effects on load:
    . (Join-Path $RepoRoot "lib/AgentsConfig.ps1")
  The repo-root agents.json holds the stages map: each category id maps to a
  type plus its member agent names; loop additionally declares an iteration
  count. Seeding emits one stage per category in map order with the model of
  its first member. The map keys are the category set, so ids are unique by
  construction while the same worker may appear in several categories.
  The same file may declare a workflows map: each workflow name maps to an
  ordered list of stage ids executed in listed order, plus a top-level
  defaultWorkflow marker naming the default. A stages-only file reads as the
  default workflow (all stages, catalog order). References are strict: empty
  maps, empty workflows, unknown or duplicated ids, the reserved name
  default, and a missing or dangling marker all fail fast.
  Select-WorkflowStages filters parsed entries to one workflow in workflow
  order so seeding, candidates, and slots follow the pick unchanged.
  Pure converters take strings or objects so Pester drives them without files.
  The agent model map resolves each member model once from its definition
  frontmatter; an injected file reader keeps that read Pester-pure.
#>

function ConvertFrom-AgentsJsonText {
    param([Parameter(Mandatory = $true)][string]$Json)
    try {
        return $Json | ConvertFrom-Json -ErrorAction Stop
    }
    catch {
        throw "Agents config is not valid JSON: $_"
    }
}

function Get-StageIds {
    param($Config)
    return @(@($Config) | ForEach-Object { "$($_.Id)" })
}

function Read-AgentsJsonFile {
    param([Parameter(Mandatory = $true)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Agents config not found: $Path"
    }
    return Get-Content -LiteralPath $Path -Raw
}

# True when the raw document declares the workflow value as an array.
# Windows PowerShell 5.1 unwraps single-element arrays to a scalar, so a
# scalar stage id is ambiguous without this check against the raw text.
function Test-WorkflowValueIsArray {
    param([Parameter(Mandatory = $true)][string]$Json, [Parameter(Mandatory = $true)][string]$Name)
    $idx = $Json.IndexOf('"workflows"')
    if ($idx -lt 0) { return $false }
    return [regex]::IsMatch($Json.Substring($idx), '"' + [regex]::Escape($Name) + '"\s*:\s*\[')
}

function ConvertFrom-AgentsConfigJson {
    param([Parameter(Mandatory = $true)][string]$Json)
    return ConvertFrom-AgentsConfigObject -Parsed (ConvertFrom-AgentsJsonText -Json $Json)
}

# Stages-only parse of an already-parsed document. Workflow rules live in
# ConvertFrom-AgentsWorkflowsObject so this stays a pure stages reader.
function ConvertFrom-AgentsConfigObject {
    param($Parsed)
    if ($null -eq $Parsed -or $null -eq $Parsed.stages) {
        throw "Agents config must declare a 'stages' map"
    }
    $entries = @()
    foreach ($prop in $parsed.stages.PSObject.Properties) {
        $id = "$($prop.Name)"
        if ([string]::IsNullOrWhiteSpace($id)) {
            throw "Agents config stage id must be a non-empty string"
        }
        $node = $prop.Value
        if ($null -eq $node) {
            throw "Agents config stage '$id' must declare a type and agents"
        }
        $rawType = "$($node.type)".Trim().ToLowerInvariant()
        if ([string]::IsNullOrWhiteSpace($rawType)) {
            throw "Agents config stage '$id' must declare a type"
        }
        if ($rawType -notin @('sequential', 'loop')) {
            throw "Agents config stage '$id' has unknown type '$($node.type)' (expected sequential or loop)"
        }
        $type = $rawType
        $agents = @()
        if ($null -ne $node.agents) {
            $agents = @(@($node.agents) | ForEach-Object { "$_" } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        }
        if ($agents.Count -eq 0) {
            throw "Agents config stage '$id' must list at least one member agent"
        }
        $iterations = $null
        if ($type -eq 'loop') {
            if ($null -eq $node.iterations) {
                throw "Agents config stage '$id' is a loop and must declare an iteration count"
            }
            $count = 0
            try {
                $count = [long]$node.iterations
            }
            catch {
                throw "Agents config stage '$id' must declare a positive iteration count"
            }
            if ($count -lt 1 -or ([double]$node.iterations -ne [double]$count)) {
                throw "Agents config stage '$id' must declare a positive iteration count"
            }
            $iterations = [int]$count
        }
        $entries += [PSCustomObject]@{
            Id         = $id
            Type       = $type
            Agents     = $agents
            Iterations = $iterations
        }
    }
    if ($entries.Count -eq 0) {
        throw "Agents config must declare at least one stage"
    }
    return $entries
}

# Shared strict check for one workflow's ordered stage id list: non-empty,
# known ids only, no duplicates. -Workflow only sharpens error messages.
function Assert-WorkflowStageIds {
    param([string]$Workflow, $StageIds, [string[]]$KnownIds)
    $subject = 'selection'
    if (-not [string]::IsNullOrWhiteSpace("$Workflow")) { $subject = "'$Workflow'" }
    $raw = @($StageIds)
    if ($raw.Count -eq 0) {
        throw "Agents config workflow $subject must list at least one stage id"
    }
    $ids = @()
    foreach ($entry in $raw) {
        $id = "$entry"
        if ([string]::IsNullOrWhiteSpace($id)) {
            throw "Agents config workflow $subject lists an empty stage id"
        }
        if (@($KnownIds) -cnotcontains $id) {
            throw "Agents config workflow $subject references unknown stage id '$id' (known: $($KnownIds -join ', '))"
        }
        if ($ids -ccontains $id) {
            throw "Agents config workflow $subject lists stage id '$id' more than once"
        }
        $ids += $id
    }
    return $ids
}

# Parses the named workflows map of an agents.json file against its stage
# catalog. A file with only a stages map reads as the default workflow (all
# stage ids, catalog order) with unchanged behavior; a file with a workflows
# map must also name its default via the top-level defaultWorkflow marker.
# Each workflow is an ordered list of known stage ids with no duplicates; the
# name default is reserved for the legacy synthesis. Returns the workflows in
# file order plus the default workflow name. Pure: takes strings or objects.
function ConvertFrom-AgentsWorkflowsJson {
    param([Parameter(Mandatory = $true)][string]$Json, $Config)
    $parsed = ConvertFrom-AgentsJsonText -Json $Json
    return ConvertFrom-AgentsWorkflowsObject -Parsed $parsed -Config $Config -RawJson $Json
}

# Workflow validation against an already-parsed document. -RawJson carries
# the source text so single-element arrays unwrapped to a scalar by older
# parsers still read as a valid single-stage workflow.
function ConvertFrom-AgentsWorkflowsObject {
    param($Parsed, $Config, [string]$RawJson)
    $knownIds = Get-StageIds -Config $Config
    $hasWorkflows = ($null -ne $parsed) -and ($null -ne $parsed.PSObject.Properties['workflows'])
    $hasDefault = ($null -ne $parsed) -and ($null -ne $parsed.PSObject.Properties['defaultWorkflow'])
    if (-not $hasWorkflows) {
        if ($hasDefault) {
            throw "Agents config declares a default workflow ('$($parsed.defaultWorkflow)') but no workflows map"
        }
        $legacy = [ordered]@{}
        $legacy['default'] = @($knownIds)
        return [PSCustomObject]@{ Workflows = $legacy; DefaultWorkflow = 'default' }
    }
    $node = $parsed.workflows
    if ($null -eq $node -or $null -eq $node.PSObject.Properties -or @($node.PSObject.Properties).Count -eq 0) {
        throw "Agents config workflows map must declare at least one workflow"
    }
    $workflows = [ordered]@{}
    foreach ($prop in $node.PSObject.Properties) {
        $name = "$($prop.Name)"
        if ([string]::IsNullOrWhiteSpace($name)) {
            throw "Agents config workflow name must be a non-empty string"
        }
        if ($name -eq 'default') {
            throw "Agents config workflow name 'default' is reserved for the legacy stages-only workflow"
        }
        $list = $prop.Value
        if ($list -isnot [array]) {
            if ($null -ne $list -and $list -is [string] -and -not [string]::IsNullOrEmpty($RawJson) -and (Test-WorkflowValueIsArray -Json $RawJson -Name $name)) {
                $list = @($list)
            }
            else {
                throw "Agents config workflow '$name' must be an ordered list of stage ids"
            }
        }
        $workflows[$name] = @(Assert-WorkflowStageIds -Workflow $name -StageIds $list -KnownIds $knownIds)
    }
    if (-not $hasDefault -or [string]::IsNullOrWhiteSpace("$($parsed.defaultWorkflow)")) {
        throw "Agents config declares workflows but no default workflow marker ('defaultWorkflow')"
    }
    $default = "$($parsed.defaultWorkflow)"
    if (@($workflows.Keys) -cnotcontains $default) {
        throw "Agents config default workflow '$default' names no known workflow (known: $($workflows.Keys -join ', '))"
    }
    return [PSCustomObject]@{ Workflows = $workflows; DefaultWorkflow = $default }
}

# Filters parsed stage entries to the picked workflow in workflow order.
# Fails fast on an empty, unknown, or duplicated stage id so a stale pick
# never silently seeds the wrong stages. -Workflow only sharpens errors.
function Select-WorkflowStages {
    param($Config, $StageIds, [string]$Workflow)
    $entries = @($Config)
    $knownIds = Get-StageIds -Config $entries
    $ids = @(Assert-WorkflowStageIds -Workflow $Workflow -StageIds $StageIds -KnownIds $knownIds)
    $filtered = @()
    foreach ($id in $ids) {
        $match = @($entries | Where-Object { "$($_.Id)" -ceq $id }) | Select-Object -First 1
        $filtered += $match
    }
    return $filtered
}

function Read-AgentsWorkflowCatalog {
    param([Parameter(Mandatory = $true)][string]$Path)
    $raw = Read-AgentsJsonFile -Path $Path
    $parsed = ConvertFrom-AgentsJsonText -Json $raw
    $stages = ConvertFrom-AgentsConfigObject -Parsed $parsed
    $flows = ConvertFrom-AgentsWorkflowsObject -Parsed $parsed -Config $stages -RawJson $raw
    return [PSCustomObject]@{
        Stages          = $stages
        Workflows       = $flows.Workflows
        DefaultWorkflow = $flows.DefaultWorkflow
    }
}

function Read-AgentsConfig {
    param([Parameter(Mandatory = $true)][string]$Path)
    return ConvertFrom-AgentsConfigJson -Json (Read-AgentsJsonFile -Path $Path)
}

function Get-AgentModelErrorMessage {
    param([string]$Agent, [string]$Category, [string]$Cause)
    $detail = if ([string]::IsNullOrWhiteSpace("$Cause")) { ' (lookup returned empty)' } else { ": $Cause" }
    if ([string]::IsNullOrWhiteSpace("$Category")) {
        return "No model found for agent '$Agent'$detail"
    }
    return "No model found for agent '$Agent' (category '$Category')$detail"
}

function Get-AgentModel {
    param(
        [Parameter(Mandatory = $true)][string]$Agent,
        [Parameter(Mandatory = $true)][string]$RepoRoot,
        [scriptblock]$FileReader
    )
    $agentPath = Join-Path $RepoRoot (Join-Path '.opencode/agents' "$Agent.md")
    $content = $null
    if ($FileReader) {
        try {
            $content = & $FileReader $agentPath
        }
        catch {
            $cause = "$_"
            if ($cause -match 'Agent definition not found|No model found in frontmatter') { throw }
            throw "Agent definition not found: $agentPath ($cause)"
        }
    }
    else {
        if (-not (Test-Path -LiteralPath $agentPath)) { throw "Agent definition not found: $agentPath" }
        $content = Get-Content -LiteralPath $agentPath -Raw
    }
    $match = ("$content" | Select-String -Pattern '(?m)^model:\s*(\S+)')
    $model = $null
    if ($null -ne $match) { $model = @($match)[0].Matches.Groups[1].Value }
    if ([string]::IsNullOrWhiteSpace("$model")) { throw "No model found in frontmatter of $agentPath" }
    return "$model"
}

function Get-AgentModelMap {
    param($Config, [Parameter(Mandatory = $true)][string]$RepoRoot, [scriptblock]$FileReader)
    $map = @{}
    $names = @()
    foreach ($entry in @($Config)) {
        foreach ($agent in @($entry.Agents)) {
            $name = "$agent"
            if ([string]::IsNullOrWhiteSpace($name) -or $map.ContainsKey($name)) { continue }
            $map[$name] = $null
            $names += $name
        }
    }
    foreach ($name in $names) {
        try {
            if ($FileReader) {
                $map[$name] = Get-AgentModel -Agent $name -RepoRoot $RepoRoot -FileReader $FileReader
            }
            else {
                $map[$name] = Get-AgentModel -Agent $name -RepoRoot $RepoRoot
            }
        }
        catch {
            $category = $null
            foreach ($entry in @($Config)) {
                if (@($entry.Agents) -contains $name) { $category = "$($entry.Id)"; break }
            }
            throw (Get-AgentModelErrorMessage -Agent $name -Category $category -Cause "$_")
        }
    }
    return $map
}

function ConvertTo-SeededStages {
    param($Config, [hashtable]$ModelMap, [scriptblock]$ModelLookup)
    $stages = @()
    foreach ($entry in @($Config)) {
        $model = $null
        $lookupError = $null
        if ($PSBoundParameters.ContainsKey('ModelMap')) {
            $model = $ModelMap["$($entry.Agents[0])"]
        }
        elseif ($ModelLookup) {
            try {
                $model = & $ModelLookup $entry.Agents[0]
            }
            catch {
                $model = $null
                $lookupError = "$_"
            }
        }
        if ([string]::IsNullOrWhiteSpace("$model")) {
            throw (Get-AgentModelErrorMessage -Agent "$($entry.Agents[0])" -Category "$($entry.Id)" -Cause $lookupError)
        }
        $stages += [ordered]@{ agent = $entry.Id; model = "$model"; category = $entry.Id; type = $entry.Type }
    }
    return $stages
}

function Get-StepCategory {
    param([string]$Agent, [int]$Iteration, $Config)
    $entries = @($Config)
    if ([string]::IsNullOrWhiteSpace($Agent) -or $entries.Count -eq 0) { return 'uncategorized' }
    # Exact match first: replay the candidate assignment (loops take the next
    # free iteration per worker, so a worker shared by two loops owns
    # distinct iterations in each) and return the owning entry.
    $used = @{}
    foreach ($entry in $entries) {
        $entryType = "$($entry.Type)".Trim().ToLowerInvariant()
        $entryAgents = @($entry.Agents)
        if ($entryType -eq 'loop') {
            $count = 0
            try { $count = [int]$entry.Iterations } catch { $count = 0 }
            for ($i = 1; $i -le $count; $i++) {
                foreach ($a in $entryAgents) {
                    if ([string]::IsNullOrWhiteSpace("$a")) { continue }
                    $u = @()
                    if ($used.ContainsKey("$a")) { $u = @($used["$a"]) }
                    $iter = 1
                    while ($u -contains $iter) { $iter++ }
                    $used["$a"] = @($u + $iter)
                    if ("$a" -eq $Agent -and $iter -eq $Iteration) { return $entry.Id }
                }
            }
        }
        else {
            foreach ($a in $entryAgents) {
                if ([string]::IsNullOrWhiteSpace("$a")) { continue }
                $u = @()
                if ($used.ContainsKey("$a")) { $u = @($used["$a"]) }
                $iter = 0
                while ($u -contains $iter) { $iter++ }
                $used["$a"] = @($u + $iter)
                if ("$a" -eq $Agent -and $iter -eq $Iteration) { return $entry.Id }
            }
        }
    }
    # Fallback for out-of-range iterations: legacy slot resolution.
    if ($Iteration -ne 0) {
        $loop = @($entries | Where-Object { $_.Type -eq 'loop' -and @($_.Agents) -contains $Agent }) | Select-Object -First 1
        if ($loop) { return $loop.Id }
    }
    $sequential = @($entries | Where-Object { $_.Type -ne 'loop' -and @($_.Agents) -contains $Agent })
    if ($sequential.Count -gt 0) {
        if ($Iteration -lt 0) { return 'uncategorized' }
        if ($Iteration -lt $sequential.Count) { return $sequential[$Iteration].Id }
        return $sequential[-1].Id
    }
    $any = @($entries | Where-Object { @($_.Agents) -contains $Agent }) | Select-Object -First 1
    if ($any) { return $any.Id }
    return 'uncategorized'
}
