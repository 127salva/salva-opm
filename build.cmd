@echo off
rem Developer build with Windows' own C# compiler (nothing downloaded). Usage: build.cmd [selftest|app]
rem   (no argument) = selftest then app. The one file for the user is dist\SalvaOPM-Setup.exe.
setlocal
set CSC=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set REFS=/noconfig /r:System.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Management.dll
set OPTS=/nologo /optimize+ /warn:4 /warnaserror+
cd /d "%~dp0"
if not exist out mkdir out
if not exist dist mkdir dist
if /i "%1"=="app" goto app

echo == self-check (pure logic + simulator scenarios, no hardware) ==
"%CSC%" %OPTS% %REFS% /target:exe /platform:x64 /main:SalvaOPM.SelfCheckMain /out:out\selftest.exe src\*.cs || exit /b 1
out\selftest.exe || exit /b 1
if /i "%1"=="selftest" exit /b 0

:app
echo == SalvaOPM-Setup.exe ==
"%CSC%" %OPTS% %REFS% /target:winexe /platform:x64 /main:SalvaOPM.Program /win32icon:res\app.ico /win32manifest:res\app.manifest ^
  /resource:third_party\AMDFamily17.bin,SalvaOPM.AMDFamily17.bin ^
  /out:dist\SalvaOPM-Setup.exe src\*.cs || exit /b 1
echo built dist\SalvaOPM-Setup.exe
exit /b 0
