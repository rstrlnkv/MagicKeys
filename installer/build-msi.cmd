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

rem Версию берём из самой программы, а не пишем второй раз. Иначе пакет 1.2.0 мог
rem нести программу 0.1.0: обновление это переживает, потому что старая версия
rem удаляется до установки новой, — но стоит удалению споткнуться, и Windows увидит
rem одинаковые версии файла, не заменит его и отчитается об успехе.
for /f "delims=" %%V in ('powershell -NoProfile -Command "[regex]::Match((Get-Content -Raw '%ROOT%\BuildInfo.cs'), 'const string Version = ' + [char]34 + '([^' + [char]34 + ']+)').Groups[1].Value"') do set VERSION=%%V
if not defined VERSION (
  echo Не удалось прочитать версию из BuildInfo.cs.
  exit /b 1
)
if not "%~1"=="" if not "%~1"=="%VERSION%" (
  echo Просят собрать %~1, а в программе версия %VERSION% — поправьте BuildInfo.cs.
  exit /b 1
)

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

rem Скачанным отсюда собирается всё, что потом подписывается настоящим ключом.
rem TLS защищает дорогу, но не отвечает за то, что лежит на другом её конце,
rem — а проверить подпись Microsoft стоит одной строки.
powershell -NoProfile -Command "$s = Get-AuthenticodeSignature '%NUGET%'; exit [int](($s.Status -ne 'Valid') -or ($s.SignerCertificate.Subject -notlike '*Microsoft*'))"
if errorlevel 1 (
  echo nuget.exe подписан не Microsoft — сборка остановлена.
  exit /b 1
)

set WIX=%HERE%wix\wix\tools
if not exist "%WIX%\light.exe" (
  rem Проверяем light.exe, а не candle.exe: оборвавшаяся установка оставляла дерево,
  rem в котором первый файл уже есть, а последнего нет, — и оно больше никогда
  rem не доустанавливалось и не проверялось.
  echo === Набор WiX ===
  "%NUGET%" install WiX -Version 3.14.1 -OutputDirectory "%HERE%wix" -NonInteractive -ExcludeVersion || exit /b 1
)

rem Пакет собирает как раз WiX, а не nuget: тот его только приносит. Проверка,
rem стоявшая на курьере, ничего не говорила об инструменте.
rem Замерено: сами candle.exe, light.exe и heat.exe подписи Authenticode не имеют
rem вовсе — проверять её у них нечего. Подписан пакет целиком, подписью хранилища
rem nuget.org, и вот её проверить можно: без этого весь набор, которым собирается
rem подписываемый нашим ключом пакет, приходит на одном доверии к TLS.
"%NUGET%" verify -Signatures "%HERE%wix\wix\wix.nupkg" >nul || (
  echo Подпись набора WiX не проверилась — сборка остановлена.
  exit /b 1
)

rem Подпись пакета не ловит подмену того, что из него распаковано: пакет лежит
rem нетронутым, а файлы правит кто угодно с правами того же пользователя. Поэтому
rem у них закреплены суммы — как отпечаток у сертификата обновления.
if not exist "%HERE%wix.sha256" (
  echo Нет файла с закреплёнными суммами WiX — сборка остановлена.
  exit /b 1
)
powershell -NoProfile -Command "$l = @(Get-Content '%HERE%wix.sha256' | Where-Object { $_.Trim() }); if ($l.Count -lt 60) { exit 1 }; $seen=@{}; foreach ($x in $l) { $n,$h = $x -split ' '; $f = Join-Path '%WIX%' $n; if (-not (Test-Path $f)) { exit 1 }; if ((Get-FileHash $f -Algorithm SHA256).Hash -ne $h) { exit 1 }; $seen[$n]=1 }; foreach ($f in Get-ChildItem '%WIX%' -File -Force) { if (-not $seen.ContainsKey($f.Name)) { exit 1 } }; exit 0"
if errorlevel 1 (
  echo Файлы WiX не совпали с закреплёнными суммами — сборка остановлена.
  exit /b 1
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
"%WIX%\heat.exe" dir "%ROOT%\layouts" -nologo -cg LayoutFiles -dr LayoutsFolder ^
  -var var.SourceDir -ag -g1 -srd -sfrag -sreg -out "%HERE%obj\layouts.wxs" || exit /b 1
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

rem Подпись — не украшение: обновление сверяет отпечаток и неподписанный пакет
rem не поставит. Такой годится только для проверки на своей машине, и сказать
rem об этом надо здесь, а не оставлять человека с бодрым «Готово».
echo.
rem Корень у сертификата самодельный, поэтому «доверенной» подпись здесь не будет
rem никогда — и не должна: Updater сверяет отпечаток, а не цепочку. Проверяем то,
rem что важно: подпись есть и совпадает с содержимым файла.
rem Сверяем и отпечаток: подписать другим ключом можно нарочно (sign.cmd умеет брать
rem его из переменной), но обновление верит ровно одному, и такой пакет оно отвергнет
rem словами «подписан чужим ключом». Лучше узнать это здесь, чем от человека.
powershell -NoProfile -Command "$want=(Get-Content '%HERE%sign.thumbprint').Trim(); $s = Get-AuthenticodeSignature '%ROOT%\MagicKeys-%VERSION%-x64.msi'; exit [int](($s.SignerCertificate -eq $null) -or ($s.Status -eq 'HashMismatch') -or ($s.SignerCertificate.Thumbprint -ne $want))"
if errorlevel 1 (
  echo Готово, НО ПАКЕТ НЕ ПОДПИСАН: %ROOT%\MagicKeys-%VERSION%-x64.msi
  echo Выкладывать такой нельзя — обновление его отвергнет.
  rem И код возврата — тоже отказ: человеку сказали правду, а машина считала
  rem сборку удачной.
  exit /b 1
) else (
  echo Готово: %ROOT%\MagicKeys-%VERSION%-x64.msi
)
