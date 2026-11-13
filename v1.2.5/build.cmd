@echo off
setlocal
cd /d "%~dp0"
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo �Ҳ��� csc.exe����ȷ���Ѱ�װ .NET Framework 4.x
  pause
  exit /b 1
)
"%CSC%" /nologo /target:winexe /platform:x64 /win32icon:app.ico /out:AutoIME.exe AutoIME.cs
if errorlevel 1 (
  echo ����ʧ��
  pause
  exit /b 1
)
echo ����ɹ�: AutoIME.exe
pause