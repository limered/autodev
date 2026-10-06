#Requires -Modules Pester
BeforeAll {
    $script:GuestScript = Join-Path $PSScriptRoot '..' 'infrastructure' 'multipass' 'test-feature-builder.sh'
    $script:GuestText = Get-Content -LiteralPath $script:GuestScript -Raw
    $script:StageIds = @((Get-Content -LiteralPath (Join-Path $PSScriptRoot '..' 'agents.json') -Raw | ConvertFrom-Json).stages.PSObject.Properties | ForEach-Object { $_.Name })
}

Describe 'Guest resume contract' {
    It 'is syntactically valid bash' {
        $out = & bash -n $script:GuestScript 2>&1
        $LASTEXITCODE | Should -Be 0
    }
    It 'takes the resume branch and stage as guest environment inputs' {
        $script:GuestText | Should -Match 'RESUME_BRANCH='
        $script:GuestText | Should -Match 'RESUME_STAGE='
    }
    It 'skips implement on a present branch instead of rebuilding it' {
        $script:GuestText | Should -Match 'Skipping phase 1/7 \(implement\)'
    }
    It 'falls back to the full pipeline when the branch is absent or not ahead' {
        $script:GuestText | Should -Match 'absent on remote; falling back to full pipeline'
        $script:GuestText | Should -Match 'is not ahead of .* falling back to full pipeline'
    }
    It 'logs which commit the resume cloned for auditability' {
        $script:GuestText | Should -Match 'checked out existing branch'
        $script:GuestText | Should -Match 'rev-parse HEAD'
    }
    It 'gates every post-implement phase behind the resume entry point' {
        foreach ($phase in @('phase 2/7', 'phase 3/7', 'phase 4/7', 'phase 5/7', 'phase 6/7', 'phase 7/7')) {
            $script:GuestText | Should -Match ([regex]::Escape($phase))
        }
        $script:GuestText | Should -Match 'resume_runs_phase 1'
        $script:GuestText | Should -Match 'resume_runs_phase 2'
        $script:GuestText | Should -Match 'resume_runs_phase 3'
        $script:GuestText | Should -Match 'resume_runs_phase 4'
        $script:GuestText | Should -Match 'resume_runs_phase 5'
        $script:GuestText | Should -Match 'resume_runs_phase 6'
    }
    It 'derives the resume entry point from the catalog order instead of a hardcoded stage list' {
        $script:GuestText | Should -Match 'STAGE_IDS='
        $script:GuestText | Should -Match 'resolve_resume_stage\(\)'
        $script:GuestText | Should -Match 'stage_index\(\)'
        $script:GuestText | Should -Match 'RESUME_IDX=\$\(\(RESUME_POS \+ 1\)\)'
        $script:GuestText | Should -Not -Match 'case "\$RESUME_STAGE" in'
    }
    It 'keeps pre-rename labels as catalog-conditional aliases' {
        $script:GuestText | Should -Match 'quality-loop\) _alias="static-loop"'
        $script:GuestText | Should -Match 'agentic-review\) _alias="architecture-review"'
    }
}

Describe 'Guest resume stage resolution' {
    BeforeAll {
        $script:GuestScript = Join-Path $PSScriptRoot '..' 'infrastructure' 'multipass' 'test-feature-builder.sh'
        $script:GuestFuncs = (& bash -c "sed -n '/^load_category_specs() {/,/^}/p;/^stage_index() {/,/^}/p;/^resolve_resume_stage() {/,/^}/p' '$script:GuestScript'") -join "`n"
        function script:Invoke-ResumeHarness {
            param([string]$CatalogJson)
            $catalog = Join-Path $TestDrive 'agents.json'
            Set-Content -LiteralPath $catalog -Value $CatalogJson -NoNewline
            $harness = Join-Path $TestDrive 'resolve.sh'
        $body = @(
            $script:GuestFuncs
            "AGENTS_JSON='$catalog'"
            'load_category_specs'
            'STAGE_IDS=()'
            'for _resume_spec in "${CATEGORY_SPECS[@]}"; do'
            '  STAGE_IDS+=("${_resume_spec%%|*}")'
            'done'
            'echo "order=${STAGE_IDS[*]}"'
            'for s in alpha beta gamma; do'
            '  echo "resolve:$s=$(resolve_resume_stage "$s" 2>/dev/null || echo UNRESOLVED)"'
            '  echo "index:$s=$(stage_index "$s" 2>/dev/null || echo UNRESOLVED)"'
            'done'
            'echo "alias-quality=$(resolve_resume_stage quality-loop 2>/dev/null || echo UNRESOLVED)"'
            'echo "alias-agentic=$(resolve_resume_stage agentic-review 2>/dev/null || echo UNRESOLVED)"'
            'echo "unknown=$(resolve_resume_stage ghost 2>/dev/null || echo UNRESOLVED)"'
        ) -join "`n"
        Set-Content -LiteralPath $harness -Value $body -NoNewline
        return (& bash $harness)
        }
    }
    It 'resolves a custom catalog in file order with unknowns falling back' {
        $out = Invoke-ResumeHarness '{"stages":{"alpha":{"type":"sequential","agents":["a"]},"beta":{"type":"loop","agents":["b"],"iterations":2},"gamma":{"type":"sequential","agents":["c"]}}}'
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
    It 'resolves legacy labels only when the catalog holds the renamed stage' {
        $out = Invoke-ResumeHarness '{"stages":{"alpha":{"type":"sequential","agents":["a"]},"static-loop":{"type":"loop","agents":["b"],"iterations":2},"architecture-review":{"type":"sequential","agents":["c"]}}}'
        $out | Should -Contain 'alias-quality=static-loop'
        $out | Should -Contain 'alias-agentic=architecture-review'
    }
    It 'prefers the exact catalog match over the alias' {
        $out = Invoke-ResumeHarness '{"stages":{"quality-loop":{"type":"loop","agents":["b"],"iterations":2},"static-loop":{"type":"loop","agents":["c"],"iterations":2},"alpha":{"type":"sequential","agents":["a"]}}}'
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
    It 'labels the quality loop phase with the seeded static-loop slot' {
        $script:GuestText | Should -Match 'Running phase 4/7 \(static-loop\)'
        $script:GuestText | Should -Match 'Skipping phase 4/7 \(static-loop\)'
    }
}
