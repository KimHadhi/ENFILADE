@echo off
setlocal
rem Opens the victory pose workspace (ENFILADE_Poses_Mayo.blend) with the Pose tab (Tools\Blender\Poses\pose_tools.py).
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
  echo Or open ENFILADE_Poses_Mayo.blend yourself and press Alt+P on the START HERE text in the Scripting tab.
  pause
  exit /b 1
)
rem No workspace yet: make it from the recipes first, about a minute.
if not exist "%~dp0ENFILADE_Poses_Mayo.blend" "%ENFILADE_BLENDER%" -b --factory-startup --python "%~dp0..\..\Tools\Blender\Poses\make_victory_poses.py"
start "" "%ENFILADE_BLENDER%" --disable-autoexec "%~dp0ENFILADE_Poses_Mayo.blend" --python "%~dp0..\..\Tools\Blender\Poses\pose_tools.py"
endlocal
