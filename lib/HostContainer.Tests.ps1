#Requires -Modules Pester
BeforeAll {
    . (Join-Path $PSScriptRoot "HostContainer.ps1")
}

Describe 'Invoke-Container Executor Seam' {
    It 'passes args to Executor and succeeds on 0' {
        $fake = { param($a) $script:seen = $a; return 0 }
        { Invoke-Container run --name c -Executor $fake } | Should -Not -Throw
        $script:seen | Should -Contain 'run'
    }
    It 'throws when Executor returns non-zero' {
        $fake = { param($a) return 3 }
        { Invoke-Container run --name c -Executor $fake } | Should -Throw
    }
    It 'passes dash-led args through without binding them to parameters' {
        $fake = { param($a) $script:seen = $a; return 0 }
        { Invoke-Container -Arguments @('exec', 'c1', 'bash', '-c', 'echo hi') -Cli 'podman' -Executor $fake } | Should -Not -Throw
        ($script:seen -join ' ') | Should -Be 'exec c1 bash -c echo hi'
    }
    It 'Remove-Container forces removal' {
        $fake = { param($a) $script:seen = $a; return 0 }
        Remove-Container -Name 'c1' -Executor $fake
        ($script:seen -join ' ') | Should -Match 'rm -f c1'
    }
    It 'New-ContainerFromImage runs detached with signal mount and env' {
        $script:calls = @()
        $fake = { param($a) $script:calls += ,@($a); return 0 }
        New-ContainerFromImage -Name 'c1' -Image 'img:latest' -SignalDir '/sig' `
            -ExtraMounts @('/host/pat.txt:/tmp/github-pat.txt:ro,z') `
            -Environment @{ MODEL = 'm' } -Executor $fake
        $script:calls.Count | Should -Be 1
        $cmd = ($script:calls[0] -join ' ')
        $cmd | Should -Match 'run -d --init --name c1'
        $cmd | Should -Match '/sig:/tmp:z'
        $cmd | Should -Match '/host/pat.txt:/tmp/github-pat.txt:ro,z'
        $cmd | Should -Match '-e MODEL=m'
        $cmd | Should -Match 'img:latest sleep infinity'
    }
    It 'omits env flags when no environment is given' {
        $script:calls = @()
        $fake = { param($a) $script:calls += ,@($a); return 0 }
        New-ContainerFromImage -Name 'c1' -SignalDir '/sig' -Executor $fake
        ($script:calls[0] -join ' ') | Should -Not -Match '-e '
    }
    It 'Copy-IntoContainer addresses the container path' {
        $fake = { param($a) $script:seen = $a; return 0 }
        Copy-IntoContainer -Name 'c1' -Source '/host/a.sh' -Dest '/tmp/a.sh' -Executor $fake
        ($script:seen -join ' ') | Should -Be 'cp /host/a.sh c1:/tmp/a.sh'
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
