#Requires -Modules Pester
BeforeAll {
    . (Join-Path $PSScriptRoot "AgentsConfig.ps1")
    $script:V1Json = @'
{
  "stages": {
    "implementation": { "type": "sequential", "agents": ["feature-builder", "test-runner"] },
    "review-loop": { "type": "loop", "agents": ["code-review", "feature-builder"], "iterations": 3 },
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
        ($entries | ForEach-Object { $_.Id }) -join ',' | Should -Be 'implementation,review-loop,quality-loop,test-rerun,agentic-review,pr-author'
        ($entries | ForEach-Object { $_.Type }) -join ',' | Should -Be 'sequential,loop,loop,sequential,sequential,sequential'
        $entries[0].Agents -join ',' | Should -Be 'feature-builder,test-runner'
        $entries[1].Agents -join ',' | Should -Be 'code-review,feature-builder'
        $entries[2].Agents -join ',' | Should -Be 'static-analysis,feature-builder'
        $entries[1].Iterations | Should -Be 3
        $entries[2].Iterations | Should -Be 3
        $entries[0].Iterations | Should -Be $null
        $entries[3].Iterations | Should -Be $null
    }
    It 'preserves arbitrary map order' {
        $json = '{"stages":{"pr-author":{"type":"sequential","agents":["pr-author"]},"implementation":{"type":"sequential","agents":["feature-builder"]},"quality-loop":{"type":"loop","agents":["static-analysis"],"iterations":2}}}'
        $entries = ConvertFrom-AgentsConfigJson -Json $json
        ($entries | ForEach-Object { $_.Id }) -join ',' | Should -Be 'pr-author,implementation,quality-loop'
    }
    It 'allows the same worker across categories' {
        $entries = ConvertFrom-AgentsConfigJson -Json $script:V1Json
        $entries.Count | Should -Be 6
        @($entries | Where-Object { $_.Agents -contains 'test-runner' }).Count | Should -Be 2
        @($entries | Where-Object { $_.Agents -contains 'feature-builder' }).Count | Should -Be 3
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
    It 'seeds the six v1 categories in map order with their models' {
        $config = ConvertFrom-AgentsConfigJson -Json $script:V1Json
        $lookup = { param($agent) return "model-$agent" }
        $stages = @(ConvertTo-SeededStages -Config $config -ModelLookup $lookup)
        $stages.Count | Should -Be 6
        ($stages | ForEach-Object { $_['category'] }) -join ',' | Should -Be 'implementation,review-loop,quality-loop,test-rerun,agentic-review,pr-author'
        ($stages | ForEach-Object { $_['agent'] }) -join ',' | Should -Be 'implementation,review-loop,quality-loop,test-rerun,agentic-review,pr-author'
        $stages[0]['model'] | Should -Be 'model-feature-builder'
        $stages[1]['model'] | Should -Be 'model-code-review'
        $stages[2]['model'] | Should -Be 'model-static-analysis'
        $stages[3]['model'] | Should -Be 'model-test-runner'
        $stages[4]['model'] | Should -Be 'model-agentic-review'
        $stages[5]['model'] | Should -Be 'model-pr-author'
    }
    It 'seeds the stage type so the dashboard reads loop membership from the payload' {
        $config = ConvertFrom-AgentsConfigJson -Json $script:V1Json
        $stages = @(ConvertTo-SeededStages -Config $config -ModelLookup { param($a) return 'm' })
        ($stages | ForEach-Object { $_['type'] }) -join ',' | Should -Be 'sequential,loop,loop,sequential,sequential,sequential'
    }
    It 'emits distinct categories when workers repeat' {
        $config = ConvertFrom-AgentsConfigJson -Json $script:V1Json
        $stages = @(ConvertTo-SeededStages -Config $config -ModelLookup { param($a) return 'm' })
        $categories = @($stages | ForEach-Object { $_['category'] } | Sort-Object -Unique)
        $categories.Count | Should -Be 6
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
            @('code-review', 1, 'review-loop'),
            @('feature-builder', 1, 'review-loop'),
            @('code-review', 3, 'review-loop'),
            @('feature-builder', 3, 'review-loop'),
            @('static-analysis', 1, 'quality-loop'),
            @('static-analysis', 3, 'quality-loop'),
            @('feature-builder', 4, 'quality-loop'),
            @('feature-builder', 6, 'quality-loop'),
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
        Get-StepCategory -Agent 'code-review' -Iteration 0 -Config $script:config | Should -Be 'review-loop'
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
        $script:MapJson = '{"stages":{"implementation":{"type":"sequential","agents":["feature-builder","test-runner"]},"review-loop":{"type":"loop","agents":["code-review","feature-builder"],"iterations":2},"quality-loop":{"type":"loop","agents":["static-analysis","feature-builder"],"iterations":2}}}'
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
        $map.Count | Should -Be 4
        $map['feature-builder'] | Should -Be 'model-feature-builder'
        $map['test-runner'] | Should -Be 'model-test-runner'
        $map['code-review'] | Should -Be 'model-code-review'
        $map['static-analysis'] | Should -Be 'model-static-analysis'
        @($script:readerCalls | Sort-Object -Unique).Count | Should -Be 4
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
            'code-review'     = 'model-code-review'
            'static-analysis' = 'model-static-analysis'
            'test-runner'     = 'model-test-runner'
            'agentic-review'  = 'model-agentic-review'
            'pr-author'       = 'model-pr-author'
        }
        $stages = @(ConvertTo-SeededStages -Config $config -ModelMap $map)
        $stages.Count | Should -Be 6
        ($stages | ForEach-Object { $_['category'] }) -join ',' | Should -Be 'implementation,review-loop,quality-loop,test-rerun,agentic-review,pr-author'
        $stages[0]['model'] | Should -Be 'model-feature-builder'
        $stages[1]['model'] | Should -Be 'model-code-review'
        $stages[2]['model'] | Should -Be 'model-static-analysis'
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
        ($entries | ForEach-Object { $_.Id }) -join ',' | Should -Be 'implementation,review-loop,static-loop,test-rerun,architecture-review,pr-author'
        $reviewLoop = @($entries | Where-Object { $_.Id -eq 'review-loop' })[0]
        $reviewLoop.Iterations | Should -Be 3
        $loop = @($entries | Where-Object { $_.Id -eq 'static-loop' })[0]
        $loop.Iterations | Should -Be 3
    }
    It 'throws for a missing file' {
        { Read-AgentsConfig -Path (Join-Path $PSScriptRoot 'no-such-agents.json') } | Should -Throw
    }
}

Describe 'ConvertFrom-AgentsWorkflowsJson' {
    BeforeAll {
        $script:CatalogJson = @'
{
  "stages": {
    "implementation": { "type": "sequential", "agents": ["feature-builder", "test-runner"] },
    "quality-loop": { "type": "loop", "agents": ["static-analysis", "feature-builder"], "iterations": 3 },
    "test-rerun": { "type": "sequential", "agents": ["test-runner"] },
    "agentic-review": { "type": "sequential", "agents": ["agentic-review"] },
    "pr-author": { "type": "sequential", "agents": ["pr-author"] }
  },
  "workflows": {
    "full": ["implementation", "quality-loop", "test-rerun", "agentic-review", "pr-author"],
    "quick": ["implementation", "pr-author"]
  },
  "defaultWorkflow": "full"
}
'@
        $script:CatalogConfig = ConvertFrom-AgentsConfigJson -Json $script:CatalogJson
    }
    It 'reads named workflows with the default marker' {
        $catalog = ConvertFrom-AgentsWorkflowsJson -Json $script:CatalogJson -Config $script:CatalogConfig
        @($catalog.Workflows.Keys) -join ',' | Should -Be 'full,quick'
        $catalog.Workflows['full'] -join ',' | Should -Be 'implementation,quality-loop,test-rerun,agentic-review,pr-author'
        $catalog.Workflows['quick'] -join ',' | Should -Be 'implementation,pr-author'
        $catalog.DefaultWorkflow | Should -Be 'full'
    }
    It 'reads a stages-only file as the default workflow in catalog order' {
        $config = ConvertFrom-AgentsConfigJson -Json $script:V1Json
        $catalog = ConvertFrom-AgentsWorkflowsJson -Json $script:V1Json -Config $config
        @($catalog.Workflows.Keys) -join ',' | Should -Be 'default'
        $catalog.Workflows['default'] -join ',' | Should -Be 'implementation,review-loop,quality-loop,test-rerun,agentic-review,pr-author'
        $catalog.DefaultWorkflow | Should -Be 'default'
    }
    It 'leaves stages parsing unchanged when workflows are present' {
        ($script:CatalogConfig | ForEach-Object { $_.Id }) -join ',' | Should -Be 'implementation,quality-loop,test-rerun,agentic-review,pr-author'
        $script:CatalogConfig[1].Iterations | Should -Be 3
    }
    It 'rejects an empty workflows map' {
        $json = '{"stages":{"a":{"type":"sequential","agents":["builder"]}},"workflows":{},"defaultWorkflow":"x"}'
        $config = ConvertFrom-AgentsConfigJson -Json '{"stages":{"a":{"type":"sequential","agents":["builder"]}}}'
        { ConvertFrom-AgentsWorkflowsJson -Json $json -Config $config } | Should -Throw '*at least one workflow*'
    }
    It 'rejects an empty workflow' {
        $json = '{"stages":{"a":{"type":"sequential","agents":["builder"]}},"workflows":{"empty":[]},"defaultWorkflow":"empty"}'
        $config = ConvertFrom-AgentsConfigJson -Json '{"stages":{"a":{"type":"sequential","agents":["builder"]}}}'
        { ConvertFrom-AgentsWorkflowsJson -Json $json -Config $config } | Should -Throw "*workflow 'empty' must list at least one stage id*"
    }
    It 'rejects a workflow referencing an unknown stage id' {
        $json = '{"stages":{"a":{"type":"sequential","agents":["builder"]}},"workflows":{"bad":["a","ghost"]},"defaultWorkflow":"bad"}'
        $config = ConvertFrom-AgentsConfigJson -Json '{"stages":{"a":{"type":"sequential","agents":["builder"]}}}'
        { ConvertFrom-AgentsWorkflowsJson -Json $json -Config $config } | Should -Throw "*references unknown stage id 'ghost'*"
    }
    It 'rejects a workflow listing a stage id twice' {
        $json = '{"stages":{"a":{"type":"sequential","agents":["builder"]}},"workflows":{"dup":["a","a"]},"defaultWorkflow":"dup"}'
        $config = ConvertFrom-AgentsConfigJson -Json '{"stages":{"a":{"type":"sequential","agents":["builder"]}}}'
        { ConvertFrom-AgentsWorkflowsJson -Json $json -Config $config } | Should -Throw "*lists stage id 'a' more than once*"
    }
    It 'rejects the reserved workflow name default' {
        $json = '{"stages":{"a":{"type":"sequential","agents":["builder"]}},"workflows":{"default":["a"]},"defaultWorkflow":"default"}'
        $config = ConvertFrom-AgentsConfigJson -Json '{"stages":{"a":{"type":"sequential","agents":["builder"]}}}'
        { ConvertFrom-AgentsWorkflowsJson -Json $json -Config $config } | Should -Throw "*reserved*"
    }
    It 'rejects workflows with no default marker instead of silently picking first' {
        $json = '{"stages":{"a":{"type":"sequential","agents":["builder"]}},"workflows":{"only":["a"]}}'
        $config = ConvertFrom-AgentsConfigJson -Json '{"stages":{"a":{"type":"sequential","agents":["builder"]}}}'
        { ConvertFrom-AgentsWorkflowsJson -Json $json -Config $config } | Should -Throw '*no default workflow marker*'
    }
    It 'rejects a default marker naming no known workflow' {
        $json = '{"stages":{"a":{"type":"sequential","agents":["builder"]}},"workflows":{"only":["a"]},"defaultWorkflow":"ghost"}'
        $config = ConvertFrom-AgentsConfigJson -Json '{"stages":{"a":{"type":"sequential","agents":["builder"]}}}'
        { ConvertFrom-AgentsWorkflowsJson -Json $json -Config $config } | Should -Throw "*names no known workflow*"
    }
    It 'rejects a default marker with no workflows map' {
        $json = '{"stages":{"a":{"type":"sequential","agents":["builder"]}},"defaultWorkflow":"full"}'
        $config = ConvertFrom-AgentsConfigJson -Json '{"stages":{"a":{"type":"sequential","agents":["builder"]}}}'
        { ConvertFrom-AgentsWorkflowsJson -Json $json -Config $config } | Should -Throw '*no workflows map*'
    }
    It 'rejects a workflow that is not an ordered list' {
        $json = '{"stages":{"a":{"type":"sequential","agents":["builder"]}},"workflows":{"odd":"a"},"defaultWorkflow":"odd"}'
        $config = ConvertFrom-AgentsConfigJson -Json '{"stages":{"a":{"type":"sequential","agents":["builder"]}}}'
        { ConvertFrom-AgentsWorkflowsJson -Json $json -Config $config } | Should -Throw "*must be an ordered list of stage ids*"
    }
    It 'accepts a valid single-stage workflow' {
        $json = '{"stages":{"a":{"type":"sequential","agents":["builder"]}},"workflows":{"only":["a"]},"defaultWorkflow":"only"}'
        $config = ConvertFrom-AgentsConfigJson -Json '{"stages":{"a":{"type":"sequential","agents":["builder"]}}}'
        $catalog = ConvertFrom-AgentsWorkflowsJson -Json $json -Config $config
        $catalog.Workflows['only'] -join ',' | Should -Be 'a'
        $catalog.DefaultWorkflow | Should -Be 'only'
    }
    It 'recovers a single-stage workflow unwrapped to a scalar by older parsers' {
        $json = '{"stages":{"a":{"type":"sequential","agents":["builder"]}},"workflows":{"only":["a"]},"defaultWorkflow":"only"}'
        $config = ConvertFrom-AgentsConfigJson -Json '{"stages":{"a":{"type":"sequential","agents":["builder"]}}}'
        $parsed = ConvertFrom-AgentsJsonText -Json $json
        $parsed.workflows.only = 'a'
        $catalog = ConvertFrom-AgentsWorkflowsObject -Parsed $parsed -Config $config -RawJson $json
        $catalog.Workflows['only'] -join ',' | Should -Be 'a'
    }
    It 'rejects a workflow with a blank stage id' {
        $json = '{"stages":{"a":{"type":"sequential","agents":["builder"]}},"workflows":{"odd":["a"," "]},"defaultWorkflow":"odd"}'
        $config = ConvertFrom-AgentsConfigJson -Json '{"stages":{"a":{"type":"sequential","agents":["builder"]}}}'
        { ConvertFrom-AgentsWorkflowsJson -Json $json -Config $config } | Should -Throw "*lists an empty stage id*"
    }
    It 'leaves workflow validation to the catalog reader instead of the stages parse' {
        $json = '{"stages":{"a":{"type":"sequential","agents":["builder"]}},"workflows":{"bad":["a","ghost"]},"defaultWorkflow":"bad"}'
        $stages = ConvertFrom-AgentsConfigJson -Json $json
        ($stages | ForEach-Object { $_.Id }) -join ',' | Should -Be 'a'
        { ConvertFrom-AgentsWorkflowsJson -Json $json -Config $stages } | Should -Throw "*references unknown stage id 'ghost'*"
    }
    It 'fails the catalog read fast on an invalid catalog' {
        $json = '{"stages":{"a":{"type":"sequential","agents":["builder"]}},"workflows":{"bad":["a","ghost"]},"defaultWorkflow":"bad"}'
        $path = Join-Path $TestDrive 'agents.json'
        Set-Content -LiteralPath $path -Value $json
        { Read-AgentsWorkflowCatalog -Path $path } | Should -Throw "*references unknown stage id 'ghost'*"
    }
}

Describe 'Select-WorkflowStages' {
    BeforeAll {
        $script:FilterConfig = ConvertFrom-AgentsConfigJson -Json $script:V1Json
    }
    It 'filters to the picked workflow preserving workflow order, not catalog order' {
        $filtered = Select-WorkflowStages -Config $script:FilterConfig -StageIds @('pr-author', 'implementation')
        ($filtered | ForEach-Object { $_.Id }) -join ',' | Should -Be 'pr-author,implementation'
        ($filtered | ForEach-Object { $_.Type }) -join ',' | Should -Be 'sequential,sequential'
        $filtered[1].Agents -join ',' | Should -Be 'feature-builder,test-runner'
    }
    It 'keeps loop iteration counts on the filtered stages' {
        $filtered = Select-WorkflowStages -Config $script:FilterConfig -StageIds @('quality-loop')
        $filtered.Count | Should -Be 1
        $filtered[0].Iterations | Should -Be 3
    }
    It 'seeds only the picked workflow stages in workflow order' {
        $catalog = ConvertFrom-AgentsWorkflowsJson -Json $script:V1Json -Config $script:FilterConfig
        $filtered = Select-WorkflowStages -Config $script:FilterConfig -StageIds @('test-rerun', 'implementation')
        $stages = @(ConvertTo-SeededStages -Config $filtered -ModelLookup { param($a) return "model-$a" })
        ($stages | ForEach-Object { $_['category'] }) -join ',' | Should -Be 'test-rerun,implementation'
        $stages[0]['model'] | Should -Be 'model-test-runner'
        $stages[1]['model'] | Should -Be 'model-feature-builder'
        $catalog.DefaultWorkflow | Should -Be 'default'
    }
    It 'fails fast on an unknown stage id, naming the workflow when given' {
        { Select-WorkflowStages -Config $script:FilterConfig -StageIds @('ghost') -Workflow 'quick' } | Should -Throw "*workflow 'quick' references unknown stage id 'ghost'*"
        { Select-WorkflowStages -Config $script:FilterConfig -StageIds @('ghost') } | Should -Throw "*unknown stage id 'ghost'*"
    }
    It 'fails fast on an empty selection instead of seeding nothing' {
        { Select-WorkflowStages -Config $script:FilterConfig -StageIds @() -Workflow 'quick' } | Should -Throw "*workflow 'quick' must list at least one stage id*"
    }
    It 'fails fast on a duplicated stage id' {
        { Select-WorkflowStages -Config $script:FilterConfig -StageIds @('implementation', 'implementation') } | Should -Throw "*more than once*"
    }
}

Describe 'Read-AgentsWorkflowCatalog' {
    BeforeAll {
        $script:repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
    }
    It 'reads the repo-root named workflows with the default marker' {
        $catalog = Read-AgentsWorkflowCatalog -Path (Join-Path $script:repoRoot 'agents.json')
        ($catalog.Stages | ForEach-Object { $_.Id }) -join ',' | Should -Be 'implementation,review-loop,static-loop,test-rerun,architecture-review,pr-author'
        @($catalog.Workflows.Keys) -join ',' | Should -Be 'full,quick'
        $catalog.Workflows['full'] -join ',' | Should -Be 'implementation,review-loop,static-loop,test-rerun,architecture-review,pr-author'
        $catalog.Workflows['quick'] -join ',' | Should -Be 'implementation,pr-author'
        $catalog.DefaultWorkflow | Should -Be 'full'
    }
    It 'throws for a missing file' {
        { Read-AgentsWorkflowCatalog -Path (Join-Path $PSScriptRoot 'no-such-agents.json') } | Should -Throw
    }
}
