@echo off
rem Builds what Xpace needs from the pc86 and sw86 checkouts:
rem   native\bin\pc86.dll          the machine (CPU, BIOS, VGA, keyboard, timer, DMA, diskette)
rem   native\bin\xpace_native.dll  the ship's bus card (native\xbus.c)
rem   disks\cos.img                the ship's diskette: COS, AUTOEXEC.BAT and FLIGHT.COM
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

rem The ship's bus card
if not exist "%BUILD%\xbus" mkdir "%BUILD%\xbus"
set INC=
for %%C in (pc86 vm86 pit86 kbd86 vga86 dma86 fdc86) do call set INC=%%INC%% /I "%PC86%\%%C\include"
cl /nologo /O2 /W4 /LD "%ROOT%\native\xbus.c" %INC% ^
   /Fo"%BUILD%\xbus\xbus.obj" /Fe"%ROOT%\native\bin\xpace_native.dll" ^
   /link /implib:"%BUILD%\xbus\xpace_native.lib" >nul || goto fail

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
rem The ship's own software
"%ASM%" "%ROOT%\software\flight\flight.asm" -o "%OUT%\FLIGHT.COM" || goto fail

if not exist "%ROOT%\disks" mkdir "%ROOT%\disks"
set IMG=%ROOT%\disks\cos.img
if exist "%IMG%" del "%IMG%"
"%FD%" new "%IMG%" --label COS || goto fail
for %%F in (COS.SYS COMMAND.COM ASM.COM EDIT.COM BASIC.COM) do "%FD%" add "%IMG%" "%OUT%\%%F" || goto fail
"%FD%" add "%IMG%" "%SW86%\programs\hello\hello.asm" HELLO.ASM || goto fail
"%FD%" add "%IMG%" "%SW86%\dos\apes.bas" APES.BAS || goto fail
"%FD%" add "%IMG%" "%OUT%\FLIGHT.COM" || goto fail
"%FD%" add "%IMG%" "%ROOT%\software\flight\flight.asm" FLIGHT.ASM || goto fail
"%FD%" add "%IMG%" "%ROOT%\software\autoexec.bat" AUTOEXEC.BAT || goto fail
"%FD%" boot "%IMG%" "%OUT%\boot.bin" || goto fail

echo native\bin\pc86.dll, native\bin\xpace_native.dll and disks\cos.img are ready.
exit /b 0

:fail
echo build failed
exit /b 1
