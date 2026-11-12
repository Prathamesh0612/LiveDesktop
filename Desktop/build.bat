@echo off
if exist "C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\VC\Auxiliary\Build\vcvars64.bat" (
    if not exist TaskbarTap.dll (
        call "C:\Program Files (x86)\Microsoft Visual Studio\18\BuildTools\VC\Auxiliary\Build\vcvars64.bat" >nul
        cl /nologo /O2 /MT /LD /EHsc TaskbarTap.cpp /link /DEF:TaskbarTap.def /OUT:TaskbarTap.dll
    )
)
set FW=C:\Windows\Microsoft.NET\Framework64\v4.0.30319
%FW%\csc.exe /nologo /platform:x64 /target:winexe /win32manifest:app.manifest /win32icon:app.ico /out:LiveDesktop.exe /r:%FW%\WPF\PresentationCore.dll /r:%FW%\WPF\PresentationFramework.dll /r:%FW%\WPF\WindowsBase.dll /r:%FW%\WPF\WindowsFormsIntegration.dll /r:%FW%\System.Xaml.dll /r:%FW%\System.Windows.Forms.dll /r:%FW%\System.Drawing.dll /r:%FW%\Microsoft.CSharp.dll /r:%FW%\System.Core.dll /r:%FW%\System.IO.Compression.dll /r:%FW%\System.IO.Compression.FileSystem.dll /r:%FW%\System.Web.Extensions.dll Program.cs
if errorlevel 1 (echo BUILD FAILED) else echo OK: LiveDesktop.exe + TaskbarTap.dll
