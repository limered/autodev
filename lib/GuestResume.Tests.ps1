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
    It 'names every pipeline stage in the resume vocabulary' {
        foreach ($id in $script:StageIds) {
            $script:GuestText | Should -Match ([regex]::Escape($id))
        }
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
}
