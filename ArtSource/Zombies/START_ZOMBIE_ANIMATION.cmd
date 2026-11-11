@echo off
setlocal
set "ENFILADE_BLENDER=F:\SteamLibrary\steamapps\common\Blender\blender.exe"
if defined BLENDER_EXE set "ENFILADE_BLENDER=%BLENDER_EXE%"
if not exist "%ENFILADE_BLENDER%" (
  echo Set BLENDER_EXE to your blender.exe path or open ENFILADE_Zombie_Animation.blend directly.
  pause
  exit /b 1
)
start "" "%ENFILADE_BLENDER%" --disable-autoexec "%~dp0ENFILADE_Zombie_Animation.blend" --python "%~dp0..\..\Tools\Blender\Zombies\zombie_tools.py"
endlocal
