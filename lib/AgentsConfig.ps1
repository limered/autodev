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
  Pure converters take strings or objects so Pester drives them without files.
#>

function ConvertFrom-AgentsConfigJson {
    param([Parameter(Mandatory = $true)][string]$Json)
    $parsed = $null
    try {
        $parsed = $Json | ConvertFrom-Json -ErrorAction Stop
    }
    catch {
        throw "Agents config is not valid JSON: $_"
    }
    if ($null -eq $parsed -or $null -eq $parsed.stages) {
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
        if ($rawType -notin @('sequential', 'loop', 'parallel')) {
            throw "Agents config stage '$id' has unknown type '$($node.type)' (expected sequential or loop)"
        }
        $type = $rawType
        if ($type -eq 'parallel') {
            $type = 'sequential'
        }
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

function Read-AgentsConfig {
    param([Parameter(Mandatory = $true)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Agents config not found: $Path"
    }
    $raw = Get-Content -LiteralPath $Path -Raw
    return ConvertFrom-AgentsConfigJson -Json $raw
}

function ConvertTo-SeededStages {
    param($Config, [scriptblock]$ModelLookup)
    $stages = @()
    foreach ($entry in @($Config)) {
        $model = $null
        $lookupError = $null
        if ($ModelLookup) {
            try {
                $model = & $ModelLookup $entry.Agents[0]
            }
            catch {
                $model = $null
                $lookupError = "$_"
            }
        }
        if ([string]::IsNullOrWhiteSpace("$model")) {
            $cause = if ($lookupError) { ": $lookupError" } else { " (lookup returned empty)" }
            throw "No model found for agent '$($entry.Agents[0])' (category '$($entry.Id)')$cause"
        }
        $stages += [ordered]@{ agent = $entry.Id; model = "$model"; category = $entry.Id }
    }
    return $stages
}
