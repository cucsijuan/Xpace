# Xpace native build, from the pc86 and sw86 submodules in external/:
#
#   bin/pc86 library            the machine (CPU, BIOS, VGA, keyboard, timer, DMA, diskette)
#   bin/xpace_native library    the ship's bus card and displays (xbus.c), the
#                               missiles' microcontroller (mcu.c)
#   ../disks/cos.img            the ship's diskette: COS, AUTOEXEC.BAT and the ship's software
#   ../disks/missile.bin        the missile's firmware
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

set(XPACE_FIRMWARE ${XPACE_ROOT}/disks/missile.bin)
add_custom_command(OUTPUT ${XPACE_FIRMWARE}
  COMMAND ${CMAKE_COMMAND} -E make_directory ${XPACE_ROOT}/disks
  COMMAND $<TARGET_FILE:asm86> ${XPACE_ROOT}/software/missile/missile.asm -o ${XPACE_FIRMWARE}
  DEPENDS asm86 ${XPACE_ROOT}/software/missile/missile.asm
  VERBATIM)
add_custom_target(firmware ALL DEPENDS ${XPACE_FIRMWARE})

set(FD $<TARGET_FILE:fdimage>)
set(DISK_SOURCES
  ${SW86_DIR}/programs/hello/hello.asm ${SW86_DIR}/dos/apes.bas
  ${XPACE_ROOT}/software/flight/flight.asm ${XPACE_ROOT}/software/firectl/firectl.asm
  ${XPACE_ROOT}/software/missile/missile.asm ${XPACE_ROOT}/software/autoexec.bat)
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
  COMMAND ${FD} add ${XPACE_DISK} ${DISK_WORK}/FLIGHT.COM
  COMMAND ${FD} add ${XPACE_DISK} ${XPACE_ROOT}/software/flight/flight.asm FLIGHT.ASM
  COMMAND ${FD} add ${XPACE_DISK} ${DISK_WORK}/FIRECTL.COM
  COMMAND ${FD} add ${XPACE_DISK} ${XPACE_ROOT}/software/firectl/firectl.asm FIRECTL.ASM
  COMMAND ${FD} add ${XPACE_DISK} ${XPACE_ROOT}/software/missile/missile.asm MISSILE.ASM
  COMMAND ${FD} add ${XPACE_DISK} ${XPACE_ROOT}/software/autoexec.bat AUTOEXEC.BAT
  COMMAND ${FD} boot ${XPACE_DISK} ${DISK_WORK}/boot.bin
  DEPENDS fdimage ${DISK_PROGRAMS} ${DISK_SOURCES}
  VERBATIM)
add_custom_target(disk ALL DEPENDS ${XPACE_DISK})
