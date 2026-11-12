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
for %%R in ("%~dp0..") do set ROOT=%%~fR
set SRC=%ROOT%\src

rem Номер сборки — число коммитов. Он растёт сам, не требует ведения вручную и
rem не разъезжается с версией: версия говорит «что это», номер — «какая по счёту».
rem Файл создаётся при каждой сборке и в репозиторий не попадает; без git номер 0.
set BUILD=0
for /f "delims=" %%I in ('git -C "%ROOT%" rev-list --count HEAD 2^>nul') do set BUILD=%%I

> "%SRC%\BuildInfo.g.cs" echo // Создаётся build.cmd при каждой сборке. Править незачем.
>> "%SRC%\BuildInfo.g.cs" echo namespace MagicKeys { internal static partial class BuildInfo { public const string Number = "%BUILD%"; } }

"%NETDIR%\csc.exe" /nologo /target:winexe /optimize+ /platform:anycpu /codepage:65001 ^
  /out:"%ROOT%\MagicKeys.exe" /win32icon:"%ROOT%\MagicKeys.ico" ^
  /resource:"%ROOT%\CHANGELOG.ru.md",changelog ^
  /r:System.dll /r:System.Core.dll /r:System.Xml.dll /r:System.Web.Extensions.dll ^
  /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Management.dll ^
  /r:"%NETDIR%\System.Xaml.dll" ^
  /r:"%WPF%\WindowsBase.dll" /r:"%WPF%\PresentationCore.dll" /r:"%WPF%\PresentationFramework.dll" ^
  "%SRC%\BuildInfo.cs" "%SRC%\BuildInfo.g.cs" "%SRC%\Updater.cs" "%SRC%\Diag.cs" "%SRC%\Iconography.cs" "%SRC%\Native.cs" "%SRC%\Models.cs" "%SRC%\AppleDriver.cs" "%SRC%\AppleDriverSetup.cs" "%SRC%\KeyWatch.cs" "%SRC%\KeyboardBattery.cs" "%SRC%\AppleLayout.cs" "%SRC%\Input.cs" "%SRC%\MacKeys.cs" "%SRC%\Actions.cs" "%SRC%\Brightness.cs" ^
  "%SRC%\Settings.cs" "%SRC%\Devices.cs" "%SRC%\Elevated.cs" "%SRC%\KeyboardArt.cs" "%SRC%\KeyboardGlyphs.cs" "%SRC%\Engine.cs" ^
  "%SRC%\Changelog.cs" "%SRC%\ChangelogWindow.cs" ^
  "%SRC%\Theme.cs" "%SRC%\Fluent.cs" "%SRC%\Osd.cs" "%SRC%\MainWindow.cs" "%SRC%\App.cs"

if errorlevel 1 exit /b 1
echo Готово: %ROOT%\MagicKeys.exe
