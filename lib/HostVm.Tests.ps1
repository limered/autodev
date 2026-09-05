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
