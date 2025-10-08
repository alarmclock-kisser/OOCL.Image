@echo off
setlocal enabledelayedexpansion

REM === CONFIG ===
set SOLUTION_DIR=%~dp0
set LOG_FILE=%SOLUTION_DIR%publish.log
set CONFIG=Release

set API_PROJECT=%SOLUTION_DIR%OOCL.Image.Api\OOCL.Image.Api.csproj
set API_OUTPUT=C:\Users\Administrator\PublishedApps\api.oocl.work\OOCL.Image.Api

set WEBAPP_PROJECT=%SOLUTION_DIR%OOCL.Image.WebApp\OOCL.Image.WebApp.csproj
set WEBAPP_OUTPUT=C:\Users\Administrator\PublishedApps\api.oocl.work\OOCL.Image.WebApp

REM === COLORS ===
for /f "delims=" %%a in ('echo prompt $E ^| cmd') do set "ESC=%%a"
set GREEN=%ESC%[92m
set RED=%ESC%[91m
set YELLOW=%ESC%[93m
set RESET=%ESC%[0m

REM === START LOG ===
echo ================================================== >> "%LOG_FILE%"
echo [%date% %time%] START PUBLISH SESSION >> "%LOG_FILE%"
echo ================================================== >> "%LOG_FILE%"
echo. >> "%LOG_FILE%"

set ERR=0

call :LogAndEcho "%YELLOW%--> Publishing API...%RESET%" "Publishing API..."
if exist "%API_OUTPUT%" (
    call :LogAndEcho "Lösche alten Inhalt: %API_OUTPUT%" "Deleting old content"
    rmdir /S /Q "%API_OUTPUT%"
)
dotnet publish "%API_PROJECT%" -c %CONFIG% -o "%API_OUTPUT%" >> "%LOG_FILE%" 2>&1
if %errorlevel% neq 0 (
    call :LogAndEcho "%RED%API Publish failed!%RESET%" "API Publish failed!"
    set ERR=1
    goto :END
)
call :LogAndEcho "%GREEN%API erfolgreich veröffentlicht nach:%RESET% %API_OUTPUT%" "API published successfully"

echo. >> "%LOG_FILE%"
echo. >> "%LOG_FILE%"

call :LogAndEcho "%YELLOW%--> Publishing WebApp...%RESET%" "Publishing WebApp..."
if exist "%WEBAPP_OUTPUT%" (
    call :LogAndEcho "Lösche alten Inhalt: %WEBAPP_OUTPUT%" "Deleting old content"
    rmdir /S /Q "%WEBAPP_OUTPUT%"
)
dotnet publish "%WEBAPP_PROJECT%" -c %CONFIG% -o "%WEBAPP_OUTPUT%" >> "%LOG_FILE%" 2>&1
if %errorlevel% neq 0 (
    call :LogAndEcho "%RED%WebApp Publish failed!%RESET%" "WebApp Publish failed!"
    set ERR=1
    goto :END
)
call :LogAndEcho "%GREEN%WebApp erfolgreich veröffentlicht nach:%RESET% %WEBAPP_OUTPUT%" "WebApp published successfully"

echo. >> "%LOG_FILE%"
echo. >> "%LOG_FILE%"

:END
if %ERR% neq 0 (
    call :LogAndEcho "%RED%Ein oder mehrere Publish-Vorgänge sind fehlgeschlagen.%RESET%" "One or more publish steps failed."
) else (
    call :LogAndEcho "%GREEN%Alle Projekte erfolgreich veröffentlicht.%RESET%" "All projects published successfully."
)

echo.
echo %YELLOW%Vollständiges Log unter:%RESET% "%LOG_FILE%"
echo.
pause
endlocal
exit /b 0

:LogAndEcho
REM === Hilfsroutine: schreibt auf Konsole + Log ===
echo %~1
echo [%date% %time%] %~2 >> "%LOG_FILE%"
goto :eof
