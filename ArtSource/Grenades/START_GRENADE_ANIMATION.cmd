@echo off
setlocal
set "THROW_BLENDER=F:\SteamLibrary\steamapps\common\Blender\blender.exe"
if defined BLENDER_EXE set "THROW_BLENDER=%BLENDER_EXE%"
if not exist "%THROW_BLENDER%" (
  echo Blender was not found. Set BLENDER_EXE to your blender.exe path.
  echo You can also open ENFILADE_Grenade_Animation.blend and run its "START HERE - Throw tools.py" text with Alt+P.
  pause
  exit /b 1
)
start "" "%THROW_BLENDER%" --disable-autoexec "%~dp0ENFILADE_Grenade_Animation.blend" --python "%~dp0..\..\Tools\Blender\TrueFPS\truefps_tools.py" --python "%~dp0..\..\Tools\Blender\Grenades\grenade_tools.py"
endlocal
