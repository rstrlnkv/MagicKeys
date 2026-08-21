@echo off
chcp 65001>nul
setlocal
rem Пересобирает MagicKeys.ico из Iconography.cs и лист образцов tools\preview.png.
rem Нужен только когда меняется рисунок значка; обычная сборка его не трогает.
set NETDIR=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
if not exist "%NETDIR%\csc.exe" set NETDIR=%WINDIR%\Microsoft.NET\Framework\v4.0.30319
if not exist "%NETDIR%\csc.exe" (
  echo Не найден компилятор C# из .NET Framework.
  exit /b 1
)

set TOOL=%TEMP%\magickeys-makeicon.exe
"%NETDIR%\csc.exe" /nologo /target:exe /codepage:65001 /out:"%TOOL%" ^
  /r:System.dll /r:System.Drawing.dll ^
  "%~dp0..\Iconography.cs" "%~dp0MakeIcon.cs"
if errorlevel 1 exit /b 1

"%TOOL%" "%~dp0.."
set RC=%errorlevel%

rem За собой убираем: собранный сборщик значка живёт одну команду.
del "%TOOL%" >nul 2>&1
exit /b %RC%
