# Xpace native build, from the pc86 and sw86 submodules in external/:
#
#   bin/pc86 library            the machine (CPU, BIOS, VGA, keyboard, timer, DMA, diskette)
#   bin/xpace_native library    the ship's bus card and displays (xbus.c), the
#                               missiles' microcontroller (mcu.c)
#   ../disks/cos.img            the ship's diskette: COS, AUTOEXEC.BAT, the ship's software and
#                               Small-C, the C compiler (with CLIB.LIB and the headers)
#   ../disks/missile.bin        the radar guided missile's firmware
#   ../disks/irmissile.bin      the heat seeker's firmware
#   ../disks/engine.bin         the main engine's controller firmware
#
# Any generator works (Visual Studio, Make, Ninja...):
#
#   git submodule update --init
#   cmake -S native -B native/build
#   cmake --build native/build --config Release

cmake_minimum_required(VERSION 3.17)
project(xpace_native C)

set(XPACE_ROOT ${CMAKE_CURRENT_SOURCE_DIR}/..)
set(PC86_DIR ${XPACE_ROOT}/external/pc86 CACHE PATH "pc86 checkout")
set(SW86_DIR ${XPACE_ROOT}/external/sw86 CACHE PATH "sw86 checkout")
set(XPACE_BIN ${CMAKE_CURRENT_SOURCE_DIR}/bin)
set(XPACE_DISK ${XPACE_ROOT}/disks/cos.img)

if(NOT EXISTS ${PC86_DIR}/pc86/CMakeLists.txt OR NOT EXISTS ${SW86_DIR}/dos/cos.asm)
  message(FATAL_ERROR "pc86 or sw86 is missing: run 'git submodule update --init'")
endif()
if(NOT CMAKE_BUILD_TYPE AND NOT CMAKE_CONFIGURATION_TYPES)
  set(CMAKE_BUILD_TYPE Release)
endif()

# ---------- pc86: the machine and the tools ----------

add_subdirectory(${PC86_DIR}/pc86 pc86 EXCLUDE_FROM_ALL)    # pc86_shared
add_subdirectory(${PC86_DIR}/vm86 vm86 EXCLUDE_FROM_ALL)    # asm86
add_subdirectory(${PC86_DIR}/fdc86 fdc86 EXCLUDE_FROM_ALL)  # fdimage
if(UNIX)
  target_link_libraries(asm86 m)
  target_link_libraries(fdimage m)
endif()

# ---------- the ship's bus card and the microcontroller ----------

add_library(xpace_native SHARED xbus.c mcu.c ${PC86_DIR}/vga86/src/font_cp437.c)
set_target_properties(xpace_native PROPERTIES C_STANDARD 99)
set_target_properties(vm86 PROPERTIES POSITION_INDEPENDENT_CODE ON)
target_link_libraries(xpace_native PRIVATE vm86)                # its own copy of the CPU
foreach(component pc86 vm86 pit86 kbd86 vga86 dma86 fdc86)
  target_include_directories(xpace_native PRIVATE ${PC86_DIR}/${component}/include)
endforeach()
target_include_directories(xpace_native PRIVATE ${PC86_DIR}/vga86/src)
if(MSVC)
  target_compile_options(xpace_native PRIVATE /W4 /utf-8)
else()
  target_compile_options(xpace_native PRIVATE -Wall -Wextra)
endif()

# Both libraries straight into bin/, with no per-configuration folder
foreach(target pc86_shared xpace_native)
  set_target_properties(${target} PROPERTIES
    RUNTIME_OUTPUT_DIRECTORY $<1:${XPACE_BIN}>
    LIBRARY_OUTPUT_DIRECTORY $<1:${XPACE_BIN}>)
endforeach()

add_custom_target(libraries ALL)
add_dependencies(libraries pc86_shared xpace_native)

# ---------- the ship's diskette ----------

set(DISK_WORK ${CMAKE_CURRENT_BINARY_DIR}/disk)
set(DISK_PROGRAMS)

# out = asm86 src, into the diskette's work folder
function(assemble out src)
  add_custom_command(OUTPUT ${DISK_WORK}/${out}
    COMMAND ${CMAKE_COMMAND} -E make_directory ${DISK_WORK}
    COMMAND $<TARGET_FILE:asm86> ${src} -o ${DISK_WORK}/${out}
    DEPENDS asm86 ${src}
    VERBATIM)
  set(DISK_PROGRAMS ${DISK_PROGRAMS} ${DISK_WORK}/${out} PARENT_SCOPE)
