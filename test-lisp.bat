@echo off
rem Compila ed esegue i test del Lisp (tests\LispTests.cs) insieme ai sorgenti dell'app.
rem Il test non entra in CorbolPrompt.exe. Lanciarlo dopo ogni modifica al Lisp e prima di ogni versione.
setlocal
set CSC=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe
if not exist "%CSC%" set CSC=%WINDIR%\Microsoft.NET\Framework\v4.0.30319\csc.exe
if not exist "%CSC%" (
  echo Compilatore csc.exe non trovato.
  exit /b 1
)
cd /d "%~dp0"
"%CSC%" /nologo /codepage:65001 /target:exe /out:tests\LispTests.exe /main:CorbolPrompt.LispTests /nowarn:0162,0168,0169,0219,0414,0649 ^
  /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll /r:System.Numerics.dll ^
  /r:System.Web.Extensions.dll /r:System.Security.dll /r:System.IO.Compression.dll /r:System.IO.Compression.FileSystem.dll ^
  CorbolPrompt.cs Tabs.cs Pty.cs Terminal.cs TermView.cs Lisp.cs LispLib.cs LispData.cs LispAi.cs LispWin.cs LispRepl.cs Assistant.cs Favorites.cs Welcome.cs Ai.cs AiLocal.cs Lang.cs Updater.cs Version.cs ^
  tests\LispTests.cs
if errorlevel 1 (
  echo Compilazione dei test fallita.
  exit /b 1
)
tests\LispTests.exe
