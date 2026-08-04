echo ================================
echo =       BUILD RELEASE          =
echo ================================

< NUL call config.bat

rmdir /S /Q %REPO_RELEASE_PATH%

mkdir %REPO_RELEASE_PATH%
mkdir %REPO_RELEASE_MOD_PATH%

copy %REPO_BUILD_PATH%%DLL_MOD_FILE_NAME% %REPO_RELEASE_MOD_PATH%
copy %REPO_BUILD_PATH%%PDB_MOD_FILE_NAME% %REPO_RELEASE_MOD_PATH%

rem Dependance de l'export GIF. FortRise resout les assemblies d'un mod dans son
rem propre dossier : sans cette copie, l'export echoue au premier appel.
copy %REPO_BUILD_PATH%SixLabors.ImageSharp.dll %REPO_RELEASE_MOD_PATH%
xcopy /S /E /Y %REPO_PATH%ModFile %REPO_RELEASE_MOD_PATH%
