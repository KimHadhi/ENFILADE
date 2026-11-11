@echo off
setlocal
rem Finds Blender 4.5 on this PC: BLENDER_EXE if set, then the usual install places (Steam, blender.org installer).
rem Plain ASCII on purpose: cmd misreads a batch file with Korean text once the code page changes.
set "ENFILADE_BLENDER="
if defined BLENDER_EXE if exist "%BLENDER_EXE%" set "ENFILADE_BLENDER=%BLENDER_EXE%"
if not defined ENFILADE_BLENDER if exist "F:\SteamLibrary\steamapps\common\Blender\blender.exe" set "ENFILADE_BLENDER=F:\SteamLibrary\steamapps\common\Blender\blender.exe"
if not defined ENFILADE_BLENDER if exist "%ProgramFiles(x86)%\Steam\steamapps\common\Blender\blender.exe" set "ENFILADE_BLENDER=%ProgramFiles(x86)%\Steam\steamapps\common\Blender\blender.exe"
if not defined ENFILADE_BLENDER if exist "%ProgramFiles%\Blender Foundation\Blender 4.5\blender.exe" set "ENFILADE_BLENDER=%ProgramFiles%\Blender Foundation\Blender 4.5\blender.exe"
if not defined ENFILADE_BLENDER for /d %%D in ("%ProgramFiles%\Blender Foundation\Blender*") do if exist "%%~D\blender.exe" set "ENFILADE_BLENDER=%%~D\blender.exe"
if not defined ENFILADE_BLENDER (
  echo Blender 4.5 was not found on this PC.
  echo Install Blender 4.5 LTS from blender.org, or set BLENDER_EXE to the full path of blender.exe.
  echo Or open ENFILADE_Zombie_Animation.blend yourself and press Alt+P on the START HERE text in the Scripting tab.
  pause
  exit /b 1
)
start "" "%ENFILADE_BLENDER%" --disable-autoexec "%~dp0ENFILADE_Zombie_Animation.blend" --python "%~dp0..\..\Tools\Blender\Zombies\zombie_tools.py"
endlocal
