@echo off

echo Please choose your language:
echo 0. German
echo 1. English
echo 2. Spanish
echo 3. Italian
echo 4. French

set /p lang=Enter your choice (0-4): 

if "m%lang%" == "m0" goto setandrun
if "m%lang%" == "m1" goto setandrun
if "m%lang%" == "m2" goto setandrun
if "m%lang%" == "m3" goto setandrun
if "m%lang%" == "m4" goto setandrun

goto done

:setandrun
REM Steam Deck or running as Admin
"%SYSTEMROOT%\\System32\\REG.EXE" ADD "HKEY_LOCAL_MACHINE\SOFTWARE\Ballance\Settings" /v language /t REG_DWORD /d %lang% /f /reg:32 > NUL 2> NUL
REM Windows 10 and 11 regular user
"%SYSTEMROOT%\\System32\\REG.EXE" ADD "HKEY_CLASSES_ROOT\VirtualStore\MACHINE\SOFTWARE\WOW6432Node\Ballance\Settings" /v language /t REG_DWORD /d %lang% /f /reg:32 > NUL 2> NUL
:done
REM pause
