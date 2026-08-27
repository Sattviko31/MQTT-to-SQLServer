@echo off
setlocal EnableDelayedExpansion

:: Check Admin rights
net session >nul 2>&1
if %errorLevel% neq 0 (
    echo [!] Please run this script as Administrator!
    pause
    exit /b 1
)

set SERVICE_NAME=MQTTToSQLServer
set DISPLAY_NAME=MQTT To SQL Server Listener
set SCRIPT_DIR=%~dp0

:: Find #PUBLISH directory
set "PUBLISH_DIR="

:: Case 1: Script is in #PUBLISH\Scripts\
for %%I in ("%SCRIPT_DIR%..") do (
    if exist "%%~fI\MQTTToSQLServer.dll" set "PUBLISH_DIR=%%~fI"
)

:: Case 2: Script is directly inside #PUBLISH\
if not defined PUBLISH_DIR (
    for %%I in ("%SCRIPT_DIR%") do (
        if exist "%%~fI\MQTTToSQLServer.dll" set "PUBLISH_DIR=%%~fI"
    )
)

:: Case 3: Script is in MQTTToSQLServer\Scripts\
if not defined PUBLISH_DIR (
    for %%I in ("%SCRIPT_DIR%..\..\#PUBLISH") do (
        if exist "%%~fI\MQTTToSQLServer.dll" set "PUBLISH_DIR=%%~fI"
    )
)

:: Case 4: Default absolute path fallback
if not defined PUBLISH_DIR (
    if exist "C:\Users\eats\Documents\WORKSPACE\REPO\MQTT-to-SQLServer\#PUBLISH\MQTTToSQLServer.dll" (
        set "PUBLISH_DIR=C:\Users\eats\Documents\WORKSPACE\REPO\MQTT-to-SQLServer\#PUBLISH"
    )
)

if not defined PUBLISH_DIR (
    echo [!] Could not locate #PUBLISH folder containing MQTTToSQLServer.dll.
    echo [*] Please verify the path or run publish first.
    pause
    exit /b 1
)

echo [*] Publish Directory : %PUBLISH_DIR%

:: Locate dotnet.exe
set "DOTNET_EXE=C:\Program Files\dotnet\dotnet.exe"
if not exist "%DOTNET_EXE%" (
    for /f "delims=" %%i in ('where dotnet 2^>nul') do (
        set "DOTNET_EXE=%%i"
        goto :got_dotnet
    )
)
:got_dotnet

if not exist "%DOTNET_EXE%" (
    echo [!] dotnet.exe not found on this machine.
    pause
    exit /b 1
)

echo [*] .NET Host Path    : %DOTNET_EXE%

:: Determine binary execution path
if exist "%PUBLISH_DIR%\MQTTToSQLServer.exe" (
    set "BIN_PATH=\"%PUBLISH_DIR%\MQTTToSQLServer.exe\""
    echo [*] Launch Target     : %PUBLISH_DIR%\MQTTToSQLServer.exe
) else (
    set "BIN_PATH=\"%DOTNET_EXE%\" \"%PUBLISH_DIR%\MQTTToSQLServer.dll\""
    echo [*] Launch Target     : %DOTNET_EXE% %PUBLISH_DIR%\MQTTToSQLServer.dll
)

:: Stop and remove previous service instance if exists
sc.exe query "%SERVICE_NAME%" >nul 2>&1
if %errorLevel% equ 0 (
    echo [*] Stopping existing service...
    sc.exe stop "%SERVICE_NAME%" >nul 2>&1
    timeout /t 2 /nobreak >nul
    echo [*] Deleting previous service definition...
    sc.exe delete "%SERVICE_NAME%" >nul 2>&1
    timeout /t 2 /nobreak >nul
)

:: Create Windows Service
echo [*] Registering Windows Service '%SERVICE_NAME%'...
sc.exe create "%SERVICE_NAME%" binPath= "%BIN_PATH%" start= auto DisplayName= "%DISPLAY_NAME%"

:: Configure failure recovery (restart automatically)
sc.exe failure "%SERVICE_NAME%" reset= 86400 actions= restart/60000/restart/60000/restart/60000

:: Set description
sc.exe description "%SERVICE_NAME%" "Background listener service for streaming MQTT meter messages into SQL Server."

:: Start service
echo [*] Starting service '%SERVICE_NAME%'...
sc.exe start "%SERVICE_NAME%"

echo.
echo [?] Service '%SERVICE_NAME%' registered successfully!
echo [*] You can check status in 'services.msc' or logs at '%PUBLISH_DIR%\logs\'
echo.
pause
