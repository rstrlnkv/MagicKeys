@echo off
rem Связать приватное из .backstage с рабочим деревом.
rem
rem Настоящие символьные ссылки на Windows требуют прав администратора или
rem режима разработчика, поэтому здесь их нет: папки связываются соединением
rem (mklink /J), файлы — жёсткой ссылкой (mklink /H). И то, и другое обычному
rem пользователю доступно, а работает так же: один файл, два имени.
rem
rem Запускать после клонирования, а также всякий раз, когда в .backstage
rem появилось что-то новое или ссылка порвалась: редактор, заменяющий файл
rem целиком вместо записи в него, жёсткую ссылку разрывает.
setlocal
set ROOT=%~dp0..
set BACK=%ROOT%\.backstage

if not exist "%BACK%" (
  echo Нет папки .backstage — приватная часть недоступна.
  echo Это нормально для тех, у кого нет к ней доступа: программа собирается и так.
  exit /b 0
)
dir /b "%BACK%" 2>nul | findstr . >nul
if errorlevel 1 (
  echo Папка .backstage пуста — приватный репозиторий не выкачан.
  exit /b 0
)

echo === Документы ===
call :file "magickeys-private\CLAUDE.md"       "CLAUDE.md"
call :file "magickeys-private\ARCHITECTURE.md" "ARCHITECTURE.md"
call :dir  "magickeys-private\docs"            "docs"

echo === Команда ===
if not exist "%ROOT%\.claude"        mkdir "%ROOT%\.claude"
if not exist "%ROOT%\plugins"        mkdir "%ROOT%\plugins"
if not exist "%ROOT%\.claude-plugin" mkdir "%ROOT%\.claude-plugin"
call :dir  "plugins\magickeys-crew\agents"     ".claude\agents"
call :dir  "plugins\magickeys-crew"            "plugins\magickeys-crew"
call :file ".claude-plugin\marketplace.json"   ".claude-plugin\marketplace.json"

echo.
echo Готово.
exit /b 0

rem ------------------------------------------------------- файл: жёсткая ссылка
:file
if not exist "%BACK%\%~1" exit /b 0
if exist "%ROOT%\%~2" erase /q "%ROOT%\%~2"
mklink /H "%ROOT%\%~2" "%BACK%\%~1" >nul && echo   %~2
exit /b 0

rem ------------------------------------------------------- папка: соединение
:dir
if not exist "%BACK%\%~1\" exit /b 0
rem rmdir снимает только соединение; настоящую папку с файлами он не тронет,
rem и это нарочно: молча стереть чужие файлы хуже, чем не поставить ссылку.
if exist "%ROOT%\%~2\" rmdir "%ROOT%\%~2" 2>nul
mklink /J "%ROOT%\%~2" "%BACK%\%~1" >nul && echo   %~2\
exit /b 0
