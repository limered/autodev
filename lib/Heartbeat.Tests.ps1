#Requires -Modules Pester
BeforeAll {
    . (Join-Path $PSScriptRoot "Heartbeat.ps1")
}

Describe 'Heartbeat poll helpers' {
    It 'reads VM clock via Executor' {
        $fake = { param($a) return '1000' }
        Get-VmEpochSeconds -Name 'v' -Executor $fake | Should -Be 1000
    }
    It 'returns null when marker is missing' {
        $fake = { param($a) throw 'no such file' }
        Get-HeartbeatEpochSeconds -Name 'v' -Executor $fake | Should -Be $null
    }
    It 'returns null when phase file is missing' {
        $fake = { param($a) throw 'no such file' }
        Get-VmCurrentPhase -Name 'v' -Executor $fake | Should -Be $null
    }
    It 'samples epoch + phase together' {
        $fake = {
            param($a)
            if ($a -contains 'date') { return '1000' }
            if ($a -contains 'stat') { return '990' }
            return 'feature-builder'
        }
        $s = Get-HeartbeatSample -Name 'v' -Executor $fake
        $s.VmNow | Should -Be 1000
        $s.HeartbeatEpoch | Should -Be 990
        $s.CurrentPhase | Should -Be 'feature-builder'
    }
}

Describe 'Get-VmDoneExit' {
    It 'returns the VM exit code when the done marker lands' {
        $fake = { param($a) return '0' }
        Get-VmDoneExit -Name 'v' -Executor $fake | Should -Be 0
    }
    It 'returns non-zero exit codes so failures still fail the run' {
        $fake = { param($a) return '1' }
        Get-VmDoneExit -Name 'v' -Executor $fake | Should -Be 1
    }
    It 'returns null when the marker is absent' {
        $fake = { param($a) throw 'no such file' }
        Get-VmDoneExit -Name 'v' -Executor $fake | Should -Be $null
    }
    It 'returns null for a non-numeric marker' {
        $fake = { param($a) return 'oops' }
        Get-VmDoneExit -Name 'v' -Executor $fake | Should -Be $null
    }
}

Describe 'Test-HeartbeatStall' {
    It 'is not stalled when fresh' {
        $r = Test-HeartbeatStall -VmNow 1000 -HeartbeatEpoch 990 -VmStartEpoch $null -StallThresholdSeconds 300
        $r.IsStalled | Should -Be $false
        $r.StaleSeconds | Should -Be 10
    }
    It 'is stalled past threshold' {
        $r = Test-HeartbeatStall -VmNow 1000 -HeartbeatEpoch 600 -VmStartEpoch $null -StallThresholdSeconds 300
        $r.IsStalled | Should -Be $true
    }
    It 'pins clock to first sample before marker exists' {
        $r1 = Test-HeartbeatStall -VmNow 1000 -HeartbeatEpoch $null -VmStartEpoch $null -StallThresholdSeconds 300
        $r1.IsStalled | Should -Be $false
        $r2 = Test-HeartbeatStall -VmNow 1400 -HeartbeatEpoch $null -VmStartEpoch $r1.VmStartEpoch -StallThresholdSeconds 300
        $r2.IsStalled | Should -Be $true
    }
}
