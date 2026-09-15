#Requires -Modules Pester
BeforeAll {
    . (Join-Path $PSScriptRoot "PhaseSteps.ps1")
}

Describe 'ConvertTo-PhaseTokens' {
    It 'sums step_finish usage across turns' {
        $content = @'
{"type":"step_start","session":"a"}
{"type":"step_finish","usage":{"inputTokens":100,"outputTokens":50}}
{"type":"step_finish","usage":{"inputTokens":10,"outputTokens":5}}
'@
        $tokens = ConvertTo-PhaseTokens -Content $content
        $tokens.InputTokens | Should -Be 110
        $tokens.OutputTokens | Should -Be 55
        $tokens.Cost | Should -Be $null
    }
    It 'accepts snake_case usage and event-level cost' {
        $content = '{"type":"step_finish","usage":{"input_tokens":7,"output_tokens":3},"cost":0.001}'
        $tokens = ConvertTo-PhaseTokens -Content $content
        $tokens.InputTokens | Should -Be 7
        $tokens.OutputTokens | Should -Be 3
        $tokens.Cost | Should -Be 0.001
    }
    It 'skips non-JSON and non-step_finish lines' {
        $content = "opencode v1.2.3`n" + '{"type":"step_finish","usage":{"inputTokens":4,"outputTokens":2}}' + "`n" + 'truncated {"type":"step_fin'
        $tokens = ConvertTo-PhaseTokens -Content $content
        $tokens.InputTokens | Should -Be 4
        $tokens.OutputTokens | Should -Be 2
    }
    It 'returns zeros for empty or usage-less content' {
        $tokens = ConvertTo-PhaseTokens -Content ''
        $tokens.InputTokens | Should -Be 0
        $tokens.OutputTokens | Should -Be 0
        $tokens.Cost | Should -Be $null
        $other = ConvertTo-PhaseTokens -Content '{"type":"step_finish"}'
        $other.InputTokens | Should -Be 0
    }
    It 'sums live part.tokens with part.cost and no usage key' {
        $content = @'
{"type":"step_finish","part":{"type":"step-finish","tokens":{"total":150,"input":100,"output":50,"reasoning":0,"cache":0},"cost":0.5}}
{"type":"step_finish","part":{"type":"step-finish","tokens":{"total":30,"input":20,"output":10,"reasoning":0,"cache":0},"cost":0.25}}
'@
        $tokens = ConvertTo-PhaseTokens -Content $content
        $tokens.InputTokens | Should -Be 120
        $tokens.OutputTokens | Should -Be 60
        $tokens.Cost | Should -Be 0.75
    }
    It 'picks up part.cost when usage and event cost are absent' {
        $content = '{"type":"step_finish","usage":{"inputTokens":5,"outputTokens":2},"part":{"type":"step-finish","tokens":{"total":7,"input":5,"output":2},"cost":0.004}}'
        $tokens = ConvertTo-PhaseTokens -Content $content
        $tokens.InputTokens | Should -Be 5
        $tokens.OutputTokens | Should -Be 2
        $tokens.Cost | Should -Be 0.004
    }
    It 'prefers usage over part.tokens without double-counting' {
        $content = '{"type":"step_finish","usage":{"inputTokens":10,"outputTokens":4,"cost":0.01},"part":{"type":"step-finish","tokens":{"input":100,"output":40},"cost":0.02},"cost":0.03}'
        $tokens = ConvertTo-PhaseTokens -Content $content
        $tokens.InputTokens | Should -Be 10
        $tokens.OutputTokens | Should -Be 4
        $tokens.Cost | Should -Be 0.01
    }
}

Describe 'ConvertTo-PhaseMeta' {
    It 'reads durationMs and done status' {
        $meta = ConvertTo-PhaseMeta -Content '{"agent":"feature-builder","iteration":0,"durationMs":61000,"status":"done"}'
        $meta.DurationMs | Should -Be 61000
        $meta.Status | Should -Be 'done'
    }
    It 'reads failed status' {
        $meta = ConvertTo-PhaseMeta -Content '{"agent":"test-runner","iteration":1,"durationMs":5,"status":"failed"}'
        $meta.Status | Should -Be 'failed'
    }
    It 'returns null for garbage or missing duration' {
        ConvertTo-PhaseMeta -Content 'not json' | Should -Be $null
        ConvertTo-PhaseMeta -Content '{"agent":"x"}' | Should -Be $null
    }
}

