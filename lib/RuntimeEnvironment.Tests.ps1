#Requires -Modules Pester
BeforeAll {
    . (Join-Path $PSScriptRoot "RuntimeEnvironment.ps1")
}

Describe 'Invoke-RuntimeVm Executor Seam' {
    It 'passes args to Executor and succeeds on 0' {
        $seen = $null
        $fake = { param($a) $script:seen = $a; return 0 }
        { Invoke-RuntimeVm launch --name v -Executor $fake } | Should -Not -Throw
        $script:seen | Should -Contain 'launch'
    }
    It 'throws when Executor returns non-zero' {
        $fake = { param($a) return 3 }
        { Invoke-RuntimeVm launch --name v -Executor $fake } | Should -Throw
    }
    It 'collects bare args positionally when Executor is omitted (prod shape)' {
        $script:seenArgs = $null
        function multipass { $script:seenArgs = $args; $global:LASTEXITCODE = 0 }
        try {
            { Invoke-RuntimeVm launch 24.04 --name v --cpus 4 } | Should -Not -Throw
        }
        finally {
            Remove-Item -Path 'function:multipass' -ErrorAction SilentlyContinue
        }
        ($script:seenArgs -join ' ') | Should -Be 'launch 24.04 --name v --cpus 4'
    }
    It 'Remove-RuntimeVm deletes with purge' {
        $seen = $null
        $fake = { param($a) $script:seen = $a; return 0 }
        Remove-RuntimeVm -Name 'v1' -Executor $fake
        ($script:seen -join ' ') | Should -Match 'delete v1 --purge'
    }
    It 'New-RuntimeVm launches then waits' {
        $script:calls = @()
        $fake = { param($a) $script:calls += ,@($a); return 0 }
        New-RuntimeVm -Name 'v1' -CloudInit 'ci.yaml' -Cpus '1' -Memory '1G' -Disk '5G' -Executor $fake
        $script:calls.Count | Should -Be 2
        ($script:calls[0] -join ' ') | Should -Match 'launch 24.04 --name v1'
        ($script:calls[0] -join ' ') | Should -Match '--cpus 1 --memory 1G --disk 5G'
        ($script:calls[0] -join ' ') | Should -Match '--timeout 1200'
        ($script:calls[1] -join ' ') | Should -Match 'cloud-init status --wait'
    }
    It 'Copy-ToRuntimeVm addresses the guest path' {
        $fake = { param($a) $script:seen = $a; return 0 }
        Copy-ToRuntimeVm -Name 'v1' -Source '/host/a.sh' -Dest '/tmp/a.sh' -Executor $fake
        ($script:seen -join ' ') | Should -Be 'transfer /host/a.sh v1:/tmp/a.sh'
    }
    It 'Invoke-RuntimeVmOutput returns Executor string' {
        $fake = { param($a) return '12345' }
        Invoke-RuntimeVmOutput -Arguments @('exec', 'v', '--', 'date', '+%s') -Executor $fake | Should -Be '12345'
    }
    It 'Invoke-RuntimeVmOutput throws when Executor throws' {
        $fake = { param($a) throw 'boom' }
        { Invoke-RuntimeVmOutput -Arguments @('exec') -Executor $fake } | Should -Throw
    }
}

Describe 'Invoke-RuntimeContainer Executor Seam' {
    It 'passes args to Executor and succeeds on 0' {
        $fake = { param($a) $script:seen = $a; return 0 }
        { Invoke-RuntimeContainer run --name c -Executor $fake } | Should -Not -Throw
        $script:seen | Should -Contain 'run'
    }
    It 'throws when Executor returns non-zero' {
        $fake = { param($a) return 3 }
        { Invoke-RuntimeContainer run --name c -Executor $fake } | Should -Throw
    }
    It 'passes dash-led args through without binding them to parameters' {
        $fake = { param($a) $script:seen = $a; return 0 }
        { Invoke-RuntimeContainer -Arguments @('exec', 'c1', 'bash', '-c', 'echo hi') -Cli 'podman' -Executor $fake } | Should -Not -Throw
        ($script:seen -join ' ') | Should -Be 'exec c1 bash -c echo hi'
    }
    It 'Remove-RuntimeContainer forces removal' {
        $fake = { param($a) $script:seen = $a; return 0 }
        Remove-RuntimeContainer -Name 'c1' -Executor $fake
        ($script:seen -join ' ') | Should -Match 'rm -f c1'
    }
    It 'Copy-ToRuntimeContainer addresses the container path' {
        $fake = { param($a) $script:seen = $a; return 0 }
        Copy-ToRuntimeContainer -Name 'c1' -Source '/host/a.sh' -Dest '/tmp/a.sh' -Executor $fake
        ($script:seen -join ' ') | Should -Be 'cp /host/a.sh c1:/tmp/a.sh'
    }
    It 'Invoke-RuntimeContainerOutput returns Executor string' {
        $fake = { param($a) return '12345' }
        Invoke-RuntimeContainerOutput -Arguments @('exec', 'c1', 'bash', '-c', 'date') -Executor $fake | Should -Be '12345'
    }
    It 'Invoke-RuntimeContainerOutput throws when Executor throws' {
        $fake = { param($a) throw 'boom' }
        { Invoke-RuntimeContainerOutput -Arguments @('exec') -Executor $fake } | Should -Throw
    }
}

