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
    It 'treats parallel as sequential' {
        $json = '{"stages":{"a":{"type":"parallel","agents":["feature-builder"]}}}'
        $entries = ConvertFrom-AgentsConfigJson -Json $json
        $entries[0].Type | Should -Be 'sequential'
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
