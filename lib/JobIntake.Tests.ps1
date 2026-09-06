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
