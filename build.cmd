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

rem Номер сборки — число коммитов. Он растёт сам, не требует ведения вручную и
rem не разъезжается с версией: версия говорит «что это», номер — «какая по счёту».
rem Файл создаётся при каждой сборке и в репозиторий не попадает; без git номер 0.
set BUILD=0
for /f "delims=" %%I in ('git -C "%~dp0." rev-list --count HEAD 2^>nul') do set BUILD=%%I

> "%~dp0BuildInfo.g.cs" echo // Создаётся build.cmd при каждой сборке. Править незачем.
>> "%~dp0BuildInfo.g.cs" echo namespace MagicKeys { internal static partial class BuildInfo { public const string Number = "%BUILD%"; } }

"%NETDIR%\csc.exe" /nologo /target:winexe /optimize+ /platform:anycpu /codepage:65001 ^
  /out:"%~dp0MagicKeys.exe" /win32icon:"%~dp0MagicKeys.ico" ^
  /r:System.dll /r:System.Core.dll /r:System.Xml.dll /r:System.Web.Extensions.dll ^
  /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Management.dll ^
  /r:"%NETDIR%\System.Xaml.dll" ^
  /r:"%WPF%\WindowsBase.dll" /r:"%WPF%\PresentationCore.dll" /r:"%WPF%\PresentationFramework.dll" ^
  "%~dp0BuildInfo.cs" "%~dp0BuildInfo.g.cs" "%~dp0Updater.cs" "%~dp0Diag.cs" "%~dp0Iconography.cs" "%~dp0Native.cs" "%~dp0Models.cs" "%~dp0AppleDriver.cs" "%~dp0AppleDriverSetup.cs" "%~dp0KeyWatch.cs" "%~dp0KeyboardBattery.cs" "%~dp0AppleLayout.cs" "%~dp0Input.cs" "%~dp0MacKeys.cs" "%~dp0Actions.cs" "%~dp0Brightness.cs" ^
  "%~dp0Settings.cs" "%~dp0Devices.cs" "%~dp0Engine.cs" ^
  "%~dp0Theme.cs" "%~dp0Fluent.cs" "%~dp0Osd.cs" "%~dp0MainWindow.cs" "%~dp0App.cs"

if errorlevel 1 exit /b 1
echo Готово: %~dp0MagicKeys.exe