Describe 'New-RuntimeContainer SignalDir' {
    It 'runs detached with signal mount and env' {
        $script:calls = @()
        $fake = { param($a) $script:calls += ,@($a); return 0 }
        $info = New-RuntimeContainer -Name 'sig-test-run' -Image 'img:latest' `
            -ExtraMounts @('/host/pat.txt:/tmp/github-pat.txt:ro,z') `
            -Environment @{ MODEL = 'm' } -Cli 'podman' -Executor $fake
        try {
            $script:calls.Count | Should -Be 1
            $cmd = ($script:calls[0] -join ' ')
            $cmd | Should -Match 'run -d --init --name sig-test-run'
            $cmd | Should -Match '/tmp:z'
            $cmd | Should -Match '/host/pat.txt:/tmp/github-pat.txt:ro,z'
            $cmd | Should -Match '-e MODEL=m'
            $cmd | Should -Match 'img:latest sleep infinity'
            $info.SignalDir | Should -Match 'factory-signals'
            Test-Path -LiteralPath $info.SignalDir | Should -Be $true
        }
        finally {
            Remove-Item -Recurse -Force -LiteralPath $info.SignalDir -ErrorAction SilentlyContinue
        }
    }
    It 'omits env flags when no environment is given' {
        $script:calls = @()
        $fake = { param($a) $script:calls += ,@($a); return 0 }
        $info = New-RuntimeContainer -Name 'sig-test-noenv' -Cli 'podman' -Executor $fake
        try {
            ($script:calls[0] -join ' ') | Should -Not -Match '-e '
        }
        finally {
            Remove-Item -Recurse -Force -LiteralPath $info.SignalDir -ErrorAction SilentlyContinue
        }
    }
    It 'Remove-RuntimeContainer deletes the signal dir' {
        $fake = { param($a) return 0 }
        $info = New-RuntimeContainer -Name 'sig-test-rm' -Cli 'podman' -Executor $fake
        Test-Path -LiteralPath $info.SignalDir | Should -Be $true
        Remove-RuntimeContainer -Name 'sig-test-rm' -Cli 'podman' -SignalDir $info.SignalDir -Executor $fake
        Test-Path -LiteralPath $info.SignalDir | Should -Be $false
    }
    It 'New-RuntimeHeartbeatExecutorContainer reads from the signal dir' {
        $fxDir = Join-Path ([System.IO.Path]::GetTempPath()) "runtime-heartbeat-fx"
        New-Item -ItemType Directory -Path $fxDir -Force | Out-Null
        try {
            'x' | Set-Content (Join-Path $fxDir 'heartbeat') -NoNewline
            $exec = New-RuntimeHeartbeatExecutorContainer -SignalDir $fxDir
            $epoch = & $exec @('exec', 'c1', '--', 'stat', '-c', '%Y', '/tmp/heartbeat')
            { [long]$epoch.Trim() } | Should -Not -Throw
        }
        finally {
            Remove-Item -Recurse -Force $fxDir -ErrorAction SilentlyContinue
        }
    }
    It 'New-RuntimeFreezeCaptureContainer builds a container exec capture' {
        $script:seen = $null
        $fake = { param($a) $script:seen = $a; return 'ok' }
        $capture = New-RuntimeFreezeCaptureContainer -Name 'c1' -Cli 'podman'
        $capture | Should -Not -Be $null
    }
    It 'New-RuntimeFreezeCaptureContainer delegates exec to Invoke-RuntimeContainerOutput' {
        $realOutput = ${function:Invoke-RuntimeContainerOutput}
        $script:seenArgs = $null
        $script:seenCli = $null
        $script:seenTimeout = $null
        function Invoke-RuntimeContainerOutput {
            param($Arguments, $Cli, $TimeoutSeconds)
            $script:seenArgs = $Arguments
            $script:seenCli = $Cli
            $script:seenTimeout = $TimeoutSeconds
            return 'ok'
        }
        try {
            $capture = New-RuntimeFreezeCaptureContainer -Name 'c1' -Cli 'podman'
            & $capture 'ps aux' | Should -Be 'ok'
            ($script:seenArgs -join ' ') | Should -Be 'exec c1 bash -c ps aux'
            $script:seenCli | Should -Be 'podman'
            $script:seenTimeout | Should -Be 15
        }
        finally {
            ${function:Invoke-RuntimeContainerOutput} = $realOutput
        }
    }
}

