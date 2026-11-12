@echo off
setlocal
set "KNIFE_BLENDER=F:\SteamLibrary\steamapps\common\Blender\blender.exe"
if defined BLENDER_EXE set "KNIFE_BLENDER=%BLENDER_EXE%"
if not exist "%KNIFE_BLENDER%" (
  echo Blender was not found. Set BLENDER_EXE to your blender.exe path.
  pause
  exit /b 1
)
if not exist "%~dp0ENFILADE_Knife_Animation.blend" (
  echo Building the knife workspace for the first time ^(about a minute^)...
  pushd "%~dp0..\.."
  "%KNIFE_BLENDER%" --background --factory-startup "ArtSource\TrueFPS\ENFILADE_TrueFPS_Start.blend" --python "Tools\Blender\Knives\build_knife_workspace.py" -- "ArtSource\Knives\ENFILADE_Knife_Animation.blend"
  popd
)
start "" "%KNIFE_BLENDER%" --disable-autoexec "%~dp0ENFILADE_Knife_Animation.blend" --python "%~dp0..\..\Tools\Blender\TrueFPS\truefps_tools.py" --python "%~dp0..\..\Tools\Blender\Knives\knife_tools.py"
endlocal
