$ErrorActionPreference = 'Stop'

$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$frontend = Join-Path $repo 'frontend'
$api = Join-Path $repo 'backend/LeadManagement.Api'
$renderer = Join-Path $repo 'reporting/ReportRenderer'
$wwwroot = Join-Path $api 'wwwroot'
$artifacts = Join-Path $repo 'artifacts'
$publish = Join-Path $artifacts 'iis'

function Assert-UnderRepo([string]$path) {
    $full = [System.IO.Path]::GetFullPath($path)
    if (-not $full.StartsWith($repo + [System.IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to remove a path outside the repository: $full"
    }
}

Assert-UnderRepo $wwwroot
Assert-UnderRepo $publish

Push-Location $frontend
try {
    npm ci
    if ($LASTEXITCODE -ne 0) { throw 'npm ci failed.' }
    npm run build
    if ($LASTEXITCODE -ne 0) { throw 'Frontend build failed.' }
} finally { Pop-Location }

if (Test-Path -LiteralPath $wwwroot) { Remove-Item -LiteralPath $wwwroot -Recurse -Force }
New-Item -ItemType Directory -Path $wwwroot -Force | Out-Null
Copy-Item -Path (Join-Path $frontend 'dist/*') -Destination $wwwroot -Recurse -Force

if (Test-Path -LiteralPath $publish) { Remove-Item -LiteralPath $publish -Recurse -Force }
Push-Location $api
try {
    dotnet publish -c Release -r win-x64 --self-contained false -o $publish
    if ($LASTEXITCODE -ne 0) { throw 'API publish failed.' }
    dotnet ef migrations bundle --configuration Release --target-runtime win-x64 --output (Join-Path $artifacts 'efbundle.exe') --force
    if ($LASTEXITCODE -ne 0) { throw 'Migration bundle failed.' }
} finally { Pop-Location }

dotnet publish (Join-Path $renderer 'ReportRenderer.csproj') -c Release -o (Join-Path $publish 'ReportRenderer')
if ($LASTEXITCODE -ne 0) { throw 'RDLC renderer publish failed.' }
if (-not (Test-Path -LiteralPath (Join-Path $publish 'ReportRenderer/ReportRenderer.exe'))) {
    throw 'RDLC renderer is missing from the IIS package.'
}
foreach ($reportType in @('customers','user-customers','active','closed','not-interested',
                         'followups','overdue','project','user-project','created')) {
    if (-not (Test-Path -LiteralPath (Join-Path $publish "ReportRenderer/Reports/$reportType.rdlc"))) {
        throw "RDLC template is missing: $reportType"
    }
}

if (Test-Path -LiteralPath (Join-Path $publish 'appsettings.Development.json')) {
    throw 'Development settings were included in the IIS package.'
}
if (-not (Test-Path -LiteralPath (Join-Path $publish 'wwwroot/index.html'))) {
    throw 'Frontend files are missing from the IIS package.'
}
if (-not (Test-Path -LiteralPath (Join-Path $publish 'web.config'))) {
    throw 'IIS web.config is missing from the publish output.'
}
if (-not (Select-String -LiteralPath (Join-Path $publish 'web.config') -Pattern 'hostingModel="outofprocess"' -Quiet)) {
    throw 'IIS web.config must use outofprocess hosting.'
}

$zip = Join-Path $artifacts 'LeadManagement-IIS.zip'
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
$bundleInPublish = Join-Path $publish 'efbundle.exe'
$demoInPublish = Join-Path $publish 'seed_kuwait_demo.sql'
try {
    Copy-Item -LiteralPath (Join-Path $artifacts 'efbundle.exe') -Destination $bundleInPublish -Force
    Copy-Item -LiteralPath (Join-Path $repo 'database/seed_kuwait_demo.sql') -Destination $demoInPublish -Force
    Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $zip
} finally {
    if (Test-Path -LiteralPath $bundleInPublish) { Remove-Item -LiteralPath $bundleInPublish -Force }
    if (Test-Path -LiteralPath $demoInPublish) { Remove-Item -LiteralPath $demoInPublish -Force }
}

Write-Host "IIS files: $publish"
Write-Host "IIS package: $zip"
Write-Host "Migration bundle: $(Join-Path $artifacts 'efbundle.exe')"
