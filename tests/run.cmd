@echo off
chcp 65001>nul
setlocal
set NETDIR=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
if not exist "%NETDIR%\csc.exe" set NETDIR=%WINDIR%\Microsoft.NET\Framework\v4.0.30319
if not exist "%NETDIR%\csc.exe" (
  echo Не найден компилятор C# из .NET Framework.
  exit /b 1
)
set WPF=%NETDIR%\WPF
set SRC=%~dp0..
set REFS=/r:System.dll /r:System.Core.dll /r:System.Xml.dll /r:System.Web.Extensions.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Management.dll /r:"%NETDIR%\System.Xaml.dll" /r:"%WPF%\WindowsBase.dll" /r:"%WPF%\PresentationCore.dll" /r:"%WPF%\PresentationFramework.dll"
set CODE="%SRC%\BuildInfo.cs" "%SRC%\BuildInfo.g.cs" "%SRC%\Updater.cs" "%SRC%\Diag.cs" "%SRC%\Iconography.cs" "%SRC%\Native.cs" "%SRC%\Models.cs" "%SRC%\AppleDriver.cs" "%SRC%\AppleDriverSetup.cs" "%SRC%\KeyWatch.cs" "%SRC%\KeyboardBattery.cs" "%SRC%\AppleLayout.cs" "%SRC%\Input.cs" "%SRC%\MacKeys.cs" "%SRC%\Actions.cs" "%SRC%\Brightness.cs" "%SRC%\Settings.cs" "%SRC%\Devices.cs" "%SRC%\Engine.cs" "%SRC%\Theme.cs" "%SRC%\Fluent.cs" "%SRC%\Osd.cs" "%SRC%\MainWindow.cs" "%SRC%\App.cs"

rem Стенды собираются в папку программы: раскладки она ищет рядом с собой.
if not exist "%SRC%\BuildInfo.g.cs" call "%SRC%\build.cmd" >nul

"%NETDIR%\csc.exe" /nologo /target:exe /platform:anycpu /codepage:65001 /main:MagicKeys.SettingsTests ^
  /out:"%SRC%\MagicKeys.Tests.exe" %REFS% %CODE% "%~dp0SettingsTests.cs"
if errorlevel 1 exit /b 1

"%NETDIR%\csc.exe" /nologo /target:exe /platform:anycpu /codepage:65001 /main:MagicKeys.WindowTests ^
  /out:"%SRC%\MagicKeys.WindowTests.exe" %REFS% %CODE% "%~dp0WindowTests.cs"
if errorlevel 1 exit /b 1

set RC=0
echo === настройки ===
"%SRC%\MagicKeys.Tests.exe"
if errorlevel 1 set RC=1
echo.
echo === окно ===
"%SRC%\MagicKeys.WindowTests.exe"
if errorlevel 1 set RC=1

del "%SRC%\MagicKeys.Tests.exe" >nul 2>&1
del "%SRC%\MagicKeys.WindowTests.exe" >nul 2>&1
exit /b %RC%
