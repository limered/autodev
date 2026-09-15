#Requires -Modules Pester
BeforeAll {
    . (Join-Path $PSScriptRoot "AgentsConfig.ps1")
    $script:V1Json = @'
{
  "stages": {
    "implementation": { "type": "sequential", "agents": ["feature-builder", "test-runner"] },
    "quality-loop": { "type": "loop", "agents": ["static-analysis", "feature-builder"], "iterations": 3 },
    "test-rerun": { "type": "sequential", "agents": ["test-runner"] },
    "agentic-review": { "type": "sequential", "agents": ["agentic-review"] },
    "pr-author": { "type": "sequential", "agents": ["pr-author"] }
  }
}
'@
}

Describe 'ConvertFrom-AgentsConfigJson' {
    It 'parses the v1 set in map order with the loop iteration count' {
        $entries = ConvertFrom-AgentsConfigJson -Json $script:V1Json
        ($entries | ForEach-Object { $_.Id }) -join ',' | Should -Be 'implementation,quality-loop,test-rerun,agentic-review,pr-author'
        ($entries | ForEach-Object { $_.Type }) -join ',' | Should -Be 'sequential,loop,sequential,sequential,sequential'
        $entries[0].Agents -join ',' | Should -Be 'feature-builder,test-runner'
        $entries[1].Agents -join ',' | Should -Be 'static-analysis,feature-builder'
        $entries[1].Iterations | Should -Be 3
        $entries[0].Iterations | Should -Be $null
        $entries[2].Iterations | Should -Be $null
    }
    It 'preserves arbitrary map order' {
        $json = '{"stages":{"pr-author":{"type":"sequential","agents":["pr-author"]},"implementation":{"type":"sequential","agents":["feature-builder"]},"quality-loop":{"type":"loop","agents":["static-analysis"],"iterations":2}}}'
        $entries = ConvertFrom-AgentsConfigJson -Json $json
        ($entries | ForEach-Object { $_.Id }) -join ',' | Should -Be 'pr-author,implementation,quality-loop'
    }
    It 'allows the same worker across categories' {
        $entries = ConvertFrom-AgentsConfigJson -Json $script:V1Json
        $entries.Count | Should -Be 5
        @($entries | Where-Object { $_.Agents -contains 'test-runner' }).Count | Should -Be 2
        @($entries | Where-Object { $_.Agents -contains 'feature-builder' }).Count | Should -Be 2
    }
    It 'rejects parallel without coercion' {
        $json = '{"stages":{"a":{"type":"parallel","agents":["feature-builder"]}}}'
        { ConvertFrom-AgentsConfigJson -Json $json } | Should -Throw '*expected sequential or loop*'
    }
    It 'throws on invalid JSON' {
        { ConvertFrom-AgentsConfigJson -Json 'not json' } | Should -Throw
    }
    It 'throws when the stages map is missing or empty' {
        { ConvertFrom-AgentsConfigJson -Json '{}' } | Should -Throw
        { ConvertFrom-AgentsConfigJson -Json '{"stages":{}}' } | Should -Throw
    }
    It 'throws on unknown type' {
        $json = '{"stages":{"a":{"type":"fan-out","agents":["feature-builder"]}}}'
        { ConvertFrom-AgentsConfigJson -Json $json } | Should -Throw
    }
    It 'throws when a loop declares no iteration count' {
        $json = '{"stages":{"quality-loop":{"type":"loop","agents":["static-analysis"]}}}'
        { ConvertFrom-AgentsConfigJson -Json $json } | Should -Throw
    }
    It 'throws on a non-positive iteration count' {
        foreach ($n in @('0', '-1', '2.5')) {
            $json = '{"stages":{"quality-loop":{"type":"loop","agents":["static-analysis"],"iterations":' + $n + '}}}'
            { ConvertFrom-AgentsConfigJson -Json $json } | Should -Throw
        }
    }
    It 'throws when a stage lists no member agents' {
        $json = '{"stages":{"a":{"type":"sequential","agents":[]}}}'
        { ConvertFrom-AgentsConfigJson -Json $json } | Should -Throw
        $missing = '{"stages":{"a":{"type":"sequential"}}}'
        { ConvertFrom-AgentsConfigJson -Json $missing } | Should -Throw
    }
}

