param(
    [Parameter(Mandatory = $true)][uri]$BaseUrl,
    [pscredential]$Credential,
    [switch]$VerifyBilling
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($BaseUrl.Scheme -ne 'https') {
    throw 'Release verification requires an HTTPS address and a trusted certificate.'
}

if ($VerifyBilling -and -not $Credential) {
    throw 'Billing smoke checks require a credential for an authorized staging Admin account.'
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
if ($Credential) {
    $session = New-Object Microsoft.PowerShell.Commands.WebRequestSession
    $password = $Credential.GetNetworkCredential().Password
    $body = @{ email = $Credential.UserName; password = $password } | ConvertTo-Json -Compress
    $password = $null
    $login = Invoke-WebRequest -Uri "$root/api/auth/login" -Method Post -Body $body `
        -ContentType 'application/json' -WebSession $session -UseBasicParsing -TimeoutSec 20
    $cookies = [string]::Join(';', @($login.Headers['Set-Cookie']))
    foreach ($attribute in @('CRM.Session=', 'HttpOnly', 'Secure', 'SameSite=Strict')) {
        if ($cookies -notmatch [regex]::Escape($attribute)) {
            throw "Production session cookie is missing $attribute"
        }
    }
    $me = Invoke-WebRequest -Uri "$root/api/auth/me" -WebSession $session -UseBasicParsing -TimeoutSec 20
    $csrf = ($me.Content | ConvertFrom-Json).csrfToken
    if ([string]::IsNullOrWhiteSpace($csrf)) { throw 'Session did not issue a CSRF token.' }
    if ($VerifyBilling) {
        $checks = @(
            @{ Path = '/api/admin/billing/invoices?pageSize=1'; Fields = @('items', 'total') },
            @{ Path = '/api/admin/billing/adjustments?state=Pending&pageSize=1'; Fields = @('items', 'total') },
            @{ Path = '/api/admin/billing/reports/outstanding?pageSize=1'; Fields = @('summary', 'items', 'total') },
            @{ Path = '/api/admin/billing/reports/collections?pageSize=1'; Fields = @('summary', 'items', 'total') },
            @{ Path = '/api/admin/billing/customers?pageSize=1'; Fields = @('items', 'total') }
        )
        foreach ($check in $checks) {
            $result = Invoke-WebRequest -Uri "$root$($check.Path)" -WebSession $session -UseBasicParsing -TimeoutSec 20
            if ($result.StatusCode -ne 200) {
                throw "$($check.Path) returned HTTP $($result.StatusCode)"
            }
            $payload = $result.Content | ConvertFrom-Json
            foreach ($field in $check.Fields) {
                if ($field -notin $payload.PSObject.Properties.Name) {
                    throw "$($check.Path) did not return $field"
                }
            }
            Write-Host "PASS: read-only $($check.Path)"
        }
    }
    try {
        Invoke-WebRequest -Uri "$root/api/auth/logout" -Method Post -WebSession $session `
            -UseBasicParsing -TimeoutSec 20 | Out-Null
        throw 'Logout without CSRF token was accepted.'
    }
    catch [System.Net.WebException] {
        if ([int]$_.Exception.Response.StatusCode -ne 400) { throw }
    }
    Invoke-WebRequest -Uri "$root/api/auth/logout" -Method Post -WebSession $session `
        -Headers @{ 'X-CSRF-TOKEN' = $csrf } -UseBasicParsing -TimeoutSec 20 | Out-Null
    try {
        Invoke-WebRequest -Uri "$root/api/auth/me" -WebSession $session -UseBasicParsing -TimeoutSec 20 | Out-Null
        throw 'Session stayed active after logout.'
    }
    catch [System.Net.WebException] {
        if ([int]$_.Exception.Response.StatusCode -ne 401) { throw }
    }
    Write-Host 'PASS: production cookie login, CSRF logout protection and sign-out'
}
Write-Host 'Still required: role workflows, database restore, backup and rollback drills.'
Write-Host 'Billing smoke checks are read-only and do not certify payment/refund processing.'
