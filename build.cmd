@echo off
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
"%CSC%" /nologo /target:winexe /optimize+ /win32icon:icon.ico /out:WorkTimer.exe ^
  /reference:System.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll ^
  WorkTimer.cs
if errorlevel 1 (echo BUILD FAILED & exit /b 1)
echo OK: WorkTimer.exe
