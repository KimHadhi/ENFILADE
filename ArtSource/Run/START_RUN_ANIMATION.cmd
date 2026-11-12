@echo off
setlocal
set "RUN_BLENDER=F:\SteamLibrary\steamapps\common\Blender\blender.exe"
if defined BLENDER_EXE set "RUN_BLENDER=%BLENDER_EXE%"
if not exist "%RUN_BLENDER%" (
  echo Blender was not found. Set BLENDER_EXE to your blender.exe path.
  echo You can also open ENFILADE_Run_Animation.blend and run its "START HERE - Run tools.py" text with Alt+P.
  pause
  exit /b 1
)
if not exist "%~dp0ENFILADE_Run_Animation.blend" (
  echo Making the run workspace from the game's cycles. This takes a few minutes the first time.
  "%RUN_BLENDER%" --background --factory-startup "%~dp0..\TrueFPS\ENFILADE_TrueFPS_Start.blend" --python "%~dp0..\..\Tools\Blender\Run\build_run_workspace.py" -- "%~dp0ENFILADE_Run_Animation.blend"
)
start "" "%RUN_BLENDER%" --disable-autoexec "%~dp0ENFILADE_Run_Animation.blend" --python "%~dp0..\..\Tools\Blender\TrueFPS\truefps_tools.py" --python "%~dp0..\..\Tools\Blender\Run\run_tools.py"
endlocal
