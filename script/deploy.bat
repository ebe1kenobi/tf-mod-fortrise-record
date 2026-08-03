@echo off

echo =================================
echo =       DEPLOY RELEASE          =
echo =================================

< NUL call config.bat

rem echo %TOWERFALL_THIS_MODULE_PATH%
rmdir /S /Q %TOWERFALL_THIS_MODULE_PATH%
rmdir /S /Q %TOWERFALL_THIS_MODULE_AUTO_PATH%

xcopy /E /S /Y %REPO_RELEASE_PATH%* %TOWERFALL_MODS_PATH%

@REM pause
