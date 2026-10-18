@echo off
rem Compila o SandLite.exe com o compilador C# que ja vem no Windows.
cd /d "%~dp0"
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /codepage:65001 /target:winexe /optimize+ /out:SandLite.exe SandLite.cs
if errorlevel 1 pause
