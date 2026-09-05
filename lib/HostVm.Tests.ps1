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
    It 'Invoke-MultipassOutput returns Executor string' {
        $fake = { param($a) return '12345' }
        Invoke-MultipassOutput -Arguments @('exec', 'v', '--', 'date', '+%s') -Executor $fake | Should -Be '12345'
    }
    It 'Invoke-MultipassOutput throws when Executor throws' {
        $fake = { param($a) throw 'boom' }
        { Invoke-MultipassOutput -Arguments @('exec') -Executor $fake } | Should -Throw
    }
}
