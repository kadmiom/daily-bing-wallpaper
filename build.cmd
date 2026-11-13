@echo off
setlocal
set "ROOT=%~dp0"
set "CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if not exist "%CSC%" set "CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe"
if not exist "%CSC%" (
  echo Could not find the .NET Framework C# compiler.
  exit /b 1
)

if not exist "%ROOT%obj" mkdir "%ROOT%obj"
if not exist "%ROOT%dist" mkdir "%ROOT%dist"

set "BUILD_DATE="
for /f %%D in ('powershell -NoProfile -Command "Get-Date -Format yyyy-MM-dd"') do set "BUILD_DATE=%%D"
if "%BUILD_DATE%"=="" set "BUILD_DATE=unknown"

> "%ROOT%obj\BuildInfo.cs" (
  echo namespace DailyBingWallpaper
  echo {
  echo     internal static class BuildInfo
  echo     {
  echo         internal const string Date = "%BUILD_DATE%";
  echo     }
  echo }
)

"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 ^
  /out:"%ROOT%dist\DailyBingWallpaper.exe" ^
  /win32manifest:"%ROOT%src\app.manifest" ^
  /reference:System.dll ^
  /reference:System.Core.dll ^
  /reference:System.Drawing.dll ^
  /reference:System.Windows.Forms.dll ^
  /reference:System.Net.Http.dll ^
  "%ROOT%src\Wallpaper.cs" "%ROOT%src\Shortcuts.cs" ^
  "%ROOT%src\Program.cs" "%ROOT%obj\BuildInfo.cs"
if errorlevel 1 exit /b 1

"%CSC%" /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 ^
  /out:"%ROOT%dist\DailyBingWallpaper-Setup.exe" ^
  /win32manifest:"%ROOT%src\app.manifest" ^
  /resource:"%ROOT%dist\DailyBingWallpaper.exe",DailyBingWallpaper.exe ^
  /reference:System.dll ^
  /reference:System.Drawing.dll ^
  /reference:System.Windows.Forms.dll ^
  "%ROOT%setup\Setup.cs" "%ROOT%src\Shortcuts.cs" "%ROOT%obj\BuildInfo.cs"
if errorlevel 1 exit /b 1

echo Built: %ROOT%dist\DailyBingWallpaper.exe
echo Built: %ROOT%dist\DailyBingWallpaper-Setup.exe
