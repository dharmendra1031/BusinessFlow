param(
    [Parameter(Mandatory = $true)][uri]$BaseUrl
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($BaseUrl.Scheme -ne 'https') {
    throw 'Release verification requires an HTTPS address and a trusted certificate.'
}

$root = $BaseUrl.AbsoluteUri.TrimEnd('/')
function Check-Endpoint([string]$path) {
    $response = Invoke-WebRequest -Uri "$root$path" -UseBasicParsing -TimeoutSec 20
    if ($response.StatusCode -ne 200) {
        throw "$path returned HTTP $($response.StatusCode)"
    }
    Write-Host "PASS: $path (HTTP 200)"
    return $response
}

$live = Check-Endpoint '/health/live'
$ready = Check-Endpoint '/health/ready'
$site = Check-Endpoint '/'

if ($ready.Content -notmatch '"status"\s*:\s*"ready"') {
    throw 'SQL readiness did not report ready.'
}
if ($site.Content -notmatch '<html') {
    throw 'The site did not return the React entry page.'
}

foreach ($header in @('Strict-Transport-Security', 'Content-Security-Policy',
                      'X-Content-Type-Options', 'X-Frame-Options', 'Referrer-Policy',
                      'X-Request-Id')) {
    if (-not $site.Headers[$header]) {
        throw "Required production response header is missing: $header"
    }
    Write-Host "PASS: $header"
}

Write-Host 'Staging HTTP/TLS checks passed.'
Write-Host 'Still required: authorized login, role permissions, database restore, backup and rollback drills.'
