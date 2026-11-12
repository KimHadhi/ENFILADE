@echo off
setlocal
set "SHOVE_BLENDER=F:\SteamLibrary\steamapps\common\Blender\blender.exe"
if defined BLENDER_EXE set "SHOVE_BLENDER=%BLENDER_EXE%"
if not exist "%SHOVE_BLENDER%" (
  echo Blender was not found. Set BLENDER_EXE to your blender.exe path.
  echo You can also open ENFILADE_Shove_Animation.blend and run its "START HERE - Shove tools.py" text with Alt+P.
  pause
  exit /b 1
)
start "" "%SHOVE_BLENDER%" --disable-autoexec "%~dp0ENFILADE_Shove_Animation.blend" --python "%~dp0..\..\Tools\Blender\TrueFPS\truefps_tools.py" --python "%~dp0..\..\Tools\Blender\Shove\shove_tools.py"
endlocal
