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
