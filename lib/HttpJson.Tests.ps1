#Requires -Modules Pester
BeforeAll {
    . (Join-Path $PSScriptRoot "HttpJson.ps1")
}

Describe 'Read-SecretFile' {
    It 'returns null for missing file' {
        Read-SecretFile (Join-Path ([System.IO.Path]::GetTempPath()) "no-such-secret.txt") | Should -Be $null
    }
    It 'trims file content' {
        $tmp = Join-Path ([System.IO.Path]::GetTempPath()) "secret-test.txt"
        "  tok123  `n" | Set-Content -LiteralPath $tmp -NoNewline
        try { Read-SecretFile $tmp | Should -Be 'tok123' }
        finally { Remove-Item -Force $tmp -ErrorAction SilentlyContinue }
    }
}

Describe 'ConvertTo-Utf8JsonBody' {
    It 'round-trips non-ASCII through UTF-8 bytes' {
        $bytes = ConvertTo-Utf8JsonBody @(@{ title = "caf· unicode ☃" }) -AsArray
        $json = [System.Text.Encoding]::UTF8.GetString($bytes)
        $json | Should -Match 'caf'
        ($json | ConvertFrom-Json).title | Should -Be 'caf· unicode ☃'
    }
    It 'sends [] for empty input' {
        $bytes = ConvertTo-Utf8JsonBody @() -AsArray
        [System.Text.Encoding]::UTF8.GetString($bytes) | Should -Be '[]'
    }
    It 'encodes a single object without array wrap' {
        $bytes = ConvertTo-Utf8JsonBody @{ type = 'run-started' }
        [System.Text.Encoding]::UTF8.GetString($bytes) | Should -Match '"run-started"'
    }
}

Describe 'Invoke-JsonRequest' {
    It 'passes method, uri, body, headers to the fake' {
        $script:seen = $null
        $fake = { param($m, $u, $b, $h) $script:seen = @($m, $u, $b, $h); return 'ok' }
        $body = [byte[]]@(1, 2)
        Invoke-JsonRequest -Method 'Put' -Uri 'https://x/issues/o/r' -Body $body -Headers @{ t = 'v' } -InvokeRest $fake | Should -Be 'ok'
        $script:seen[0] | Should -Be 'Put'
        $script:seen[1] | Should -Be 'https://x/issues/o/r'
    }
}
