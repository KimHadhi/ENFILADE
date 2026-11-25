@echo off
setlocal
set "PIVOT_BLENDER=F:\SteamLibrary\steamapps\common\Blender\blender.exe"
if defined BLENDER_EXE set "PIVOT_BLENDER=%BLENDER_EXE%"
if not exist "%PIVOT_BLENDER%" (
  echo Blender was not found. Set BLENDER_EXE to your blender.exe path.
  echo You can also open ENFILADE_Pivot_Animation.blend and run its "START HERE - Pivot tools.py" text with Alt+P.
  pause
  exit /b 1
)
if not exist "%~dp0ENFILADE_Pivot_Animation.blend" (
  echo Making the pivot workspace from the game's BIPER cycles. This takes a few minutes the first time.
  "%PIVOT_BLENDER%" --background --factory-startup "%~dp0..\TrueFPS\ENFILADE_TrueFPS_Start.blend" --python "%~dp0..\..\Tools\Blender\Pivots\build_pivot_workspace.py" -- "%~dp0ENFILADE_Pivot_Animation.blend"
)
start "" "%PIVOT_BLENDER%" --disable-autoexec "%~dp0ENFILADE_Pivot_Animation.blend" --python "%~dp0..\..\Tools\Blender\TrueFPS\truefps_tools.py" --python "%~dp0..\..\Tools\Blender\Pivots\pivot_tools.py"
endlocal