Describe 'ConvertTo-SeededStages' {
    It 'seeds the five v1 categories in map order with their models' {
        $config = ConvertFrom-AgentsConfigJson -Json $script:V1Json
        $lookup = { param($agent) return "model-$agent" }
        $stages = @(ConvertTo-SeededStages -Config $config -ModelLookup $lookup)
        $stages.Count | Should -Be 5
        ($stages | ForEach-Object { $_['category'] }) -join ',' | Should -Be 'implementation,quality-loop,test-rerun,agentic-review,pr-author'
        ($stages | ForEach-Object { $_['agent'] }) -join ',' | Should -Be 'implementation,quality-loop,test-rerun,agentic-review,pr-author'
        $stages[0]['model'] | Should -Be 'model-feature-builder'
        $stages[1]['model'] | Should -Be 'model-static-analysis'
        $stages[2]['model'] | Should -Be 'model-test-runner'
        $stages[3]['model'] | Should -Be 'model-agentic-review'
        $stages[4]['model'] | Should -Be 'model-pr-author'
    }
    It 'seeds the stage type so the dashboard reads loop membership from the payload' {
        $config = ConvertFrom-AgentsConfigJson -Json $script:V1Json
        $stages = @(ConvertTo-SeededStages -Config $config -ModelLookup { param($a) return 'm' })
        ($stages | ForEach-Object { $_['type'] }) -join ',' | Should -Be 'sequential,loop,sequential,sequential,sequential'
    }
    It 'emits distinct categories when workers repeat' {
        $config = ConvertFrom-AgentsConfigJson -Json $script:V1Json
        $stages = @(ConvertTo-SeededStages -Config $config -ModelLookup { param($a) return 'm' })
        $categories = @($stages | ForEach-Object { $_['category'] } | Sort-Object -Unique)
        $categories.Count | Should -Be 5
    }
    It 'throws when a model is missing' {
        $config = ConvertFrom-AgentsConfigJson -Json $script:V1Json
        { ConvertTo-SeededStages -Config $config -ModelLookup { param($a) return $null } } | Should -Throw
    }
    It 'surfaces the lookup failure inside the throw' {
        $config = ConvertFrom-AgentsConfigJson -Json $script:V1Json
        $failing = { param($a) throw "Agent definition not found: /x/$a.md" }
        try {
            ConvertTo-SeededStages -Config $config -ModelLookup $failing
            throw "expected ConvertTo-SeededStages to throw"
        }
        catch {
            "$_" | Should -Match "feature-builder"
            "$_" | Should -Match "Agent definition not found"
        }
    }
}

