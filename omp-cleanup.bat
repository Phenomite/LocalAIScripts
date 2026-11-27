@echo off
setlocal
set "AGENT=%USERPROFILE%\.omp\agent" 
if exist "%AGENT%\sessions" rd /s /q "%AGENT%\sessions"
if not exist "%AGENT%\sessions" mkdir "%AGENT%\sessions"
if exist "%AGENT%\blobs" rd /s /q "%AGENT%\blobs"
if not exist "%AGENT%\blobs" mkdir "%AGENT%\blobs"
if exist "%AGENT%\cache\document-conversions" rd /s /q "%AGENT%\cache\document-conversions"
if not exist "%AGENT%\cache\document-conversions" mkdir "%AGENT%\cache\document-conversions"
if exist "%AGENT%\cache\document-conversions" rd /s /q "%AGENT%\terminal-sessions"
if not exist "%AGENT%\cache\document-conversions" mkdir "%AGENT%\terminal-sessions"
endlocal
