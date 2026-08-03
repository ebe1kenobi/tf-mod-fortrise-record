@echo off

echo =================================
echo =       DEPLOY RELEASE          =
echo =================================

< NUL call config.bat

rmdir /S /Q %TOWERFALL_THIS_MODULE_PATH%

xcopy /E /S /Y %REPO_RELEASE_PATH%* %TOWERFALL_MODS_PATH%
