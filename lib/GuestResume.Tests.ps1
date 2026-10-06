#Requires -Modules Pester
BeforeAll {
    $script:GuestScript = Join-Path $PSScriptRoot '..' 'infrastructure' 'multipass' 'test-feature-builder.sh'
    $script:GuestText = Get-Content -LiteralPath $script:GuestScript -Raw
}

Describe 'Guest frozen workflow contract' {
    It 'is syntactically valid bash' {
        $out = & bash -n $script:GuestScript 2>&1
        $LASTEXITCODE | Should -Be 0
    }
    It 'audits its frozen stage list from the host launcher' {
        $script:GuestText | Should -Match 'WORKFLOW_STAGES_B64'
        $script:GuestText | Should -Match 'FROZEN_STAGES='
        $script:GuestText | Should -Match 'for _idx in "\$\{!FROZEN_STAGES\[@\]\}"'
        $script:GuestText | Should -Not -Match 'phase [0-9]/7'
    }
    It 'fails fast on a missing or empty frozen list instead of falling back' {
        ($script:GuestText | Select-String -Pattern 'stale-workflow' -AllMatches).Count | Should -BeGreaterThan 0
        $script:GuestText | Should -Match 'refusing to fall back to any default pipeline'
        $script:GuestText | Should -Match 'empty stage id'
    }
    It 'fails fast on a stage id it has no runner for' {
        $script:GuestText | Should -Match "unknown stage id at start"
    }
    It 'takes the resume branch and stage as guest environment inputs' {
        $script:GuestText | Should -Match 'RESUME_BRANCH='
        $script:GuestText | Should -Match 'RESUME_STAGE='
    }
    It 'skips implement on a present branch instead of rebuilding it' {
        $script:GuestText | Should -Match 'Skipping implement: resumed branch already holds the implementation'
    }
    It 'falls back to the full pipeline when the branch is absent or not ahead' {
        $script:GuestText | Should -Match 'absent on remote; falling back to full pipeline'
        $script:GuestText | Should -Match 'is not ahead of .* falling back to full pipeline'
    }
    It 'logs which commit the resume cloned for auditability' {
        $script:GuestText | Should -Match 'checked out existing branch'
        $script:GuestText | Should -Match 'rev-parse HEAD'
    }
    It 'gates every pre-resume stage behind the resume entry point' {
        $script:GuestText | Should -Match 'stage_no.*-lt.*RESUME_IDX'
        $script:GuestText | Should -Match 'resume starts at ..RESUME_STAGE.'
    }
    It 'derives the resume entry point from the frozen workflow order instead of a hardcoded stage list' {
        $script:GuestText | Should -Match 'resolve_resume_stage\(\)'
        $script:GuestText | Should -Match 'stage_index\(\)'
        $script:GuestText | Should -Match 'RESUME_IDX=\$\(\(RESUME_POS \+ 1\)\)'
        $script:GuestText | Should -Not -Match 'case "\$RESUME_STAGE" in'
    }
    It 'keeps pre-rename labels as frozen-list-conditional aliases' {
        $script:GuestText | Should -Match 'quality-loop\) _alias="static-loop"'
        $script:GuestText | Should -Match 'agentic-review\) _alias="architecture-review"'
    }
}

Describe 'Guest frozen stage resolution' {
    BeforeAll {
        $script:GuestScript = Join-Path $PSScriptRoot '..' 'infrastructure' 'multipass' 'test-feature-builder.sh'
        $script:GuestFuncs = (& bash -c "sed -n '/^stage_index() {/,/^}/p;/^resolve_resume_stage() {/,/^}/p' '$script:GuestScript'") -join "`n"
        function script:Invoke-ResumeHarness {
            param([string]$FrozenStages)
            # The extraction above stays file-free; FROZEN_STAGES is seeded
            # into the harness shell from the frozen list text.
            $frozen = Join-Path $TestDrive 'frozen.txt'
            Set-Content -LiteralPath $frozen -Value $FrozenStages -NoNewline
            $harness = Join-Path $TestDrive 'resolve.sh'
            $resolveLine = 'echo "resolve:$s=$(resolve_resume_stage "$s" 2>/dev/null || echo UNRESOLVED)"'
            $indexLine = 'echo "index:$s=$(stage_index "$s" 2>/dev/null || echo UNRESOLVED)"'
            $harnessBody = $script:GuestFuncs + "`n" +
                "IFS=',' read -r -a FROZEN_STAGES <<< ``cat '$frozen'```n" +
                'echo "order=${FROZEN_STAGES[*]}"' + "`n" +
                'for s in alpha beta gamma; do' + "`n" +
                "$resolveLine`n" +
                "$indexLine`n" +
                'done' + "`n" +
                'echo "alias-quality=$(resolve_resume_stage quality-loop 2>/dev/null || echo UNRESOLVED)"' + "`n" +
                'echo "alias-agentic=$(resolve_resume_stage agentic-review 2>/dev/null || echo UNRESOLVED)"' + "`n" +
                'echo "unknown=$(resolve_resume_stage ghost 2>/dev/null || echo UNRESOLVED)"'
            Set-Content -LiteralPath $harness -Value $harnessBody -NoNewline
            return (& bash $harness)
        }
    }
    It 'resolves a picked list in workflow order with unknowns unresolved' {
        $out = Invoke-ResumeHarness 'alpha,beta,gamma'
        $out | Should -Contain 'order=alpha beta gamma'
        $out | Should -Contain 'resolve:alpha=alpha'
        $out | Should -Contain 'index:alpha=0'
        $out | Should -Contain 'resolve:beta=beta'
        $out | Should -Contain 'index:beta=1'
        $out | Should -Contain 'resolve:gamma=gamma'
        $out | Should -Contain 'index:gamma=2'
        $out | Should -Contain 'alias-quality=UNRESOLVED'
        $out | Should -Contain 'alias-agentic=UNRESOLVED'
        $out | Should -Contain 'unknown=UNRESOLVED'
    }
    It 'resolves legacy labels only when the frozen list holds the renamed stage' {
        $out = Invoke-ResumeHarness 'alpha,static-loop,architecture-review'
        $out | Should -Contain 'order=alpha static-loop architecture-review'
        $out | Should -Contain 'alias-quality=static-loop'
        $out | Should -Contain 'alias-agentic=architecture-review'
    }
    It 'prefers the exact frozen match over the alias' {
        $out = Invoke-ResumeHarness 'quality-loop,static-loop,alpha'
        $out | Should -Contain 'alias-quality=quality-loop'
    }
}

Describe 'Guest seeded category names' {
    BeforeAll {
        $script:GuestScript = Join-Path $PSScriptRoot '..' 'infrastructure' 'multipass' 'test-feature-builder.sh'
        $script:GuestText = Get-Content -LiteralPath $script:GuestScript -Raw
    }
    It 'reports the seeded static-loop slot from the quality loop phases' {
        $script:GuestText | Should -Match 'run_agent_phase static-analysis "\$QUALITY_SPEC" "\$i" "static-loop"'
        $script:GuestText | Should -Match 'run_agent_phase feature-builder "\$FIX_SPEC" "\$\(\(i\+3\)\)" "static-loop"'
    }
    It 'dispatches the loops from their stage arms below the slot naming' {
        $script:GuestText | Should -Match 'review-loop\)'
        $script:GuestText | Should -Match 'static-loop\)'
        $script:GuestText | Should -Match 'run_review_loop'
        $script:GuestText | Should -Match 'run_quality_loop'
    }
}
