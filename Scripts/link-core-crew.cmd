@echo off
rem Связать приватное из Core Crew с рабочим деревом.
rem
rem Настоящие символьные ссылки на Windows требуют прав администратора или
rem режима разработчика, поэтому здесь их нет: папки связываются соединением
rem (mklink /J), файлы — жёсткой ссылкой (mklink /H). И то, и другое обычному
rem пользователю доступно, а работает так же: один файл, два имени.
rem
rem Жёсткая ссылка не пересекает тома: Core Crew обязан лежать на том же диске.
rem
rem Запускать после клонирования Core Crew рядом, а также всякий раз, когда
rem ссылка порвалась. Рвёт её всякий, кто заменяет файл целиком, а не пишет
rem в него: и редактор, и git pull в Core Crew.
rem
rem Прежде это же делал link-backstage.cmd — из .backstage, которого больше нет:
rem приватная половина переехала в Core Crew 05.09.2026.
rem
rem Ссылок пять. Шестой, .claude\agents, нет намеренно: своих ролей у проекта
rem не осталось, они приходят из ~\.claude\agents, а ссылка на пустой каталог
rem честнее выглядит отсутствующей.
setlocal
set ROOT=%~dp0..
set CC=%ROOT%\..\CoreCrew\projects\MagicKeys

if not exist "%CC%\" (
  echo Нет Core Crew рядом с этим деревом. Ожидается:
  echo   %CC%
  echo Поставить: git clone https://github.com/rstrlnkv/CoreCrew.git
  exit /b 1
)

echo === Документы ===
call :file "magickeys-private\CLAUDE.md"       "CLAUDE.md"
call :file "magickeys-private\ARCHITECTURE.md" "ARCHITECTURE.md"

echo === Команда ===
if not exist "%ROOT%\.claude"        mkdir "%ROOT%\.claude"
if not exist "%ROOT%\plugins"        mkdir "%ROOT%\plugins"
if not exist "%ROOT%\.claude-plugin" mkdir "%ROOT%\.claude-plugin"
rem Файлы знания. Без этой ссылки десять общих ролей читают «файла нет»,
rem и четыре из них при этом молча останавливаются.
call :dir  "magickeys-private\crew"            ".claude\crew"
call :dir  "plugins\magickeys-crew"            "plugins\magickeys-crew"
call :file ".claude-plugin\marketplace.json"   ".claude-plugin\marketplace.json"

echo.
echo Готово. Проверить: plugins\magickeys-crew\check-in-step.cmd
exit /b 0

rem ------------------------------------------------------- файл: жёсткая ссылка
:file
if not exist "%CC%\%~1" exit /b 0
if exist "%ROOT%\%~2" del /f /q "%ROOT%\%~2"
mklink /H "%ROOT%\%~2" "%CC%\%~1" >nul && echo   %~2
exit /b 0

rem ------------------------------------------------------- папка: соединение
:dir
if not exist "%CC%\%~1\" exit /b 0
rem rmdir снимает только соединение; настоящую папку с файлами он не тронет,
rem и это нарочно: молча стереть чужие файлы хуже, чем не поставить ссылку.
if exist "%ROOT%\%~2\" rmdir "%ROOT%\%~2" 2>nul
mklink /J "%ROOT%\%~2" "%CC%\%~1" >nul && echo   %~2\
exit /b 0
