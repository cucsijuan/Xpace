@echo off
rem Builds what Xpace needs from the pc86 and sw86 checkouts:
rem   native\bin\pc86.dll     the machine (CPU, BIOS, VGA, keyboard, timer, DMA, diskette)
rem   disks\cos.img           the COS system diskette
rem
rem Usage: native\build.bat [PC86_DIR] [SW86_DIR]
rem Defaults: ..\vm86\pc86 and ..\vm86\sw86 next to this project.

setlocal
set ROOT=%~dp0..
set PC86=%~1
if "%PC86%"=="" set PC86=%ROOT%\..\vm86\pc86
set SW86=%~2
if "%SW86%"=="" set SW86=%ROOT%\..\vm86\sw86
set BUILD=%ROOT%\native\build
set VS=C:\Program Files\Microsoft Visual Studio\2022\Professional

if not defined VCINSTALLDIR call "%VS%\VC\Auxiliary\Build\vcvars64.bat" >nul || goto fail
where cmake >nul 2>nul || set "PATH=%VS%\Common7\IDE\CommonExtensions\Microsoft\CMake\CMake\bin;%PATH%"

for %%C in (pc86 vm86 fdc86) do (
    cmake -S "%PC86%\%%C" -B "%BUILD%\%%C" -G "NMake Makefiles" -DCMAKE_BUILD_TYPE=Release >nul || goto fail
)
cmake --build "%BUILD%\pc86" --target pc86_shared || goto fail
cmake --build "%BUILD%\vm86" --target asm86 || goto fail
cmake --build "%BUILD%\fdc86" --target fdimage || goto fail

if not exist "%ROOT%\native\bin" mkdir "%ROOT%\native\bin"
copy /y "%BUILD%\pc86\pc86.dll" "%ROOT%\native\bin\" >nul || goto fail

rem The COS system diskette, as sw86's "make dos" makes it
set ASM=%BUILD%\vm86\asm86.exe
set FD=%BUILD%\fdc86\fdimage.exe
set OUT=%BUILD%\cos
if not exist "%OUT%" mkdir "%OUT%"
"%ASM%" "%SW86%\dos\boot.asm" -o "%OUT%\boot.bin" || goto fail
"%ASM%" "%SW86%\dos\cos.asm" -o "%OUT%\COS.SYS" || goto fail
"%ASM%" "%SW86%\dos\command.asm" -o "%OUT%\COMMAND.COM" || goto fail
"%ASM%" "%SW86%\dos\asm.asm" -o "%OUT%\ASM.COM" || goto fail
"%ASM%" "%SW86%\dos\edit.asm" -o "%OUT%\EDIT.COM" || goto fail
"%ASM%" "%SW86%\dos\basic.asm" -o "%OUT%\BASIC.COM" || goto fail

if not exist "%ROOT%\disks" mkdir "%ROOT%\disks"
set IMG=%ROOT%\disks\cos.img
if exist "%IMG%" del "%IMG%"
"%FD%" new "%IMG%" --label COS || goto fail
for %%F in (COS.SYS COMMAND.COM ASM.COM EDIT.COM BASIC.COM) do "%FD%" add "%IMG%" "%OUT%\%%F" || goto fail
"%FD%" add "%IMG%" "%SW86%\programs\hello\hello.asm" HELLO.ASM || goto fail
"%FD%" add "%IMG%" "%SW86%\dos\apes.bas" APES.BAS || goto fail
"%FD%" boot "%IMG%" "%OUT%\boot.bin" || goto fail

echo native\bin\pc86.dll and disks\cos.img are ready.
exit /b 0

:fail
echo build failed
exit /b 1
