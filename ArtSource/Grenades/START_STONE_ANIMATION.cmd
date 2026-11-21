@echo off
rem The throw workspace opened on the stone's throw (T). START_GRENADE_ANIMATION.cmd opens the same file on the last clip worked on.
setlocal
set "THROW_BLENDER=F:\SteamLibrary\steamapps\common\Blender\blender.exe"
if defined BLENDER_EXE set "THROW_BLENDER=%BLENDER_EXE%"
if not exist "%THROW_BLENDER%" (
  echo Blender was not found. Set BLENDER_EXE to your blender.exe path.
  echo You can also open ENFILADE_Grenade_Animation.blend and run its "START HERE - Throw tools.py" text with Alt+P.
  pause
  exit /b 1
)
start "" "%THROW_BLENDER%" --disable-autoexec "%~dp0ENFILADE_Grenade_Animation.blend" --python "%~dp0..\..\Tools\Blender\TrueFPS\truefps_tools.py" --python "%~dp0..\..\Tools\Blender\Grenades\grenade_tools.py" -- --clip stone_throw
endlocal