endfunction()

assemble(boot.bin    ${SW86_DIR}/dos/boot.asm)
assemble(COS.SYS     ${SW86_DIR}/dos/cos.asm)
assemble(COMMAND.COM ${SW86_DIR}/dos/command.asm)
assemble(ASM.COM     ${SW86_DIR}/dos/asm.asm)
assemble(EDIT.COM    ${SW86_DIR}/dos/edit.asm)
assemble(BASIC.COM   ${SW86_DIR}/dos/basic.asm)
assemble(FLIGHT.COM  ${XPACE_ROOT}/software/flight/flight.asm)
assemble(FIRECTL.COM ${XPACE_ROOT}/software/firectl/firectl.asm)
assemble(RWR.COM     ${XPACE_ROOT}/software/rwr/rwr.asm)
assemble(POWER.COM   ${XPACE_ROOT}/software/power/power.asm)
assemble(COMMS.COM   ${XPACE_ROOT}/software/comms/comms.asm)

set(XPACE_FIRMWARE)
foreach(source missile/missile missile/irmissile engine/engine)
  get_filename_component(name ${source} NAME)
  set(out ${XPACE_ROOT}/disks/${name}.bin)
  add_custom_command(OUTPUT ${out}
    COMMAND ${CMAKE_COMMAND} -E make_directory ${XPACE_ROOT}/disks
    COMMAND $<TARGET_FILE:asm86> ${XPACE_ROOT}/software/${source}.asm -o ${out}
    DEPENDS asm86 ${XPACE_ROOT}/software/${source}.asm
    VERBATIM)
  list(APPEND XPACE_FIRMWARE ${out})
endforeach()
add_custom_target(firmware ALL DEPENDS ${XPACE_FIRMWARE})

set(FD $<TARGET_FILE:fdimage>)

# ---------- Small-C, the C compiler ----------
# Built for this computer first; that one makes the library (CLIB.LIB) and
# compiles itself into SMALLC.EXE for the diskette. (Left out with an
# sw86 older than Small-C.)

