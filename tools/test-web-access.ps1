# Fork: end-to-end check of the web panel and Wi-Fi port against the RUNNING agent (Windows PowerShell 5.1+).
# Usage: powershell -File tools\test-web-access.ps1 [-Hosts 192.0.2.45,100.100.10.20]
# Reads the PIN and keys from %APPDATA%\PhoneBox\config.json; prints no secrets. Exit code 1 if any check fails.
param([string[]]$Hosts)

$ErrorActionPreference = 'Stop'
$cfg = Get-Content "$env:APPDATA\PhoneBox\config.json" -Raw | ConvertFrom-Json
$port = if ($cfg.lan.port) { $cfg.lan.port } else { 8766 }
$pin = $cfg.lan.webPin; $tok = $cfg.lan.token
$wrong = if ($pin -eq '000000') { '111111' } else { '000000' }
if (-not $Hosts) { $Hosts = (Invoke-RestMethod http://127.0.0.1:8765/api/web).addresses | ForEach-Object { ($_ -replace ':\d+$', '') } }

$failed = 0
function Check($name, $expected, [string[]]$curlArgs) {
    $got = (& curl.exe -sk --max-time 8 -o NUL -w '%{http_code}' @curlArgs) -join ''
    $ok = $got -eq $expected
    if (-not $ok) { $script:failed++ }
    '{0} {1,-46} {2} (expected {3})' -f ($(if ($ok) { 'PASS' } else { 'FAIL' })), $name, $got, $expected
}

foreach ($h in $Hosts) {
    $b = "https://${h}:$port"
    $jar = Join-Path $env:TEMP ("pb_test_" + ($h -replace '\W', '') + ".txt")
    Remove-Item $jar -ErrorAction SilentlyContinue
    "===== $h ====="
    Check 'http://IP:port redirects to https' '301' @("http://${h}:$port/")
    Check 'https / shows the PIN form' '200' @("$b/")
    Check '/panel/ without cookie -> PIN form' '302' @("$b/panel/")
    Check 'wrong PIN' '401' @('-d', "pin=$wrong", "$b/web/login")
    Check 'right PIN sets the cookie' '302' @('-c', $jar, '-H', "Origin: $b", '-d', "pin=$pin", "$b/web/login")
    Check '/panel/ with cookie' '200' @('-b', $jar, "$b/panel/")
    Check 'panel API with cookie (same-origin)' '200' @('-b', $jar, '-H', 'Sec-Fetch-Site: same-origin', "$b/agent/api/state")
    Check 'panel API with cookie (cross-site)' '403' @('-b', $jar, '-H', 'Sec-Fetch-Site: cross-site', "$b/agent/api/state")
    Check 'settings stay local-only' '403' @('-b', $jar, "$b/api/settings")
    Check 'login with a foreign Origin' '403' @('-H', 'Origin: https://evil.example', '-d', "pin=$pin", "$b/web/login")
    Check 'Android app (Bearer token)' '200' @('-H', "Authorization: Bearer $tok", "$b/api/state")
    Check 'no key at all' '403' @("$b/api/state")
    Remove-Item $jar -ErrorAction SilentlyContinue
}
if ($failed) { "$failed check(s) FAILED"; exit 1 } else { 'All checks passed' }
