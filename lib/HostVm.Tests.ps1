#Requires -Modules Pester
BeforeAll {
    . (Join-Path $PSScriptRoot "HostVm.ps1")
}

Describe 'Invoke-Multipass Executor Seam' {
    It 'passes args to Executor and succeeds on 0' {
        $seen = $null
        $fake = { param($a) $script:seen = $a; return 0 }
        { Invoke-Multipass launch --name v -Executor $fake } | Should -Not -Throw
        $script:seen | Should -Contain 'launch'
    }
    It 'throws when Executor returns non-zero' {
        $fake = { param($a) return 3 }
        { Invoke-Multipass launch --name v -Executor $fake } | Should -Throw
    }
    It 'collects bare args positionally when Executor is omitted (prod shape)' {
        # Prod callers (start-job.ps1, test-*.ps1) omit -Executor; without an
        # explicit Position the first bare word bound to -Executor and failed
        # [scriptblock] conversion. Shadow the native command so nothing runs.
        $script:seenArgs = $null
        function multipass { $script:seenArgs = $args; $global:LASTEXITCODE = 0 }
        try {
            { Invoke-Multipass launch 24.04 --name v --cpus 4 } | Should -Not -Throw
        }
        finally {
            Remove-Item -Path 'function:multipass' -ErrorAction SilentlyContinue
        }
        ($script:seenArgs -join ' ') | Should -Be 'launch 24.04 --name v --cpus 4'
    }
    It 'Remove-Vm deletes with purge' {
        $seen = $null
        $fake = { param($a) $script:seen = $a; return 0 }
        Remove-Vm -Name 'v1' -Executor $fake
        ($script:seen -join ' ') | Should -Match 'delete v1 --purge'
    }
    It 'New-VmFromBlueprint launches then waits' {
        $script:calls = @()
        $fake = { param($a) $script:calls += ,@($a); return 0 }
        New-VmFromBlueprint -Name 'v1' -CloudInit 'ci.yaml' -Cpus '1' -Memory '1G' -Disk '5G' -Executor $fake
        $script:calls.Count | Should -Be 2
        ($script:calls[0] -join ' ') | Should -Match 'launch 24.04 --name v1'
        ($script:calls[0] -join ' ') | Should -Match '--cpus 1 --memory 1G --disk 5G'
        ($script:calls[0] -join ' ') | Should -Match '--timeout 1200'
        ($script:calls[1] -join ' ') | Should -Match 'cloud-init status --wait'
    }
    It 'Invoke-MultipassOutput returns Executor string' {
        $fake = { param($a) return '12345' }
        Invoke-MultipassOutput -Arguments @('exec', 'v', '--', 'date', '+%s') -Executor $fake | Should -Be '12345'
    }
    It 'Invoke-MultipassOutput throws when Executor throws' {
        $fake = { param($a) throw 'boom' }
        { Invoke-MultipassOutput -Arguments @('exec') -Executor $fake } | Should -Throw
    }
}

Describe 'Save-FreezeSnapshot Capture Seam' {
    It 'writes manifest from fake captures without a VM' {
        $tmp = Join-Path ([System.IO.Path]::GetTempPath()) "freeze-test-manifest"
        $fake = { param($c) "fake:$c" }
        $path = Save-FreezeSnapshot -Name 'v1' -RepoRoot $null -Timestamp '20260101-000000' -OutDir $tmp -Capture $fake -JobParams @{ repo = 'o/r' }
        try {
            $j = Get-Content -Raw $path | ConvertFrom-Json
            $j.vm | Should -Be 'v1'
            $j.jobParams.repo | Should -Be 'o/r'
            $j.psAux | Should -Match 'fake:ps aux'
            $j.markerMtime | Should -Match 'fake:stat'
        }
        finally { Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue }
    }
    It 'keeps partial manifest when one capture throws' {
        $tmp = Join-Path ([System.IO.Path]::GetTempPath()) "freeze-test-partial"
        $fake = { param($c) if ($c -eq 'ps aux') { throw 'wedged' }; return 'ok' }
        $path = Save-FreezeSnapshot -Name 'v1' -RepoRoot $null -Timestamp '20260101-000000' -OutDir $tmp -Capture $fake -JobParams @{}
        try {
            $j = Get-Content -Raw $path | ConvertFrom-Json
            $j.psAux | Should -Match 'unavailable'
            $j.freeM | Should -Be 'ok'
        }
        finally { Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue }
    }
}
