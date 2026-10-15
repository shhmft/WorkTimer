@echo off
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
"%CSC%" /nologo /codepage:65001 /target:winexe /optimize+ /win32icon:icon.ico /out:WorkTimer.exe ^
  /reference:System.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll ^
  WorkTimer.cs Updater.cs
if errorlevel 1 (echo BUILD FAILED & exit /b 1)
echo OK: WorkTimer.exe
