# Copies the Synty POLYGON Sci-Fi Space files the game uses from the pack's
# source files into assets/synty/. The pack's licence does not allow
# redistributing its files, so they stay out of the repository; without them
# the game falls back to placeholder geometry.
#
#   cmake -DPACK=<path to SourceFiles> -P tools/import_synty.cmake
#
# PACK defaults to .raw-assets/SourceFiles in the project.

get_filename_component(ROOT "${CMAKE_CURRENT_LIST_DIR}/.." ABSOLUTE)
if(NOT PACK)
  set(PACK "${ROOT}/.raw-assets/SourceFiles")
endif()
if(NOT EXISTS "${PACK}/FBX")
  message(FATAL_ERROR "No Synty source files at ${PACK} (pass -DPACK=...)")
endif()

set(DEST "${ROOT}/assets/synty")
set(MODELS
  SM_Veh_POV_Cockpit_01
  SM_Veh_POV_Cockpit_Frame_01
  SM_Veh_POV_Cockpit_Frame_02
  SM_Veh_POV_Cockpit_Frame_03
)
set(TEXTURES
  PolygonSciFiSpace_Texture_01_A
  PolygonSciFiSpace_Emissive_01
)

file(MAKE_DIRECTORY "${DEST}")
foreach(m IN LISTS MODELS)
  file(COPY "${PACK}/FBX/${m}.fbx" DESTINATION "${DEST}")
endforeach()
foreach(t IN LISTS TEXTURES)
  file(COPY "${PACK}/Textures/${t}.png" DESTINATION "${DEST}")
endforeach()
message(STATUS "Synty files copied to ${DEST}")