Describe 'Get-StepCategory' {
    BeforeAll {
        $script:config = ConvertFrom-AgentsConfigJson -Json $script:V1Json
    }
    It 'maps the v1 phases to their seeded slots' {
        $pairs = @(
            @('feature-builder', 0, 'implementation'),
            @('test-runner', 0, 'implementation'),
            @('static-analysis', 1, 'quality-loop'),
            @('feature-builder', 1, 'quality-loop'),
            @('static-analysis', 3, 'quality-loop'),
            @('feature-builder', 3, 'quality-loop'),
            @('test-runner', 1, 'test-rerun'),
            @('agentic-review', 0, 'agentic-review'),
            @('pr-author', 0, 'pr-author')
        )
        foreach ($pair in $pairs) {
            $agent, $iteration, $expected = $pair
            Get-StepCategory -Agent $agent -Iteration $iteration -Config $script:config | Should -Be $expected
        }
    }
    It 'lands a worker no slot names in the uncategorized bucket' {
        Get-StepCategory -Agent 'no-such-agent' -Iteration 0 -Config $script:config | Should -Be 'uncategorized'
    }
    It 'pins the uncategorized literal shared across tiers' {
        Get-StepCategory -Agent '' -Iteration 0 -Config $script:config | Should -Be 'uncategorized'
        Get-StepCategory -Agent '   ' -Iteration 0 -Config $script:config | Should -Be 'uncategorized'
        Get-StepCategory -Agent 'no-such-agent' -Iteration 0 -Config $script:config | Should -BeExactly 'uncategorized'
    }
    It 'clamps a repeated worker past its slots to the last matching slot' {
        Get-StepCategory -Agent 'test-runner' -Iteration 5 -Config $script:config | Should -Be 'test-rerun'
    }
    It 'clamps a single-slot worker past its slot to that slot' {
        Get-StepCategory -Agent 'pr-author' -Iteration 5 -Config $script:config | Should -Be 'pr-author'
    }
    It 'maps a loop-only worker on pass zero to its loop slot' {
        Get-StepCategory -Agent 'static-analysis' -Iteration 0 -Config $script:config | Should -Be 'quality-loop'
    }
    It 'derives sequential slots from map order, not hardcoded names' {
        $json = '{"stages":{"build":{"type":"sequential","agents":["builder"]},"retest":{"type":"sequential","agents":["builder"]}}}'
        $custom = ConvertFrom-AgentsConfigJson -Json $json
        Get-StepCategory -Agent 'builder' -Iteration 0 -Config $custom | Should -Be 'build'
        Get-StepCategory -Agent 'builder' -Iteration 1 -Config $custom | Should -Be 'retest'
    }
}

Describe 'Get-AgentModel' {
    It 'reads the model from frontmatter through the injected reader' {
        $reader = { param($p) return "---`ndescription: x`nmode: primary`nmodel: opencode-go/muse-spark-1.3-contributor`n---`nbody" }
        Get-AgentModel -Agent 'feature-builder' -RepoRoot '/repo' -FileReader $reader | Should -Be 'opencode-go/muse-spark-1.3-contributor'
    }
    It 'passes the agent definition path to the reader' {
        $script:seenAgentPath = $null
        $reader = { param($p) $script:seenAgentPath = "$p"; return "---`nmodel: m`n---" }
        Get-AgentModel -Agent 'test-runner' -RepoRoot '/repo' -FileReader $reader | Should -Be 'm'
        "$script:seenAgentPath" | Should -Match 'test-runner\.md$'
    }
    It 'throws when the definition is absent' {
        { Get-AgentModel -Agent 'ghost' -RepoRoot '/nope' -FileReader { param($p) throw "missing $p" } } | Should -Throw '*Agent definition not found*'
    }
    It 'throws when frontmatter carries no model' {
        { Get-AgentModel -Agent 'x' -RepoRoot '/repo' -FileReader { param($p) return "---`ndescription: x`n---`nbody" } } | Should -Throw '*No model found in frontmatter*'
    }
}

