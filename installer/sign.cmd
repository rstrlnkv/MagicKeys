@echo off
chcp 65001>nul
rem Подписать переданные файлы. Отпечаток сертификата берётся из sign.thumbprint
rem рядом с этим скриптом или из переменной MAGICKEYS_SIGN_THUMBPRINT.
rem
rem Без сертификата подпись молча пропускается: сборка не должна ломаться у того,
rem у кого его нет, — а нет его у всех, кроме владельца ключа. Зато и обновление
rem такой пакет не поставит: Updater сверяет отпечаток и чужому не верит.
setlocal enabledelayedexpansion

set THUMB=
if exist "%~dp0sign.thumbprint" set /p THUMB=<"%~dp0sign.thumbprint"
if not defined THUMB set THUMB=%MAGICKEYS_SIGN_THUMBPRINT%
if not defined THUMB (
  echo Подпись пропущена: сертификат не задан.
  exit /b 0
)

rem Сертификата с этим отпечатком у большинства нет — репозиторий открытый, а ключ один.
rem Спрашиваем хранилище заранее: без этого signtool отказывал, отказ поднимался в
rem build-msi.cmd, и установщик не собирался ни у кого, кроме владельца ключа. Обещание
rem «сборка не должна ломаться у того, у кого сертификата нет» не выполнялось.
powershell -NoProfile -Command "exit [int](-not (Get-ChildItem Cert:\CurrentUser\My, Cert:\LocalMachine\My -ErrorAction SilentlyContinue | Where-Object { $_.Thumbprint -eq '%THUMB%' }))"
if errorlevel 1 (
  echo Подпись пропущена: сертификата %THUMB% нет в хранилище этой машины.
  exit /b 0
)

rem signtool приходит с Windows SDK. Искать сразу по маске пути нельзя — dir не
rem подставляет звёздочку в середине; ищем по имени во всём дереве и отбираем x64,
rem последний по сортировке и есть самый свежий выпуск SDK.
set SIGNTOOL=
for /f "delims=" %%I in ('dir /b /s /o:n "%ProgramFiles(x86)%\Windows Kits\10\bin\signtool.exe" 2^>nul ^| findstr /i "\x64\\"') do set SIGNTOOL=%%I
if not defined SIGNTOOL (
  echo Подпись пропущена: не найден signtool.exe из Windows SDK.
  exit /b 0
)

rem Метка времени обязательна: без неё подпись перестанет считаться действительной
rem в тот день, когда истечёт сертификат, — даже для давно выпущенных файлов.
"%SIGNTOOL%" sign /q /sha1 %THUMB% /fd SHA256 /td SHA256 ^
  /tr http://timestamp.digicert.com ^
  /d "MagicKeys — клавиатуры Apple в Windows" ^
  %* || exit /b 1

exit /b 0