set(CC_DIR ${SW86_DIR}/smallc)
set(CC_DISK_COMMANDS)
set(CC_HEADERS)
if(EXISTS ${CC_DIR}/main.c)
  set(CC_SOURCES)
  foreach(f main lex decl expr gen stmt link)
    list(APPEND CC_SOURCES ${CC_DIR}/${f}.c)
  endforeach()
  file(GLOB CC_HEADERS ${CC_DIR}/include/*.h)
  file(GLOB CC_LIB_SOURCES ${CC_DIR}/lib/*.asm ${CC_DIR}/lib/*.c)
  list(SORT CC_LIB_SOURCES)
  add_executable(smallc ${CC_SOURCES})
  set_target_properties(smallc PROPERTIES C_STANDARD 99 RUNTIME_OUTPUT_DIRECTORY ${CMAKE_CURRENT_BINARY_DIR})
  if(MSVC)
    target_compile_definitions(smallc PRIVATE _CRT_SECURE_NO_WARNINGS)
  endif()
  add_custom_command(OUTPUT ${DISK_WORK}/CLIB.LIB
    COMMAND ${CMAKE_COMMAND} -E make_directory ${DISK_WORK}
    COMMAND $<TARGET_FILE:smallc> -I${CC_DIR}/include/ -c -o ${DISK_WORK}/CLIB.LIB ${CC_LIB_SOURCES}
    DEPENDS smallc ${CC_LIB_SOURCES} ${CC_HEADERS}
    VERBATIM)
  add_custom_command(OUTPUT ${DISK_WORK}/SMALLC.EXE
    COMMAND $<TARGET_FILE:smallc> -n -L${DISK_WORK}/CLIB.LIB -I${CC_DIR}/include/ -e -S -o ${DISK_WORK}/SMALLC.EXE ${CC_SOURCES}
    COMMAND $<TARGET_FILE:asm86> ${DISK_WORK}/SMALLC.ASM -o ${DISK_WORK}/SMALLC.EXE
    DEPENDS smallc asm86 ${DISK_WORK}/CLIB.LIB ${CC_SOURCES} ${CC_DIR}/smallc.h ${CC_HEADERS}
    VERBATIM)
  list(APPEND DISK_PROGRAMS ${DISK_WORK}/CLIB.LIB ${DISK_WORK}/SMALLC.EXE)
  list(APPEND CC_DISK_COMMANDS
    COMMAND ${FD} add ${XPACE_DISK} ${DISK_WORK}/SMALLC.EXE
    COMMAND ${FD} add ${XPACE_DISK} ${DISK_WORK}/CLIB.LIB
    COMMAND ${FD} add ${XPACE_DISK} ${SW86_DIR}/programs/hello/hello.c HELLO.C)
  foreach(h ${CC_HEADERS})
    get_filename_component(name ${h} NAME)
    string(TOUPPER ${name} name)
    list(APPEND CC_DISK_COMMANDS COMMAND ${FD} add ${XPACE_DISK} ${h} ${name})
  endforeach()
endif()

set(DISK_SOURCES
  ${SW86_DIR}/programs/hello/hello.asm ${SW86_DIR}/dos/apes.bas ${CC_HEADERS}
  ${XPACE_ROOT}/software/flight/flight.asm ${XPACE_ROOT}/software/firectl/firectl.asm
  ${XPACE_ROOT}/software/rwr/rwr.asm ${XPACE_ROOT}/software/power/power.asm
  ${XPACE_ROOT}/software/comms/comms.asm
  ${XPACE_ROOT}/software/missile/missile.asm ${XPACE_ROOT}/software/missile/irmissile.asm
  ${XPACE_ROOT}/software/autoexec.bat)
add_custom_command(OUTPUT ${XPACE_DISK}
  COMMAND ${CMAKE_COMMAND} -E make_directory ${XPACE_ROOT}/disks
  COMMAND ${CMAKE_COMMAND} -E rm -f ${XPACE_DISK}
  COMMAND ${FD} new ${XPACE_DISK} --label COS
  COMMAND ${FD} add ${XPACE_DISK} ${DISK_WORK}/COS.SYS
  COMMAND ${FD} add ${XPACE_DISK} ${DISK_WORK}/COMMAND.COM
  COMMAND ${FD} add ${XPACE_DISK} ${DISK_WORK}/ASM.COM
  COMMAND ${FD} add ${XPACE_DISK} ${DISK_WORK}/EDIT.COM
  COMMAND ${FD} add ${XPACE_DISK} ${DISK_WORK}/BASIC.COM
  COMMAND ${FD} add ${XPACE_DISK} ${SW86_DIR}/programs/hello/hello.asm HELLO.ASM
  COMMAND ${FD} add ${XPACE_DISK} ${SW86_DIR}/dos/apes.bas APES.BAS
  ${CC_DISK_COMMANDS}
  COMMAND ${FD} add ${XPACE_DISK} ${DISK_WORK}/FLIGHT.COM
  COMMAND ${FD} add ${XPACE_DISK} ${XPACE_ROOT}/software/flight/flight.asm FLIGHT.ASM
  COMMAND ${FD} add ${XPACE_DISK} ${DISK_WORK}/FIRECTL.COM
  COMMAND ${FD} add ${XPACE_DISK} ${XPACE_ROOT}/software/firectl/firectl.asm FIRECTL.ASM
  COMMAND ${FD} add ${XPACE_DISK} ${DISK_WORK}/RWR.COM
  COMMAND ${FD} add ${XPACE_DISK} ${XPACE_ROOT}/software/rwr/rwr.asm RWR.ASM
  COMMAND ${FD} add ${XPACE_DISK} ${DISK_WORK}/POWER.COM
  COMMAND ${FD} add ${XPACE_DISK} ${XPACE_ROOT}/software/power/power.asm POWER.ASM
  COMMAND ${FD} add ${XPACE_DISK} ${DISK_WORK}/COMMS.COM
  COMMAND ${FD} add ${XPACE_DISK} ${XPACE_ROOT}/software/comms/comms.asm COMMS.ASM
  COMMAND ${FD} add ${XPACE_DISK} ${XPACE_ROOT}/software/missile/missile.asm MISSILE.ASM
  COMMAND ${FD} add ${XPACE_DISK} ${XPACE_ROOT}/software/missile/irmissile.asm IRMISSIL.ASM
  COMMAND ${FD} add ${XPACE_DISK} ${XPACE_ROOT}/software/autoexec.bat AUTOEXEC.BAT
  COMMAND ${FD} boot ${XPACE_DISK} ${DISK_WORK}/boot.bin
  DEPENDS fdimage ${DISK_PROGRAMS} ${DISK_SOURCES}
  VERBATIM)
add_custom_target(disk ALL DEPENDS ${XPACE_DISK})
