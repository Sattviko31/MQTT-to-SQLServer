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

:: 1. Search for publish directory or executable / dll
set PUBLISH_DIR=

if exist "%SCRIPT_DIR%..\..\#PUBLISH\MQTTToSQLServer.dll" (
    pushd "%SCRIPT_DIR%..\..\#PUBLISH"
    set "PUBLISH_DIR=%CD%"
    popd
) else if exist "%SCRIPT_DIR%..\#PUBLISH\MQTTToSQLServer.dll" (
    pushd "%SCRIPT_DIR%..\#PUBLISH"
    set "PUBLISH_DIR=%CD%"
    popd
) else if exist "%SCRIPT_DIR%..\MQTTToSQLServer.dll" (
    pushd "%SCRIPT_DIR%.."
    set "PUBLISH_DIR=%CD%"
    popd
) else if exist "%SCRIPT_DIR%MQTTToSQLServer.dll" (
    pushd "%SCRIPT_DIR%"
    set "PUBLISH_DIR=%CD%"
    popd
) else if exist "C:\Users\eats\Documents\WORKSPACE\REPO\MQTT-to-SQLServer\#PUBLISH\MQTTToSQLServer.dll" (
    set "PUBLISH_DIR=C:\Users\eats\Documents\WORKSPACE\REPO\MQTT-to-SQLServer\#PUBLISH"
)

if "%PUBLISH_DIR%"=="" (
    echo [!] Could not find #PUBLISH folder containing MQTTToSQLServer.dll.
    echo [*] Please run: dotnet publish -c Release -o ..\#PUBLISH
    pause
    exit /b 1
)

echo [*] Publish Directory: %PUBLISH_DIR%

:: 2. Find dotnet.exe path
set "DOTNET_EXE=C:\Program Files\dotnet\dotnet.exe"
if not exist "%DOTNET_EXE%" (
    for /f "delims=" %%i in ('where dotnet 2^>nul') do (
        set "DOTNET_EXE=%%i"
        goto :got_dotnet
    )
)
:got_dotnet

if not exist "%DOTNET_EXE%" (
    echo [!] Could not find dotnet.exe runtime.
    pause
    exit /b 1
)

:: 3. Determine binPath: Use .exe if present, otherwise use dotnet.exe <dll>
if exist "%PUBLISH_DIR%\MQTTToSQLServer.exe" (
    set "BIN_PATH=\"%PUBLISH_DIR%\MQTTToSQLServer.exe\""
    echo [*] Running via Executable: %PUBLISH_DIR%\MQTTToSQLServer.exe
) else (
    set "BIN_PATH=\"%DOTNET_EXE%\" \"%PUBLISH_DIR%\MQTTToSQLServer.dll\""
    echo [*] Running via .NET Core Host: %DOTNET_EXE% %PUBLISH_DIR%\MQTTToSQLServer.dll
)

:: 4. Stop and remove existing service if already registered
sc.exe query "%SERVICE_NAME%" >nul 2>&1
if %errorLevel% equ 0 (
    echo [*] Stopping previous instance of service...
    sc.exe stop "%SERVICE_NAME%" >nul 2>&1
    timeout /t 2 /nobreak >nul
    sc.exe delete "%SERVICE_NAME%" >nul 2>&1
    timeout /t 2 /nobreak >nul
)

:: 5. Create the Windows Service
echo [*] Registering Windows Service '%SERVICE_NAME%'...
sc.exe create "%SERVICE_NAME%" binPath= "%BIN_PATH%" start= auto DisplayName= "%DISPLAY_NAME%"

:: 6. Set Failure Recovery (Auto-restart on failure)
sc.exe failure "%SERVICE_NAME%" reset= 86400 actions= restart/60000/restart/60000/restart/60000

:: 7. Set Description
sc.exe description "%SERVICE_NAME%" "Background listener service for streaming MQTT meter messages into SQL Server."

:: 8. Start Service
echo [*] Starting service '%SERVICE_NAME%'...
sc.exe start "%SERVICE_NAME%"

echo.
echo [✓] Service '%SERVICE_NAME%' installed and running successfully!
echo [*] Check status anytime in 'services.msc' or logs at '%PUBLISH_DIR%\logs\'
echo.
pause
