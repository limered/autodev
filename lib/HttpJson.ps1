#!/usr/bin/env powershell
#Requires -Version 5.1
<#
.SYNOPSIS
  Secret-file reads + UTF-8 JSON transport behind one Seam (lib/HttpJson.ps1).

.DESCRIPTION
  Dot-source this file — no side effects on load:
    . (Join-Path $RepoRoot "lib/HttpJson.ps1")
  PowerShell 5.1 encodes a string -Body as Latin-1 when the content type omits
  a charset, corrupting non-ASCII chars into bytes the dashboard rejects. All
  JSON leaves here as UTF-8 bytes. Transport hides behind an injectable
  -InvokeRest Seam (real Invoke-RestMethod in prod, fake in Pester).
#>

function Read-SecretFile {
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) { return $null }
    try {
        return (Get-Content -LiteralPath $Path -Raw).Trim()
    }
    catch {
        Write-Warning "Failed to read secret file ${Path}: $_"
        return $null
    }
}

function ConvertTo-Utf8JsonBody {
    param($InputObject, [int]$Depth = 4, [switch]$AsArray)
    if ($AsArray) {
        $json = ConvertTo-Json -InputObject @($InputObject | Where-Object { $_ }) -Depth $Depth -Compress
    }
    else {
        $json = $InputObject | ConvertTo-Json -Depth $Depth -Compress
    }
    # Unary comma keeps the byte[] intact: a bare return unrolls it into the
    # pipeline and the caller recollects it as Object[], which Invoke-RestMethod
    # cannot send as a raw body (it stringifies) → the dashboard 400s every event.
    return , [System.Text.Encoding]::UTF8.GetBytes($json)
}

function Invoke-JsonRequest {
    param([string]$Method, [string]$Uri, [byte[]]$Body, [hashtable]$Headers, [scriptblock]$InvokeRest)
    if ($InvokeRest) { return (& $InvokeRest $Method $Uri $Body $Headers) }
    return (Invoke-RestMethod -Method $Method -Uri $Uri -Body $Body `
        -ContentType "application/json; charset=utf-8" `
        -Headers $Headers -TimeoutSec 30)
}
