@echo off
:: Ensure Admin rights
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo [!] Please run this script as Administrator!
    pause
    exit /b 1
)

set SERVICE_NAME=MQTTToSQLServer
set DISPLAY_NAME=MQTT To SQL Server Listener
set SCRIPT_DIR=%~dp0
set EXE_PATH=%SCRIPT_DIR%..\bin\Release\netcoreapp2.1\publish\MQTTToSQLServer.exe

:: Check if publish exists, otherwise check debug
if not exist "%EXE_PATH%" (
    set EXE_PATH=%SCRIPT_DIR%..\bin\Release\netcoreapp2.1\MQTTToSQLServer.exe
)
if not exist "%EXE_PATH%" (
    set EXE_PATH=%SCRIPT_DIR%..\bin\Debug\netcoreapp2.1\MQTTToSQLServer.exe
)

if not exist "%EXE_PATH%" (
    echo [!] Could not find MQTTToSQLServer.exe.
    echo [*] Please run: dotnet publish -c Release
    pause
    exit /b 1
)

echo [*] Target Executable: %EXE_PATH%

:: 1. Create the Windows Service
echo [*] Registering Windows Service '%SERVICE_NAME%'...
sc.exe create "%SERVICE_NAME%" binPath= "\"%EXE_PATH%\"" start= auto DisplayName= "%DISPLAY_NAME%"

:: 2. Set Failure Recovery (Auto-restart if crashed)
sc.exe failure "%SERVICE_NAME%" reset= 86400 actions= restart/60000/restart/60000/restart/60000

:: 3. Set Description
sc.exe description "%SERVICE_NAME%" "Background listener service for streaming MQTT meter messages into SQL Server."

:: 4. Start Service
echo [*] Starting service '%SERVICE_NAME%'...
sc.exe start "%SERVICE_NAME%"

echo [✓] Service '%SERVICE_NAME%' installed and started successfully!
pause
