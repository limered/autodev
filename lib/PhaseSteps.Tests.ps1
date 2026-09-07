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
    It 'covers all six phases plus loop iterations in execution order' {
        $candidates = Get-PhaseStepCandidates
        $candidates.Count | Should -Be 11
        $pairs = @($candidates | ForEach-Object { "$($_.Agent)/$($_.Iteration)" })
        $pairs[0] | Should -Be 'feature-builder/0'
        $pairs[1] | Should -Be 'test-runner/0'
        $pairs[2] | Should -Be 'static-analysis/1'
        $pairs[3] | Should -Be 'feature-builder/1'
        $pairs[8] | Should -Be 'test-runner/1'
        $pairs[9] | Should -Be 'agentic-review/0'
        $pairs[10] | Should -Be 'pr-author/0'
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
        Send-PhaseFinishedSteps -RunId 'r1' -VmName 'v1' -ModelLookup $fakeModels -Executor $fakeCat -Relay $fakeRelay
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
        Send-PhaseFinishedSteps -RunId 'r1' -VmName 'v1' -ModelLookup $fakeModels -Executor $fakeCat -Relay $fakeRelay
        $script:relayed.Count | Should -Be 1
        $script:relayed[0]['status'] | Should -Be 'failed'
        $script:relayed[0].ContainsKey('cost') | Should -Be $false
    }
    It 'relays nothing when no phase file exists' {
        $fakeCat = { param($a) throw 'missing' }
        $script:relayed = @()
        $fakeRelay = { param($fields) $script:relayed += $fields }
        { Send-PhaseFinishedSteps -RunId 'r1' -VmName 'v1' -ModelLookup { param($a) return 'm' } -Executor $fakeCat -Relay $fakeRelay } | Should -Not -Throw
        $script:relayed.Count | Should -Be 0
    }
}
