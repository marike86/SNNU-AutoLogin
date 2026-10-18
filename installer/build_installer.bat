@echo off
chcp 65001 >nul
setlocal

rem ===========================================================================
rem  一键构建安装包
rem  前置：已执行 dotnet publish 生成 ..\release 目录
rem ===========================================================================

cd /d "%~dp0"

set "ISCC="
if exist "%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe" set "ISCC=%LOCALAPPDATA%\Programs\Inno Setup 6\ISCC.exe"
if not defined ISCC if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if not defined ISCC if exist "%ProgramFiles%\Inno Setup 6\ISCC.exe" set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"

if not defined ISCC (
  echo [错误] 未找到 Inno Setup 6 的编译器 ISCC.exe
  echo        请先安装：winget install JRSoftware.InnoSetup
  exit /b 1
)

if not exist "..\release\SNNUAutoLogin.exe" (
  echo [错误] 缺少 ..\release\SNNUAutoLogin.exe
  echo        请先运行：dotnet publish -c Release -r win-x64 -p:Platform=x64 --self-contained -o release
  exit /b 1
)

echo 使用编译器：%ISCC%
echo 正在打包，LZMA2 最高压缩约需 1~3 分钟……
"%ISCC%" SNNUAutoLogin.iss
if errorlevel 1 (
  echo [失败] 打包未完成
  exit /b 1
)

echo.
echo 完成，产物位于：%~dp0Output
dir /b Output\*.exe
endlocal
