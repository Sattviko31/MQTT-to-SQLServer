$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host "[!] Please run PowerShell as Administrator!" -ForegroundColor Red
    pause
    exit 1
}

$serviceName = "MQTTToSQLServer"

Write-Host "[*] Stopping service '$serviceName'..." -ForegroundColor Yellow
Stop-Service -Name $serviceName -Force -ErrorAction SilentlyContinue
& sc.exe stop $serviceName | Out-Null
Start-Sleep -Seconds 2

Write-Host "[*] Removing service '$serviceName'..." -ForegroundColor Yellow
& sc.exe delete $serviceName

Write-Host ""
Write-Host "[OK] Service '$serviceName' uninstalled successfully!" -ForegroundColor Green
Write-Host ""
pause
