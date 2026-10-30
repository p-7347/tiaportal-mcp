@echo off
rem Drag a TIA Portal HMI tag export (.xlsx) onto this file, or run: HmiTags.bat <file.xlsx> [--hmi HMI_1]
rem Shows the changes first and asks before writing. See "TiaMcpServer.exe hmitags --help".
chcp 65001 >nul
if "%~1"=="" (
  "%~dp0TiaMcpServer.exe" hmitags --help
) else (
  "%~dp0TiaMcpServer.exe" hmitags %* --apply
)
pause
