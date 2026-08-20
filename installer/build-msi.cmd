@echo off
rem Сборка установщика MagicKeys.msi. Сеть нужна только при первом запуске:
rem набор WiX подтягивается пакетом, ставить в систему ничего не надо.
rem
rem   build-msi.cmd            версия 1.0.0
rem   build-msi.cmd 1.2.0      заданная версия
rem
rem Каналов у пакета нет: Stable и Dev у MagicKeys — это выбор человека в самой
rem программе, а не разные сборки. Ранним выпуск делает пометка pre-release
rem на GitHub, и пакет для обоих каналов один и тот же.
setlocal
chcp 65001>nul
set HERE=%~dp0
set ROOT=%HERE%..

set VERSION=%1
if not defined VERSION set VERSION=%MAGICKEYS_VERSION%
if not defined VERSION set VERSION=1.0.0

if not exist "%ROOT%\MagicKeys.exe" (
  echo Нет MagicKeys.exe — сначала соберите программу: build.cmd
  exit /b 1
)

rem Эти три собирать нечем — они просто лежат в репозитории. Совет «соберите
rem программу» отправил бы делать то, что не помогает.
for %%F in ("%ROOT%\MagicKeys.ico" "%ROOT%\LICENSE" "%ROOT%\README.md") do (
  if not exist %%F (
    echo Нет файла %%F — он должен лежать в корне репозитория.
    exit /b 1
  )
)
if not exist "%ROOT%\layouts" (
  echo Нет папки layouts — без неё раскладки Apple работать не будут.
  exit /b 1
)

set NUGET=%HERE%obj\nuget.exe
if not exist "%HERE%obj" mkdir "%HERE%obj"
if not exist "%NUGET%" (
  echo === nuget.exe ===
  powershell -NoProfile -Command "Invoke-WebRequest https://dist.nuget.org/win-x86-commandline/latest/nuget.exe -OutFile '%NUGET%' -UseBasicParsing" || exit /b 1
)

set WIX=%HERE%wix\wix\tools
if not exist "%WIX%\candle.exe" (
  echo === Набор WiX ===
  "%NUGET%" install WiX -Version 3.14.1 -OutputDirectory "%HERE%wix" -NonInteractive -ExcludeVersion || exit /b 1
)

rem Программу подписываем в копии, а не на месте: она может быть запущена,
rem и signtool упрётся в занятый файл. Заодно сборка не правит рабочий каталог.
echo === Подпись программы ===
if not exist "%HERE%obj\stage" mkdir "%HERE%obj\stage"
copy /y "%ROOT%\MagicKeys.exe" "%HERE%obj\stage\" >nul || exit /b 1
call "%HERE%sign.cmd" "%HERE%obj\stage\MagicKeys.exe" || exit /b 1

rem Раскладок тридцать три, и перечислять их в .wxs руками значило бы забыть
rem новую при первом же добавлении. Список собирает heat.exe из самой папки.
echo === Список раскладок ===
"%WIX%\heat.exe" dir "%ROOT%\layouts" -nologo -cg LayoutFiles -dr INSTALLFOLDER ^
  -var var.SourceDir -gg -g1 -srd -sfrag -sreg -out "%HERE%obj\layouts.wxs" || exit /b 1
rem heat пишет пути от корня дерева, а нам нужна подпапка layouts.
powershell -NoProfile -Command "(Get-Content -Raw -Encoding UTF8 '%HERE%obj\layouts.wxs').Replace('$(var.SourceDir)\', '$(var.SourceDir)\layouts\') | Set-Content -NoNewline -Encoding UTF8 '%HERE%obj\layouts.wxs'" || exit /b 1

echo === Лицензия ===
powershell -NoProfile -ExecutionPolicy Bypass -File "%HERE%make-license-rtf.ps1" -Source "%ROOT%\LICENSE" -Target "%HERE%obj\LICENSE.rtf" || exit /b 1

echo === Сборка пакета ===
"%WIX%\candle.exe" -nologo -arch x64 ^
  -dVersion=%VERSION% -dSourceDir="%ROOT%" -dBinDir="%HERE%obj\stage" -dLicenseRtf="%HERE%obj\LICENSE.rtf" ^
  -ext WixUtilExtension -ext WixUIExtension ^
  -out "%HERE%obj\\" "%HERE%MagicKeys.wxs" "%HERE%obj\layouts.wxs" || exit /b 1

rem Отключённые проверки:
rem   ICE60 — про шрифты в файлах без версии; к раскладкам не относится;
rem   ICE38, ICE43, ICE57 — считают меню «Пуск» пользовательским и требуют ключа
rem     в HKCU. Пакет ставится на всю машину, меню там общее, и ключ в HKLM
rem     для него верный — статические проверки этого не учитывают.
"%WIX%\light.exe" -nologo ^
  -ext WixUtilExtension -ext WixUIExtension ^
  -cultures:ru-RU ^
  -sice:ICE60 -sice:ICE38 -sice:ICE43 -sice:ICE57 ^
  -out "%ROOT%\MagicKeys-%VERSION%-x64.msi" "%HERE%obj\MagicKeys.wixobj" "%HERE%obj\layouts.wixobj" || exit /b 1

echo === Подпись пакета ===
call "%HERE%sign.cmd" "%ROOT%\MagicKeys-%VERSION%-x64.msi" || exit /b 1

echo.
echo Готово: %ROOT%\MagicKeys-%VERSION%-x64.msi
