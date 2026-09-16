#Requires -Modules Pester
BeforeAll {
    . (Join-Path $PSScriptRoot "Heartbeat.ps1")
}

Describe 'Get-HeartbeatPoll' {
    It 'drives sample, verdict, and completion through one Executor call' {
        $script:calls = @()
        $fake = {
            param($a)
            $script:calls += ,@($a)
            return "now=1000`nhb=990`nphase=feature-builder`ncategory=quality-loop`ndone=MISSING`n"
        }
        $p = Get-HeartbeatPoll -Name 'v' -Executor $fake -StallThresholdSeconds 300
        $script:calls.Count | Should -Be 1
        $p.VmNow | Should -Be 1000
        $p.HeartbeatEpoch | Should -Be 990
        $p.CurrentPhase | Should -Be 'feature-builder'
        $p.CurrentCategory | Should -Be 'quality-loop'
        $p.DoneExit | Should -Be $null
        $p.IsStalled | Should -Be $false
        $p.StaleSeconds | Should -Be 10
    }
    It 'maps missing markers to null' {
        $fake = { param($a) return "now=1000`nhb=MISSING`nphase=MISSING`ncategory=MISSING`ndone=MISSING`n" }
        $p = Get-HeartbeatPoll -Name 'v' -Executor $fake -StallThresholdSeconds 300
        $p.HeartbeatEpoch | Should -Be $null
        $p.CurrentPhase | Should -Be $null
        $p.CurrentCategory | Should -Be $null
        $p.DoneExit | Should -Be $null
    }
    It 'returns the done exit code when the marker lands' {
        $fake = { param($a) return "now=1000`nhb=990`nphase=feature-builder`ncategory=feature-builder`ndone=0`n" }
        Get-HeartbeatPoll -Name 'v' -Executor $fake -StallThresholdSeconds 300 |
            Select-Object -ExpandProperty DoneExit | Should -Be 0
    }
    It 'returns non-zero exit codes so failures still fail the run' {
        $fake = { param($a) return "now=1000`nhb=990`nphase=feature-builder`ncategory=feature-builder`ndone=1`n" }
        Get-HeartbeatPoll -Name 'v' -Executor $fake -StallThresholdSeconds 300 |
            Select-Object -ExpandProperty DoneExit | Should -Be 1
    }
    It 'maps a non-numeric done marker to null' {
        $fake = { param($a) return "now=1000`nhb=990`nphase=feature-builder`ncategory=feature-builder`ndone=oops`n" }
        Get-HeartbeatPoll -Name 'v' -Executor $fake -StallThresholdSeconds 300 |
            Select-Object -ExpandProperty DoneExit | Should -Be $null
    }
    It 'flags a stale marker as stalled in the same poll' {
        $fake = { param($a) return "now=1000`nhb=600`nphase=feature-builder`ncategory=feature-builder`ndone=MISSING`n" }
        $p = Get-HeartbeatPoll -Name 'v' -Executor $fake -StallThresholdSeconds 300
        $p.IsStalled | Should -Be $true
        $p.StaleSeconds | Should -Be 400
    }
    It 'pins the clock to the first sample before the marker exists' {
        $fake = { param($a) return "now=1000`nhb=MISSING`nphase=MISSING`ncategory=MISSING`ndone=MISSING`n" }
        $p1 = Get-HeartbeatPoll -Name 'v' -Executor $fake -StallThresholdSeconds 300
        $p1.IsStalled | Should -Be $false
        $p2 = Get-HeartbeatPoll -Name 'v' -Executor $fake -StallThresholdSeconds 300 -VmStartEpoch $p1.VmStartEpoch
        $p2.IsStalled | Should -Be $false
        $late = { param($a) return "now=1400`nhb=MISSING`nphase=MISSING`ncategory=MISSING`ndone=MISSING`n" }
        $p3 = Get-HeartbeatPoll -Name 'v' -Executor $late -StallThresholdSeconds 300 -VmStartEpoch $p1.VmStartEpoch
        $p3.IsStalled | Should -Be $true
    }
    It 'throws when the VM clock is missing' {
        $fake = { param($a) return "hb=990`nphase=x`ncategory=y`ndone=MISSING`n" }
        { Get-HeartbeatPoll -Name 'v' -Executor $fake } | Should -Throw
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
