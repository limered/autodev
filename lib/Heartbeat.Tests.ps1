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
    It 'passes the stream error count and message through' {
        $fake = { param($a) return "now=1000`nhb=990`nphase=x`ncategory=y`ndone=MISSING`nerr=2`nerrmsg=level=ERROR message=""stream error""`n" }
        $p = Get-HeartbeatPoll -Name 'v' -Executor $fake -StallThresholdSeconds 300
        $p.StreamErrorCount | Should -Be 2
        $p.StreamErrorMessage | Should -Match 'stream error'
    }
    It 'maps a missing err probe to unknown, never stalled-by-error' {
        $fake = { param($a) return "now=1000`nhb=990`nphase=x`ncategory=y`ndone=MISSING`n" }
        $p = Get-HeartbeatPoll -Name 'v' -Executor $fake -StallThresholdSeconds 300
        $p.StreamErrorCount | Should -Be $null
        $p.StreamErrorMessage | Should -Be $null
    }
    It 'maps a non-numeric err count to unknown' {
        $fake = { param($a) return "now=1000`nhb=990`nphase=x`ncategory=y`ndone=MISSING`nerr=oops`nerrmsg=MISSING`n" }
        $p = Get-HeartbeatPoll -Name 'v' -Executor $fake -StallThresholdSeconds 300
        $p.StreamErrorCount | Should -Be $null
        $p.StreamErrorMessage | Should -Be $null
    }
}

Describe 'Get-HeartbeatPollScript' {
    It 'probes the opencode log for stream errors in the same exec' {
        $script = Get-HeartbeatPollScript
        $script | Should -Match 'stream error'
        $script | Should -Match 'err=%s'
        $script | Should -Match 'errmsg=%s'
        $script | Should -Match '\.local/share/opencode/log/'
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

Describe 'Test-StreamErrorStall' {
    It 'has no opinion when the probe predates err=' {
        $r = Test-StreamErrorStall -VmNow 1000 -ErrorCount $null -ErrorBaseline 0 -HeartbeatAdvanced $false -GraceSeconds 180
        $r.IsStalled | Should -Be $false
        $r.ErrorBaseline | Should -Be 0
        $r.ErrorFirstSeenEpoch | Should -Be $null
    }
    It 're-baselines a hiccup the agent recovers from' {
        $r = Test-StreamErrorStall -VmNow 1000 -ErrorCount 3 -ErrorBaseline 0 -HeartbeatAdvanced $true -GraceSeconds 180
        $r.IsStalled | Should -Be $false
        $r.ErrorBaseline | Should -Be 3
        $r.ErrorFirstSeenEpoch | Should -Be $null
    }
    It 'starts the grace window on the first unrecovered error' {
        $r = Test-StreamErrorStall -VmNow 1000 -ErrorCount 1 -ErrorBaseline 0 -HeartbeatAdvanced $false -GraceSeconds 180
        $r.IsStalled | Should -Be $false
        $r.ErrorFirstSeenEpoch | Should -Be 1000
    }
    It 'stays quiet inside the grace window' {
        $r = Test-StreamErrorStall -VmNow 1100 -ErrorCount 2 -ErrorBaseline 0 -ErrorFirstSeenEpoch 1000 -HeartbeatAdvanced $false -GraceSeconds 180
        $r.IsStalled | Should -Be $false
        $r.ErrorFirstSeenEpoch | Should -Be 1000
    }
    It 'fails past the grace window with the error delta in the reason' {
        $r = Test-StreamErrorStall -VmNow 1180 -ErrorCount 2 -ErrorBaseline 0 -ErrorFirstSeenEpoch 1000 -HeartbeatAdvanced $false -GraceSeconds 180
        $r.IsStalled | Should -Be $true
        $r.StallReason | Should -Match 'unrecovered for 180s'
        $r.StallReason | Should -Match '2 error\(s\)'
    }
    It 're-baselines on log rotation instead of firing' {
        $r = Test-StreamErrorStall -VmNow 2000 -ErrorCount 1 -ErrorBaseline 5 -ErrorFirstSeenEpoch 1000 -HeartbeatAdvanced $false -GraceSeconds 180
        $r.IsStalled | Should -Be $false
        $r.ErrorBaseline | Should -Be 1
        $r.ErrorFirstSeenEpoch | Should -Be $null
    }
    It 'never fires when the count matches the baseline' {
        $r = Test-StreamErrorStall -VmNow 9999 -ErrorCount 0 -ErrorBaseline 0 -HeartbeatAdvanced $false -GraceSeconds 180
        $r.IsStalled | Should -Be $false
    }
}
