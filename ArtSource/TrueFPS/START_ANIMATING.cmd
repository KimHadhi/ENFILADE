@echo off
setlocal
set "TRUEFPS_BLENDER=F:\SteamLibrary\steamapps\common\Blender\blender.exe"
if defined BLENDER_EXE set "TRUEFPS_BLENDER=%BLENDER_EXE%"
if not exist "%TRUEFPS_BLENDER%" (
  echo Blender was not found. Set BLENDER_EXE to your blender.exe path.
  echo You can also open ENFILADE_TrueFPS_FullRig.blend and run its embedded True FPS tools text.
  pause
  exit /b 1
)
start "" "%TRUEFPS_BLENDER%" --disable-autoexec "%~dp0ENFILADE_TrueFPS_FullRig.blend" --python "%~dp0..\..\Tools\Blender\TrueFPS\truefps_tools.py"
endlocal
