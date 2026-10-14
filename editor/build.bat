@echo off
rem Builds FFXIISaveEditor.exe and the tests (tests\SelfTest.exe, tests\UiTest.exe) with the
rem C# compiler that ships with Windows (.NET Framework 4.x).
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo Could not find csc.exe. .NET Framework 4.x is required.
  exit /b 1
)
set D=%~dp0
set REFS=/r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll
"%CSC%" /nologo /target:winexe /optimize+ /out:"%D%FFXIISaveEditor.exe" %REFS% ^
  "%D%Codec.cs" "%D%Save.cs" "%D%Tables.cs" "%D%MainForm.cs"
if errorlevel 1 exit /b 1
"%CSC%" /nologo /target:exe /optimize+ /out:"%D%tests\SelfTest.exe" %REFS% ^
  "%D%Codec.cs" "%D%Save.cs" "%D%Tables.cs" "%D%tests\SelfTest.cs"
if errorlevel 1 exit /b 1
"%CSC%" /nologo /target:exe /optimize+ /out:"%D%tests\UiTest.exe" /main:FFXIISaveEditor.UiTest %REFS% ^
  "%D%Codec.cs" "%D%Save.cs" "%D%Tables.cs" "%D%MainForm.cs" "%D%tests\UiTest.cs"
if errorlevel 1 exit /b 1
echo Built %D%FFXIISaveEditor.exe