Describe 'Get-AgentModelMap' {
    BeforeAll {
        $script:MapJson = '{"stages":{"implementation":{"type":"sequential","agents":["feature-builder","test-runner"]},"quality-loop":{"type":"loop","agents":["static-analysis","feature-builder"],"iterations":2}}}'
        $script:MapConfig = ConvertFrom-AgentsConfigJson -Json $script:MapJson
        $script:MapReader = {
            param($p)
            $name = [System.IO.Path]::GetFileNameWithoutExtension("$p")
            return "---`ndescription: d`nmodel: model-$name`n---"
        }
    }
    It 'resolves each distinct agent once' {
        $script:readerCalls = @()
        $counting = {
            param($p)
            $script:readerCalls += "$p"
            return (& $script:MapReader $p)
        }
        $map = Get-AgentModelMap -Config $script:MapConfig -RepoRoot '/repo' -FileReader $counting
        $map.Count | Should -Be 3
        $map['feature-builder'] | Should -Be 'model-feature-builder'
        $map['test-runner'] | Should -Be 'model-test-runner'
        $map['static-analysis'] | Should -Be 'model-static-analysis'
        @($script:readerCalls | Sort-Object -Unique).Count | Should -Be 3
    }
    It 'throws the shared shape naming agent and category when a definition is missing' {
        $reader = {
            param($p)
            $name = [System.IO.Path]::GetFileNameWithoutExtension("$p")
            if ($name -eq 'ghost') { throw "boom-$name" }
            return "---`nmodel: m-$name`n---"
        }
        $config = ConvertFrom-AgentsConfigJson -Json '{"stages":{"implementation":{"type":"sequential","agents":["ghost"]}}}'
        try {
            Get-AgentModelMap -Config $config -RepoRoot '/repo' -FileReader $reader
            throw 'expected Get-AgentModelMap to throw'
        }
        catch {
            "$_" | Should -Match "No model found for agent 'ghost'"
            "$_" | Should -Match "category 'implementation'"
            "$_" | Should -Match 'boom-ghost'
        }
    }
    It 'throws the same shape when frontmatter carries no model' {
        $config = ConvertFrom-AgentsConfigJson -Json '{"stages":{"implementation":{"type":"sequential","agents":["ghost"]}}}'
        try {
            Get-AgentModelMap -Config $config -RepoRoot '/repo' -FileReader { param($p) return "---`ndescription: x`n---" }
            throw 'expected Get-AgentModelMap to throw'
        }
        catch {
            "$_" | Should -Match "No model found for agent 'ghost'"
            "$_" | Should -Match "category 'implementation'"
        }
    }
}

Describe 'ConvertTo-SeededStages ModelMap' {
    It 'seeds from the shared map' {
        $config = ConvertFrom-AgentsConfigJson -Json $script:V1Json
        $map = @{
            'feature-builder' = 'model-feature-builder'
            'static-analysis' = 'model-static-analysis'
            'test-runner'     = 'model-test-runner'
            'agentic-review'  = 'model-agentic-review'
            'pr-author'       = 'model-pr-author'
        }
        $stages = @(ConvertTo-SeededStages -Config $config -ModelMap $map)
        $stages.Count | Should -Be 5
        ($stages | ForEach-Object { $_['category'] }) -join ',' | Should -Be 'implementation,quality-loop,test-rerun,agentic-review,pr-author'
        $stages[0]['model'] | Should -Be 'model-feature-builder'
        $stages[1]['model'] | Should -Be 'model-static-analysis'
    }
    It 'throws the shared error shape on a map miss' {
        $config = ConvertFrom-AgentsConfigJson -Json $script:V1Json
        try {
            ConvertTo-SeededStages -Config $config -ModelMap @{}
            throw 'expected ConvertTo-SeededStages to throw'
        }
        catch {
            "$_" | Should -Match "No model found for agent 'feature-builder'"
            "$_" | Should -Match "category 'implementation'"
        }
    }
}

Describe 'Read-AgentsConfig' {
    BeforeAll {
        $script:repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
    }
    It 'reads the repo-root v1 set in map order' {
        $entries = Read-AgentsConfig -Path (Join-Path $script:repoRoot 'agents.json')
        ($entries | ForEach-Object { $_.Id }) -join ',' | Should -Be 'implementation,quality-loop,test-rerun,agentic-review,pr-author'
        $loop = @($entries | Where-Object { $_.Id -eq 'quality-loop' })[0]
        $loop.Iterations | Should -Be 3
    }
    It 'throws for a missing file' {
        { Read-AgentsConfig -Path (Join-Path $PSScriptRoot 'no-such-agents.json') } | Should -Throw
    }
}
