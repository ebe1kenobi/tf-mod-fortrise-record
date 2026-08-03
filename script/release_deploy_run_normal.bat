@echo OFF
< NUL call config.bat

cd %REPO_SCRIPT_PATH%
%REPO_DRIVE%
< NUL call release.bat

cd %REPO_SCRIPT_PATH%
%REPO_DRIVE%
< NUL call deploy.bat

echo =================================
echo =       EXECUTE TOWERFALL       =
echo =================================

cd %TOWERFALL_PATH%
C:
%EXECUTABLE%

@REM pause
