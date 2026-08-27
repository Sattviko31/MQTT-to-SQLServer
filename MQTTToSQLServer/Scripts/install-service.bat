@echo off
setlocal EnableDelayedExpansion

net session >nul 2>&1
if %errorLevel% neq 0 (
    echo [!] Please run this script as Administrator!
    pause
    exit /b 1
)

set SERVICE_NAME=MQTTToSQLServer
set DISPLAY_NAME=MQTT To SQL Server Listener
set SCRIPT_DIR=%~dp0

set "PUBLISH_DIR="

for %%I in ("%SCRIPT_DIR%..") do (
    if exist "%%~fI\MQTTToSQLServer.dll" set "PUBLISH_DIR=%%~fI"
)

if not defined PUBLISH_DIR (
    for %%I in ("%SCRIPT_DIR%") do (
        if exist "%%~fI\MQTTToSQLServer.dll" set "PUBLISH_DIR=%%~fI"
    )
)

if not defined PUBLISH_DIR (
    for %%I in ("%SCRIPT_DIR%..\..\#PUBLISH") do (
        if exist "%%~fI\MQTTToSQLServer.dll" set "PUBLISH_DIR=%%~fI"
    )
)

if not defined PUBLISH_DIR (
    if exist "C:\Users\eats\Documents\WORKSPACE\REPO\MQTT-to-SQLServer\#PUBLISH\MQTTToSQLServer.dll" (
        set "PUBLISH_DIR=C:\Users\eats\Documents\WORKSPACE\REPO\MQTT-to-SQLServer\#PUBLISH"
    )
)

if not defined PUBLISH_DIR (
    echo [!] Could not locate publish directory containing MQTTToSQLServer.dll.
    pause
    exit /b 1
)

echo [*] Publish Directory : %PUBLISH_DIR%

set "DOTNET_EXE=C:\Program Files\dotnet\dotnet.exe"
if not exist "%DOTNET_EXE%" (
    for /f "delims=" %%i in ('where dotnet 2^>nul') do (
        set "DOTNET_EXE=%%i"
        goto :got_dotnet
    )
)
:got_dotnet

if not exist "%DOTNET_EXE%" (
    echo [!] dotnet.exe runtime not found.
    pause
    exit /b 1
)

echo [*] .NET Host Path    : %DOTNET_EXE%

if exist "%PUBLISH_DIR%\MQTTToSQLServer.exe" (
    set "BIN_PATH=\"%PUBLISH_DIR%\MQTTToSQLServer.exe\""
    echo [*] Launch Target     : %PUBLISH_DIR%\MQTTToSQLServer.exe
) else (
    set "BIN_PATH=\"%DOTNET_EXE%\" \"%PUBLISH_DIR%\MQTTToSQLServer.dll\""
    echo [*] Launch Target     : %DOTNET_EXE% %PUBLISH_DIR%\MQTTToSQLServer.dll
)

sc.exe query "%SERVICE_NAME%" >nul 2>&1
if %errorLevel% equ 0 (
    echo [*] Updating existing service configuration...
    sc.exe config "%SERVICE_NAME%" binPath= "%BIN_PATH%" start= auto DisplayName= "%DISPLAY_NAME%" >nul 2>&1
) else (
    echo [*] Registering Windows Service '%SERVICE_NAME%'...
    sc.exe create "%SERVICE_NAME%" binPath= "%BIN_PATH%" start= auto DisplayName= "%DISPLAY_NAME%"
)

sc.exe failure "%SERVICE_NAME%" reset= 86400 actions= restart/60000/restart/60000/restart/60000 >nul 2>&1
sc.exe description "%SERVICE_NAME%" "Background listener service for streaming MQTT meter messages into SQL Server." >nul 2>&1

echo [*] Starting service '%SERVICE_NAME%'...
sc.exe start "%SERVICE_NAME%"

echo.
echo [OK] Service '%SERVICE_NAME%' setup completed!
echo [*] Logs are written to: '%PUBLISH_DIR%\logs\'
echo.
pause
