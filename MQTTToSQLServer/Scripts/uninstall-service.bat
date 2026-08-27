@echo off
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo [!] Please run this script as Administrator!
    pause
    exit /b 1
)

set SERVICE_NAME=MQTTToSQLServer

echo [*] Stopping service '%SERVICE_NAME%'...
sc.exe stop "%SERVICE_NAME%"
timeout /t 3 /nobreak >nul

echo [*] Removing service '%SERVICE_NAME%'...
sc.exe delete "%SERVICE_NAME%"

echo [✓] Service '%SERVICE_NAME%' removed successfully!
pause
