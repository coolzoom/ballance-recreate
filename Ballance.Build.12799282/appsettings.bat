@echo off
REM We are using the 32 bit version of REG.EXE on purpose so it works like the app
REM SET INITIAL LANGUAGE 0 - 
"%SYSTEMROOT%\\System32\\REG.EXE" ADD "HKEY_CLASSES_ROOT\VirtualStore\MACHINE\SOFTWARE\WOW6432Node\Ballance\Settings" /v language /t REG_DWORD /d 1 /f /reg:32
"%SYSTEMROOT%\\System32\\REG.EXE" ADD "HKEY_LOCAL_MACHINE\SOFTWARE\Ballance\Settings" /v language /t REG_DWORD /d 1 /f /reg:32
REM SET INITAL DISPLAY TO FULLSCREEN (1), 0 is WINDOWED
"%SYSTEMROOT%\\System32\\REG.EXE" ADD "HKEY_CLASSES_ROOT\VirtualStore\MACHINE\SOFTWARE\WOW6432Node\Ballance\Settings" /v FullScreen /t REG_DWORD /d 1 /f /reg:32
"%SYSTEMROOT%\\System32\\REG.EXE" ADD "HKEY_LOCAL_MACHINE\SOFTWARE\Ballance\Settings" /v FullScreen /t REG_DWORD /d 1 /f /reg:32
REM SET INITAL DISPLAY TO FIRST MONITOR
"%SYSTEMROOT%\\System32\\REG.EXE" ADD "HKEY_CLASSES_ROOT\VirtualStore\MACHINE\SOFTWARE\WOW6432Node\Ballance\Settings" /v VideoDriver /t REG_DWORD /d 0 /f /reg:32
"%SYSTEMROOT%\\System32\\REG.EXE" ADD "HKEY_LOCAL_MACHINE\SOFTWARE\Ballance\Settings" /v VideoDriver /t REG_DWORD /d 0 /f /reg:32
REM SET INITAL DISPLAY RESOLUTION TO SOMETHING MOST COMPUTERS HANDLE
"%SYSTEMROOT%\\System32\\REG.EXE" ADD "HKEY_CLASSES_ROOT\VirtualStore\MACHINE\SOFTWARE\WOW6432Node\Ballance\Settings" /v VideoMode /t REG_DWORD /d 41943520 /f /reg:32
"%SYSTEMROOT%\\System32\\REG.EXE" ADD "HKEY_LOCAL_MACHINE\SOFTWARE\Ballance\Settings" /v VideoMode /t REG_DWORD /d 41943520 /f /reg:32
goto done
:done