Describe 'Get-PhaseStepCandidates' {
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
        $script:V1Config = ConvertFrom-AgentsConfigJson -Json $script:V1Json
    }
    It 'expands the v1 config in map order with distinct iteration keys' {
        $candidates = Get-PhaseStepCandidates -Config $script:V1Config
        $candidates.Count | Should -Be 11
        $pairs = @($candidates | ForEach-Object { "$($_.Agent)/$($_.Iteration)" })
        $pairs -join ',' | Should -Be 'feature-builder/0,test-runner/0,static-analysis/1,feature-builder/1,static-analysis/2,feature-builder/2,static-analysis/3,feature-builder/3,test-runner/1,agentic-review/0,pr-author/0'
    }
    It 'emits sequential members at iteration 0 in map order' {
        $config = ConvertFrom-AgentsConfigJson -Json '{"stages":{"a":{"type":"sequential","agents":["alpha","beta"]},"b":{"type":"sequential","agents":["gamma"]}}}'
        $pairs = @((Get-PhaseStepCandidates -Config $config) | ForEach-Object { "$($_.Agent)/$($_.Iteration)" })
        $pairs -join ',' | Should -Be 'alpha/0,beta/0,gamma/0'
    }
    It 'emits loop members at 1..N in member order per pass' {
        $config = ConvertFrom-AgentsConfigJson -Json '{"stages":{"q":{"type":"loop","agents":["scan","fix"],"iterations":2}}}'
        $pairs = @((Get-PhaseStepCandidates -Config $config) | ForEach-Object { "$($_.Agent)/$($_.Iteration)" })
        $pairs -join ',' | Should -Be 'scan/1,fix/1,scan/2,fix/2'
    }
    It 'bumps a repeated sequential worker to the next free iteration key' {
        $config = ConvertFrom-AgentsConfigJson -Json '{"stages":{"a":{"type":"sequential","agents":["builder"]},"b":{"type":"sequential","agents":["builder"]}}}'
        $pairs = @((Get-PhaseStepCandidates -Config $config) | ForEach-Object { "$($_.Agent)/$($_.Iteration)" })
        $pairs -join ',' | Should -Be 'builder/0,builder/1'
    }
    It 'returns nothing for an empty config' {
        @(Get-PhaseStepCandidates -Config @()).Count | Should -Be 0
    }
}

Describe 'Get-VmPhaseTokens Executor Seam' {
    It 'cats the per-phase jsonl and parses usage' {
        $script:seen = $null
        $fake = {
            param($a)
            $script:seen = $a
            return '{"type":"step_finish","usage":{"inputTokens":9,"outputTokens":1}}'
        }
        $tokens = Get-VmPhaseTokens -VmName 'v1' -Agent 'feature-builder' -Iteration 0 -Executor $fake
        ($script:seen -join ' ') | Should -Be 'exec v1 -- cat /tmp/phase-feature-builder-0.jsonl'
        $tokens.InputTokens | Should -Be 9
        $tokens.OutputTokens | Should -Be 1
    }
    It 'returns null when the phase file is absent' {
        $fake = { param($a) throw 'multipass failed with exit code 1' }
        Get-VmPhaseTokens -VmName 'v1' -Agent 'static-analysis' -Iteration 3 -Executor $fake | Should -Be $null
    }
    It 'cats the per-iteration file for loop passes' {
        $script:seen = $null
        $fake = {
            param($a)
            $script:seen = $a
            return '{"agent":"x","iteration":2,"durationMs":42,"status":"done"}'
        }
        $meta = Get-VmPhaseMeta -VmName 'v1' -Agent 'static-analysis' -Iteration 2 -Executor $fake
        ($script:seen -join ' ') | Should -Be 'exec v1 -- cat /tmp/phase-static-analysis-2.meta.json'
        $meta.DurationMs | Should -Be 42
    }
    It 'returns null when the meta file is absent' {
        $fake = { param($a) throw 'multipass failed with exit code 1' }
        Get-VmPhaseMeta -VmName 'v1' -Agent 'pr-author' -Iteration 0 -Executor $fake | Should -Be $null
    }
}