Describe 'Get-ContainerSocketMount' {
    BeforeEach {
        $script:savedXdg = $env:XDG_RUNTIME_DIR
    }
    AfterEach {
        $env:XDG_RUNTIME_DIR = $script:savedXdg
    }
    It 'returns null when no runtime dir is set' {
        $env:XDG_RUNTIME_DIR = $null
        Get-ContainerSocketMount | Should -Be $null
    }
    It 'returns null without throwing on an unreadable tree' {
        $base = Join-Path ([System.IO.Path]::GetTempPath()) "sock-fx"
        New-Item -ItemType Directory -Path (Join-Path $base 'podman') -Force | Out-Null
        try {
            chmod 000 $base
            $env:XDG_RUNTIME_DIR = $base
            { Get-ContainerSocketMount } | Should -Not -Throw
            Get-ContainerSocketMount | Should -Be $null
        }
        finally {
            chmod 755 $base
            Remove-Item -Recurse -Force $base -ErrorAction SilentlyContinue
        }
    }
}

Describe 'New-ContainerFileExecutor' {
    BeforeAll {
        $script:fxDir = Join-Path ([System.IO.Path]::GetTempPath()) "container-exec-fx"
        New-Item -ItemType Directory -Path $script:fxDir -Force | Out-Null
        'feature-builder' | Set-Content (Join-Path $script:fxDir 'current-phase') -NoNewline
        '{}' | Set-Content (Join-Path $script:fxDir 'phase-feature-builder-0.jsonl') -NoNewline
    }
    AfterAll {
        Remove-Item -Recurse -Force $script:fxDir -ErrorAction SilentlyContinue
    }
    It 'answers date with a parseable epoch' {
        $exec = New-ContainerFileExecutor -SignalDir $script:fxDir
        $out = & $exec @('exec', 'c1', '--', 'date', '+%s')
        { [long]$out.Trim() } | Should -Not -Throw
    }
    It 'reads heartbeat mtime and phase files from the signal dir' {
        $hb = Join-Path $script:fxDir 'heartbeat'
        'x' | Set-Content $hb -NoNewline
        $exec = New-ContainerFileExecutor -SignalDir $script:fxDir
        $epoch = & $exec @('exec', 'c1', '--', 'stat', '-c', '%Y', '/tmp/heartbeat')
        { [long]$epoch.Trim() } | Should -Not -Throw
        & $exec @('exec', 'c1', '--', 'cat', '/tmp/current-phase') | Should -Match 'feature-builder'
        & $exec @('exec', 'c1', '--', 'cat', '/tmp/phase-feature-builder-0.jsonl') | Should -Match '\{\}'
    }
    It 'throws on missing files so callers map to null' {
        $exec = New-ContainerFileExecutor -SignalDir (Join-Path $script:fxDir 'absent')
        { & $exec @('exec', 'c1', '--', 'cat', '/tmp/current-phase') } | Should -Throw
    }
    It 'answers the combined heartbeat poll from the signal dir' {
        $pollDir = Join-Path $script:fxDir 'poll-fx'
        New-Item -ItemType Directory -Path $pollDir -Force | Out-Null
        'static-analysis' | Set-Content (Join-Path $pollDir 'current-phase') -NoNewline
        'quality-loop' | Set-Content (Join-Path $pollDir 'current-category') -NoNewline
        'x' | Set-Content (Join-Path $pollDir 'heartbeat') -NoNewline
        $exec = New-ContainerFileExecutor -SignalDir $pollDir
        $out = & $exec @('exec', 'c1', '--', 'bash', '-c', 'now=$(date +%s); echo probe')
        $out | Should -Match 'now=\d+'
        $out | Should -Match 'hb=\d+'
        $out | Should -Match 'phase=static-analysis'
        $out | Should -Match 'category=quality-loop'
        $out | Should -Match 'done=MISSING'
        Remove-Item -Recurse -Force $pollDir -ErrorAction SilentlyContinue
    }
    It 'maps a missing poll marker to MISSING' {
        $emptyDir = Join-Path $script:fxDir 'poll-empty'
        New-Item -ItemType Directory -Path $emptyDir -Force | Out-Null
        $exec = New-ContainerFileExecutor -SignalDir $emptyDir
        $out = & $exec @('exec', 'c1', '--', 'bash', '-c', 'probe')
        $out | Should -Match 'hb=MISSING'
        $out | Should -Match 'phase=MISSING'
        Remove-Item -Recurse -Force $emptyDir -ErrorAction SilentlyContinue
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

Describe 'Nested-scope closure resolution' {
    It 'freeze capture resolves its callee when the lib is dot-sourced in a child scope' {
        $lib = Join-Path $PSScriptRoot 'RuntimeEnvironment.ps1'
        $result = & {
            param($Lib)
            . $Lib
            $cap = New-RuntimeFreezeCaptureContainer -Name 'c1' -Cli 'nonexistent-cli-xyz'
            Invoke-CaptureSafe -Name 'c1' -Command 'true' -Capture $cap
        } -Lib $lib
        "$result" | Should -Not -Match 'not recognized'
    }
}
