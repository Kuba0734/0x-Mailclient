@echo off
setlocal EnableExtensions DisableDelayedExpansion
title Git Commit ^& Push

rem In den Ordner wechseln, in dem diese .bat liegt
cd /d "%~dp0"

rem --- Pruefungen ---------------------------------------------------------
where git >nul 2>&1
if errorlevel 1 (
    echo [FEHLER] Git wurde nicht gefunden. Ist Git installiert und im PATH?
    pause
    exit /b 1
)

git rev-parse --is-inside-work-tree >nul 2>&1
if errorlevel 1 (
    echo [FEHLER] Dieser Ordner ist kein Git-Repository:
    echo %CD%
    pause
    exit /b 1
)

for /f "delims=" %%b in ('git rev-parse --abbrev-ref HEAD') do set "BRANCH=%%b"

echo ==========================================
echo  Ordner : %CD%
echo  Branch : %BRANCH%
echo ==========================================
echo.

rem --- Gibt es Aenderungen? ----------------------------------------------
git status --porcelain | findstr . >nul
if errorlevel 1 (
    echo Keine Aenderungen zum Committen.
    echo Pruefe trotzdem, ob noch etwas zu pushen ist...
    echo.
    goto push
)

echo Geaenderte Dateien:
git status --short
echo.

rem --- Commit-Nachricht abfragen -----------------------------------------
:ask
set "MSG="
set /p "MSG=Was wurde geaendert? "
if not defined MSG (
    echo Bitte eine Beschreibung eingeben.
    goto ask
)

rem Nachricht sicher in Datei schreiben (auch mit Sonderzeichen wie ^& ^| ^< ^> ")
set "MSGFILE=%TEMP%\git_commit_msg_%RANDOM%.txt"
setlocal EnableDelayedExpansion
> "!MSGFILE!" echo(!MSG!
endlocal

rem --- Commit ------------------------------------------------------------
git add -A
git commit -F "%MSGFILE%"
set "ERR=%ERRORLEVEL%"
del "%MSGFILE%" >nul 2>&1
if not "%ERR%"=="0" (
    echo.
    echo [FEHLER] Commit fehlgeschlagen.
    pause
    exit /b 1
)

rem --- Push --------------------------------------------------------------
:push
echo.
git rev-parse --abbrev-ref --symbolic-full-name @{u} >nul 2>&1
if errorlevel 1 (
    echo Kein Upstream gesetzt - pushe nach origin/%BRANCH% ...
    git push -u origin "%BRANCH%"
) else (
    git push
)
if errorlevel 1 (
    echo.
    echo [FEHLER] Push fehlgeschlagen. Evtl. vorher "git pull" noetig?
    pause
    exit /b 1
)

echo.
echo ==========================================
echo  Fertig! Alles committed und gepusht.
echo ==========================================
pause