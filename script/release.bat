echo ================================
echo =       BUILD RELEASE          =
echo ================================

< NUL call config.bat

rmdir /S /Q %REPO_RELEASE_PATH%

mkdir %REPO_RELEASE_PATH%
mkdir %REPO_RELEASE_MOD_PATH%

copy %REPO_BUILD_PATH%%DLL_MOD_FILE_NAME% %REPO_RELEASE_MOD_PATH%
copy %REPO_BUILD_PATH%%PDB_MOD_FILE_NAME% %REPO_RELEASE_MOD_PATH%
xcopy /S /E /Y %REPO_PATH%ModFile %REPO_RELEASE_MOD_PATH%
