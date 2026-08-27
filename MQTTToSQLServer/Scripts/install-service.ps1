# Requires Administrator
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "[!] Please run PowerShell as Administrator!" -ForegroundColor Red
    pause
    exit 1
}

$serviceName = "MQTTToSQLServer"
$displayName = "MQTT To SQL Server Listener"
$scriptDir = $PSScriptRoot

# Locate #PUBLISH directory
$publishDir = $null
$candidatePaths = @(
    (Join-Path $scriptDir ".."),
    $scriptDir,
    (Join-Path $scriptDir "..\..\#PUBLISH"),
    "C:\Users\eats\Documents\WORKSPACE\REPO\MQTT-to-SQLServer\#PUBLISH"
)

foreach ($path in $candidatePaths) {
    if (Test-Path (Join-Path $path "MQTTToSQLServer.dll")) {
        $publishDir = (Resolve-Path $path).Path
        break
    }
}

if (-not $publishDir) {
    Write-Host "[!] Could not locate publish directory containing MQTTToSQLServer.dll." -ForegroundColor Red
    pause
    exit 1
}

Write-Host "[*] Publish Directory : $publishDir" -ForegroundColor Cyan

# Locate dotnet.exe
$dotnetExe = "C:\Program Files\dotnet\dotnet.exe"
if (-not (Test-Path $dotnetExe)) {
    $dotnetExe = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
}

if (-not $dotnetExe) {
    Write-Host "[!] dotnet.exe runtime not found." -ForegroundColor Red
    pause
    exit 1
}

Write-Host "[*] .NET Host Path    : $dotnetExe" -ForegroundColor Cyan

# Determine Binary Path
if (Test-Path (Join-Path $publishDir "MQTTToSQLServer.exe")) {
    $binPath = "`"$publishDir\MQTTToSQLServer.exe`""
    Write-Host "[*] Launch Target     : $publishDir\MQTTToSQLServer.exe" -ForegroundColor Green
} else {
    $binPath = "`"$dotnetExe`" `"$publishDir\MQTTToSQLServer.dll`""
    Write-Host "[*] Launch Target     : $dotnetExe $publishDir\MQTTToSQLServer.dll" -ForegroundColor Green
}

# Check if service already exists
$existingService = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
if ($existingService) {
    Write-Host "[*] Updating existing service configuration..." -ForegroundColor Yellow
    & sc.exe config $serviceName binPath= $binPath start= auto DisplayName= $displayName
} else {
    Write-Host "[*] Registering Windows Service '$serviceName'..." -ForegroundColor Cyan
    & sc.exe create $serviceName binPath= $binPath start= auto DisplayName= $displayName
}

& sc.exe failure $serviceName reset= 86400 actions= restart/60000/restart/60000/restart/60000 | Out-Null
& sc.exe description $serviceName "Background listener service for streaming MQTT meter messages into SQL Server." | Out-Null

Write-Host "[*] Starting service '$serviceName'..." -ForegroundColor Cyan
Start-Service -Name $serviceName -ErrorAction SilentlyContinue
if ((Get-Service -Name $serviceName).Status -ne 'Running') {
    & sc.exe start $serviceName
}

Write-Host ""
Write-Host "[OK] Service '$serviceName' setup completed and running!" -ForegroundColor Green
Write-Host "[*] Logs are written to: '$publishDir\logs\'" -ForegroundColor Gray
Write-Host ""
pause
