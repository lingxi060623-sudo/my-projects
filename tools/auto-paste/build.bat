@echo off
chcp 65001 >nul
rem Builds the exe with the C# compiler that ships with Windows (.NET Framework 4.x); nothing to install.
setlocal
cd /d "%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"

if not exist "src\app.ico" (
  "%CSC%" /nologo /codepage:65001 /out:"%TEMP%\AutoPasteMakeIcon.exe" /r:System.Drawing.dll tools\MakeIcon.cs src\Drawing.cs || goto fail
  "%TEMP%\AutoPasteMakeIcon.exe" src\app.ico || goto fail
)

"%CSC%" /nologo /codepage:65001 /target:winexe /optimize+ /out:"自动粘贴.exe" /win32icon:src\app.ico /win32manifest:src\app.manifest /resource:src\app.ico,AutoPaste.app.ico /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll src\*.cs || goto fail
echo 编译完成：自动粘贴.exe
goto done

:fail
echo 编译失败
:done
echo %cmdcmdline% | find /i "/c" >nul && pause
