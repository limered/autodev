#Requires -Modules Pester
BeforeAll {
    . (Join-Path $PSScriptRoot "JobIntake.ps1")
}

Describe 'ConvertTo-IssueSnapshot' {
    It 'maps a single issue incl. labels' {
        $in = @([PSCustomObject]@{
            id = 1; number = 8; title = 't'; html_url = 'u'
            labels = @([PSCustomObject]@{ name = 'ready-for-agent' })
            body = 'b'; state = 'open'; updated_at = '2026-01-01'
        })
        $s = @(ConvertTo-IssueSnapshot -Issues $in)
        $s.Count | Should -Be 1
        $s[0].number | Should -Be 8
        $s[0].labels | Should -Be @('ready-for-agent')
    }
    It 'returns empty for null or empty' {
        @(ConvertTo-IssueSnapshot -Issues $null).Count | Should -Be 0
        @(ConvertTo-IssueSnapshot -Issues @()).Count | Should -Be 0
    }
}

Describe 'Get-StaleSeconds' {
    It 'subtracts reference from now' {
        Get-StaleSeconds -VmNow 100 -ReferenceEpoch 40 | Should -Be 60
    }
}

Describe 'ConvertTo-IssueNumber' {
    It 'parses bare, hash, and URL forms' {
        ConvertTo-IssueNumber -Issue '8' | Should -Be '8'
        ConvertTo-IssueNumber -Issue '#8' | Should -Be '8'
        ConvertTo-IssueNumber -Issue 'https://github.com/o/r/issues/8' | Should -Be '8'
    }
    It 'throws on garbage' {
        { ConvertTo-IssueNumber -Issue 'abc' } | Should -Throw
    }
}

Describe 'New-IssueBranchName' {
    It 'builds deterministic name when pinned' {
        New-IssueBranchName -IssueNumber '8' -Timestamp '20260101-000000' -RandomSuffix '7' |
            Should -Be 'factory/issue-8-20260101-000000-7'
    }
}

Describe 'ConvertTo-OwnerRepo' {
    It 'parses https and ssh forms' {
        ConvertTo-OwnerRepo -RepoUrl 'https://github.com/owner/name.git' | Should -Be 'owner/name'
        ConvertTo-OwnerRepo -RepoUrl 'git@github.com:owner/name.git' | Should -Be 'owner/name'
    }
    It 'throws on garbage' {
        { ConvertTo-OwnerRepo -RepoUrl 'not-a-url' } | Should -Throw
    }
}

Describe 'Wait-ForPullRequest' {
    It 'returns the PR when found on retry' {
        $script:calls = 0
        $poll = { param($r, $o, $b) $script:calls++; if ($script:calls -lt 3) { return @() }; return @([PSCustomObject]@{ html_url = 'u'; number = 1 }) }
        $pr = Wait-ForPullRequest -Repo 'o/r' -Owner 'o' -Branch 'b' -Poll $poll -SleepSeconds 0
        $pr.number | Should -Be 1
        $script:calls | Should -Be 3
    }
    It 'throws when no PR appears' {
        $poll = { param($r, $o, $b) return @() }
        { Wait-ForPullRequest -Repo 'o/r' -Owner 'o' -Branch 'b' -Poll $poll -MaxAttempts 2 -SleepSeconds 0 } | Should -Throw
    }
}

Describe 'Get-ClaimedJobArgs' {
    It 'passes an ordinary claim through with no resume keys' {
        $claim = [PSCustomObject]@{
            runId = 'run-1'; repoUrl = 'https://github.com/o/r.git'; spec = 's'
            catalogContent = 'c'; catalogSha = 'sha'; workflow = 'full'
        }
        $args = Get-ClaimedJobArgs -Claim $claim -RepoRoot '/repo' -Isolator 'container'
        $args['RunId'] | Should -Be 'run-1'
        $args['RepoUrl'] | Should -Be 'https://github.com/o/r.git'
        $args['Isolator'] | Should -Be 'container'
        $args['Workflow'] | Should -Be 'full'
        $args.Contains('ResumeBranch') | Should -Be $false
        $args.Contains('ResumeStage') | Should -Be $false
        $args.Contains('ParentRunId') | Should -Be $false
        $args.Contains('Branch') | Should -Be $false
    }
    It 'passes a blank workflow through so the launcher rechecks it instead of defaulting' {
        $claim = [PSCustomObject]@{
            runId = 'run-3'; repoUrl = 'https://github.com/o/r.git'; spec = 's'
            catalogContent = 'c'; catalogSha = 'sha'
        }
        $args = Get-ClaimedJobArgs -Claim $claim -RepoRoot '/repo' -Isolator 'multipass'
        $args['Workflow'] | Should -Be ''
    }
    It 'carries the same branch plus resume stage and parent link for a resume claim' {
        $claim = [PSCustomObject]@{
            runId = 'run-2'; repoUrl = 'https://github.com/o/r.git'; spec = 's'
            catalogContent = 'c'; catalogSha = 'sha'
            branch = 'factory/issue-1-abc'; resumeStage = 'review-loop'; parentRunId = 'run-1'
        }
        $args = Get-ClaimedJobArgs -Claim $claim -RepoRoot '/repo' -Isolator 'multipass'
        $args['Branch'] | Should -Be 'factory/issue-1-abc'
        $args['ResumeBranch'] | Should -Be 'factory/issue-1-abc'
        $args['ResumeStage'] | Should -Be 'review-loop'
        $args['ParentRunId'] | Should -Be 'run-1'
    }
}
