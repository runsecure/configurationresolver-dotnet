@echo off
rem Runs Build-Signed.ps1 next to this file: build and test Release, sign
rem every assembly through Azure Artifact Signing, pack into
rem artifacts\packages, and verify the signatures inside every package.
rem Needs no elevation, but does need an Azure sign-in (az login) for an
rem account with the Artifact Signing Certificate Profile Signer role.
rem Arguments are forwarded, e.g.:
rem   build-signed.bat                      (version 0.9.0, from Directory.Build.props)
rem   build-signed.bat -Version 0.9.0-rc.1 -SkipTests

setlocal
set "SCRIPT_DIR=%~dp0"

powershell -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT_DIR%Build-Signed.ps1" %*
set "EXITCODE=%errorlevel%"

echo.
pause
exit /b %EXITCODE%