Describe 'Send-PhaseFinishedSteps relay' {
    BeforeAll {
        $repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
        . (Join-Path $repoRoot 'factory-report.ps1')
        . (Join-Path $PSScriptRoot 'AgentsConfig.ps1')
        $script:RelayConfig = Read-AgentsConfig -Path (Join-Path $repoRoot 'agents.json')
    }
    It 'relays landed phases with host-attached model and skips the rest' {
        $files = @{
            '/tmp/phase-feature-builder-0.meta.json' = '{"agent":"feature-builder","iteration":0,"durationMs":61000,"status":"done"}'
            '/tmp/phase-feature-builder-0.jsonl'     = '{"type":"step_finish","usage":{"inputTokens":100,"outputTokens":50},"cost":0.0123}'
            '/tmp/phase-test-runner-0.meta.json'     = '{"agent":"test-runner","iteration":0,"durationMs":2000,"status":"failed"}'
            '/tmp/phase-test-runner-0.jsonl'         = '{"type":"step_finish","usage":{"inputTokens":9,"outputTokens":1}}'
        }
        $fakeCat = { param($a) $path = $a[-1]; if ($files.ContainsKey($path)) { return $files[$path] }; throw "missing $path" }
        $fakeModels = { param($agent) if ($agent -eq 'feature-builder') { return 'm1' }; return $null }
        $script:relayed = @()
        $fakeRelay = { param($fields) $script:relayed += $fields }
        Send-PhaseFinishedSteps -RunId 'r1' -VmName 'v1' -Config $script:RelayConfig -ModelLookup $fakeModels -Executor $fakeCat -Relay $fakeRelay
        $script:relayed.Count | Should -Be 1 # test-runner skipped: model-less is never sent
        $only = $script:relayed[0]
        $only['agent'] | Should -Be 'feature-builder'
        $only['iteration'] | Should -Be 0
        $only['durationMs'] | Should -Be 61000
        $only['inputTokens'] | Should -Be 100
        $only['outputTokens'] | Should -Be 50
        $only['status'] | Should -Be 'done'
        $only['model'] | Should -Be 'm1'
        $only['cost'] | Should -Be 0.0123
    }
    It 'omits cost when the phase carried none and keeps failed status' {
        $files = @{
            '/tmp/phase-test-runner-0.meta.json' = '{"agent":"test-runner","iteration":0,"durationMs":2000,"status":"failed"}'
            '/tmp/phase-test-runner-0.jsonl'     = '{"type":"step_finish","usage":{"inputTokens":9,"outputTokens":1}}'
        }
        $fakeCat = { param($a) $path = $a[-1]; if ($files.ContainsKey($path)) { return $files[$path] }; throw "missing $path" }
        $fakeModels = { param($agent) return 'm9' }
        $script:relayed = @()
        $fakeRelay = { param($fields) $script:relayed += $fields }
        $tiny = ConvertFrom-AgentsConfigJson -Json '{"stages":{"implementation":{"type":"sequential","agents":["test-runner"]}}}'
        Send-PhaseFinishedSteps -RunId 'r1' -VmName 'v1' -Config $tiny -ModelLookup $fakeModels -Executor $fakeCat -Relay $fakeRelay
        $script:relayed.Count | Should -Be 1
        $script:relayed[0]['status'] | Should -Be 'failed'
        $script:relayed[0].ContainsKey('cost') | Should -Be $false
    }
    It 'relays nothing when no phase file exists' {
        $fakeCat = { param($a) throw 'missing' }
        $script:relayed = @()
        $fakeRelay = { param($fields) $script:relayed += $fields }
        { Send-PhaseFinishedSteps -RunId 'r1' -VmName 'v1' -Config $script:RelayConfig -ModelLookup { param($a) return 'm' } -Executor $fakeCat -Relay $fakeRelay } | Should -Not -Throw
        $script:relayed.Count | Should -Be 0
    }
    It 'walks candidates in Seam order with per-iteration file names' {
        $config = ConvertFrom-AgentsConfigJson -Json '{"stages":{"a":{"type":"sequential","agents":["alpha"]},"q":{"type":"loop","agents":["scan"],"iterations":2}}}'
        $script:seenPaths = @()
        $fakeCat = { param($a) $script:seenPaths += $a[-1]; throw 'missing' }
        $fakeRelay = { param($fields) }
        Send-PhaseFinishedSteps -RunId 'r1' -VmName 'v1' -Config $config -ModelLookup { param($a) return 'm' } -Executor $fakeCat -Relay $fakeRelay
        $script:seenPaths -join ',' | Should -Be '/tmp/phase-alpha-0.meta.json,/tmp/phase-scan-1.meta.json,/tmp/phase-scan-2.meta.json'
    }
    It 'attaches the seeded category from the lookup' {
        $files = @{
            '/tmp/phase-feature-builder-0.meta.json' = '{"agent":"feature-builder","iteration":0,"durationMs":61000,"status":"done"}'
            '/tmp/phase-feature-builder-0.jsonl'     = '{"type":"step_finish","usage":{"inputTokens":100,"outputTokens":50}}'
            '/tmp/phase-test-runner-1.meta.json'     = '{"agent":"test-runner","iteration":1,"durationMs":2000,"status":"done"}'
            '/tmp/phase-test-runner-1.jsonl'         = '{"type":"step_finish","usage":{"inputTokens":9,"outputTokens":1}}'
        }
        $fakeCat = { param($a) $path = $a[-1]; if ($files.ContainsKey($path)) { return $files[$path] }; throw "missing $path" }
        $lookup = { param($agent, $iteration) if ($agent -eq 'test-runner') { return 'test-rerun' }; return 'implementation' }
        $script:relayed = @()
        $fakeRelay = { param($fields) $script:relayed += $fields }
        Send-PhaseFinishedSteps -RunId 'r1' -VmName 'v1' -Config $script:RelayConfig -ModelLookup { param($a) return 'm' } -Executor $fakeCat -Relay $fakeRelay -CategoryLookup $lookup
        $script:relayed.Count | Should -Be 2
        $script:relayed[0]['category'] | Should -Be 'implementation'
        $script:relayed[1]['category'] | Should -Be 'test-rerun'
    }
    It 'attaches the uncategorized bucket from the lookup without skipping the step' {
        $files = @{
            '/tmp/phase-agentic-review-0.meta.json' = '{"agent":"agentic-review","iteration":0,"durationMs":1000,"status":"done"}'
            '/tmp/phase-agentic-review-0.jsonl'     = '{"type":"step_finish","usage":{"inputTokens":9,"outputTokens":1}}'
        }
        $fakeCat = { param($a) $path = $a[-1]; if ($files.ContainsKey($path)) { return $files[$path] }; throw "missing $path" }
        $script:relayed = @()
        $fakeRelay = { param($fields) $script:relayed += $fields }
        Send-PhaseFinishedSteps -RunId 'r1' -VmName 'v1' -Config $script:RelayConfig -ModelLookup { param($a) return 'm' } -Executor $fakeCat -Relay $fakeRelay -CategoryLookup { param($a, $i) return 'uncategorized' }
        $script:relayed.Count | Should -Be 1
        $script:relayed[0]['category'] | Should -Be 'uncategorized'
    }
    It 'relays from the shared model map and skips a map miss without throwing' {
        $files = @{
            '/tmp/phase-feature-builder-0.meta.json' = '{"agent":"feature-builder","iteration":0,"durationMs":61000,"status":"done"}'
            '/tmp/phase-feature-builder-0.jsonl'     = '{"type":"step_finish","usage":{"inputTokens":100,"outputTokens":50}}'
            '/tmp/phase-test-runner-0.meta.json'     = '{"agent":"test-runner","iteration":0,"durationMs":2000,"status":"done"}'
            '/tmp/phase-test-runner-0.jsonl'         = '{"type":"step_finish","usage":{"inputTokens":9,"outputTokens":1}}'
        }
        $fakeCat = { param($a) $path = $a[-1]; if ($files.ContainsKey($path)) { return $files[$path] }; throw "missing $path" }
        $script:relayed = @()
        $fakeRelay = { param($fields) $script:relayed += $fields }
        $tiny = ConvertFrom-AgentsConfigJson -Json '{"stages":{"implementation":{"type":"sequential","agents":["feature-builder","test-runner"]}}}'
        { Send-PhaseFinishedSteps -RunId 'r1' -VmName 'v1' -Config $tiny -ModelMap @{ 'feature-builder' = 'm1' } -Executor $fakeCat -Relay $fakeRelay } | Should -Not -Throw
        $script:relayed.Count | Should -Be 1
        $script:relayed[0]['agent'] | Should -Be 'feature-builder'
        $script:relayed[0]['model'] | Should -Be 'm1'
    }
    It 'omits the category when there is no lookup or it misses' {
        $files = @{
            '/tmp/phase-feature-builder-0.meta.json' = '{"agent":"feature-builder","iteration":0,"durationMs":61000,"status":"done"}'
            '/tmp/phase-feature-builder-0.jsonl'     = '{"type":"step_finish","usage":{"inputTokens":100,"outputTokens":50}}'
        }
        $fakeCat = { param($a) $path = $a[-1]; if ($files.ContainsKey($path)) { return $files[$path] }; throw "missing $path" }
        $script:relayed = @()
        $fakeRelay = { param($fields) $script:relayed += $fields }
        Send-PhaseFinishedSteps -RunId 'r1' -VmName 'v1' -Config $script:RelayConfig -ModelLookup { param($a) return 'm' } -Executor $fakeCat -Relay $fakeRelay
        $script:relayed[0].ContainsKey('category') | Should -Be $false
        $script:relayed = @()
        Send-PhaseFinishedSteps -RunId 'r1' -VmName 'v1' -Config $script:RelayConfig -ModelLookup { param($a) return 'm' } -Executor $fakeCat -Relay $fakeRelay -CategoryLookup { param($a, $i) return $null }
        $script:relayed[0].ContainsKey('category') | Should -Be $false
        $script:relayed = @()
        Send-PhaseFinishedSteps -RunId 'r1' -VmName 'v1' -Config $script:RelayConfig -ModelLookup { param($a) return 'm' } -Executor $fakeCat -Relay $fakeRelay -CategoryLookup { param($a, $i) throw 'lookup broke' }
        $script:relayed.Count | Should -Be 1
        $script:relayed[0].ContainsKey('category') | Should -Be $false
    }
}
