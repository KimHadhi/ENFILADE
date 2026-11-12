@echo off
setlocal
rem Opens the paired executions workspace in Blender 4.5 with its tools (the True FPS tab and the Execution tab).
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
  pause
  exit /b 1
)
if not exist "%~dp0ENFILADE_Execution_Animation.blend" (
  echo Making the execution workspace from the drafts. This takes a few seconds the first time.
  "%ENFILADE_BLENDER%" --background --factory-startup "%~dp0..\TrueFPS\ENFILADE_TrueFPS_Start.blend" --python "%~dp0..\..\Tools\Blender\Executions\build_execution_workspace.py" -- "%~dp0ENFILADE_Execution_Animation.blend"
)
start "" "%ENFILADE_BLENDER%" --disable-autoexec "%~dp0ENFILADE_Execution_Animation.blend" --python "%~dp0..\..\Tools\Blender\TrueFPS\truefps_tools.py" --python "%~dp0..\..\Tools\Blender\Executions\execution_tools.py"
endlocal
