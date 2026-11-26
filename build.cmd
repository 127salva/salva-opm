@echo off
rem Developer build with Windows' own C# compiler (nothing downloaded). Usage: build.cmd [selftest|app]
rem   (no argument) = selftest then app. The one file for the user is dist\SalvaOPM-Setup.exe.
setlocal
set CSC=%SystemRoot%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
set REFS=/noconfig /r:System.dll /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Management.dll
set OPTS=/nologo /optimize+ /warn:4 /warnaserror+
rem the embedded fonts, Plus Jakarta Sans and Oswald (third_party\fonts, SIL OFL 1.1); the self-checks load them too
set FONTS=/resource:third_party\fonts\PlusJakartaSans-SemiBold.ttf,SalvaOPM.PlusJakartaSans-SemiBold.ttf /resource:third_party\fonts\PlusJakartaSans-Bold.ttf,SalvaOPM.PlusJakartaSans-Bold.ttf /resource:third_party\fonts\PlusJakartaSans-ExtraBold.ttf,SalvaOPM.PlusJakartaSans-ExtraBold.ttf /resource:third_party\fonts\Oswald-SemiBold.ttf,SalvaOPM.Oswald-SemiBold.ttf
rem the window's icons (res\icons: white with transparency, tinted when drawn)
set ICONS=/resource:res\icons\leaf.png,SalvaOPM.icon-leaf.png /resource:res\icons\scale.png,SalvaOPM.icon-scale.png /resource:res\icons\bolt.png,SalvaOPM.icon-bolt.png /resource:res\icons\thermo.png,SalvaOPM.icon-thermo.png /resource:res\icons\fan.png,SalvaOPM.icon-fan.png /resource:res\icons\cpu.png,SalvaOPM.icon-cpu.png /resource:res\icons\gpu.png,SalvaOPM.icon-gpu.png
cd /d "%~dp0"
if not exist out mkdir out
if not exist dist mkdir dist
if /i "%1"=="app" goto app

echo == self-check (pure logic + simulator scenarios, no hardware) ==
"%CSC%" %OPTS% %REFS% /target:exe /platform:x64 /main:SalvaOPM.SelfCheckMain %FONTS% %ICONS% /out:out\selftest.exe src\*.cs || exit /b 1
out\selftest.exe || exit /b 1
if /i "%1"=="selftest" exit /b 0

:app
echo == SalvaOPM-Setup.exe ==
"%CSC%" %OPTS% %REFS% /target:winexe /platform:x64 /main:SalvaOPM.Program /win32icon:res\app.ico /win32manifest:res\app.manifest ^
  /resource:third_party\AMDFamily17.bin,SalvaOPM.AMDFamily17.bin %FONTS% %ICONS% ^
  /out:dist\SalvaOPM-Setup.exe src\*.cs || exit /b 1
echo built dist\SalvaOPM-Setup.exe
exit /b 0
