@echo off
rem Compila CorbolPrompt.exe con il compilatore C# incluso in Windows (.NET Framework 4.x)
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo Compilatore csc.exe non trovato.
  exit /b 1
)
cd /d "%~dp0"
rem Traduzioni: ogni lang\*.txt (tranne _template.txt) viene incorporato nell'exe
set LANGRES=
if exist lang for %%F in (lang\*.txt) do if /i not "%%~nF"=="_template" call set LANGRES=%%LANGRES%% /resource:"%%F",CorbolPrompt.lang.%%~nF.txt
rem Icona dell'exe (e quindi della finestra): la genera make-icon.ps1 se manca
set ICON=
if not exist corbolprompt.ico powershell -NoProfile -ExecutionPolicy Bypass -File make-icon.ps1
if exist corbolprompt.ico set ICON=/win32icon:corbolprompt.ico /resource:corbolprompt.ico,CorbolPrompt.corbolprompt.ico
"%CSC%" /nologo /codepage:65001 /target:winexe /optimize+ /out:CorbolPrompt.exe %ICON% ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll /r:System.Numerics.dll ^
  /r:System.Web.Extensions.dll /r:System.Security.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll ^
  CorbolPrompt.cs Tabs.cs Pty.cs Terminal.cs TermView.cs Lisp.cs LispLib.cs LispData.cs LispAi.cs LispWin.cs LispHelp.cs LispRepl.cs Assistant.cs Favorites.cs Welcome.cs Ai.cs AiLocal.cs Lang.cs Updater.cs Version.cs %LANGRES%
if errorlevel 1 (
  echo Compilazione fallita.
  exit /b 1
)
echo Creato CorbolPrompt.exe
