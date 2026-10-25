# Copies the Synty POLYGON Sci-Fi Space files the game uses from the pack's
# source files into assets/synty/. The pack's licence does not allow
# redistributing its files, so they stay out of the repository; without them
# the game falls back to placeholder geometry.
#
#   cmake -DPACK=<path to SourceFiles> -DGODOT=<path to Godot .NET> -P tools/import_synty.cmake
#
# PACK defaults to .raw-assets/SourceFiles in the project. With GODOT, it
# then has Godot import them and runs the mesh part tool on every list in
# tools/mesh_parts/lists (the cockpit's sliders and switches, as meshes of
# their own, into assets/synty/parts; see docs/tools/mesh-parts.md). The
# C# project must be built first (dotnet build).

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
  SM_Ship_Fighter_03
  FX_Meshes/SM_Flame_Mesh_New
  SM_Env_Astroid_01
  SM_Env_Astroid_02
  SM_Env_Astroid_03
  SM_Env_Astroid_04
  SM_Env_Astroid_05
  SM_Env_Astroid_Large_01
  SM_Env_Astroid_Large_02
  SM_Env_Astroid_Holes_03
  SM_Env_Astroid_Large_Holes_02
)
set(TEXTURES
  PolygonSciFiSpace_Texture_01_A
  PolygonSciFiSpace_Emissive_01
  FX_Textures/Gradient_Boost
  FX_Textures/PolygonSciFiSpace_Skybox_04_Front
  FX_Textures/PolygonSciFiSpace_Skybox_04_Back
  FX_Textures/PolygonSciFiSpace_Skybox_04_Left
  FX_Textures/PolygonSciFiSpace_Skybox_04_Right
  FX_Textures/PolygonSciFiSpace_Skybox_04_Up
  FX_Textures/PolygonSciFiSpace_Skybox_04_Down
)

file(MAKE_DIRECTORY "${DEST}")
foreach(m IN LISTS MODELS)
  file(COPY "${PACK}/FBX/${m}.fbx" DESTINATION "${DEST}")
endforeach()
foreach(t IN LISTS TEXTURES)
  file(COPY "${PACK}/Textures/${t}.png" DESTINATION "${DEST}")
endforeach()
message(STATUS "Synty files copied to ${DEST}")

if(GODOT)
  execute_process(COMMAND "${GODOT}" --headless --path "${ROOT}" --import
                  RESULT_VARIABLE imported)
  if(NOT imported EQUAL 0)
    message(FATAL_ERROR "Godot could not import the project (${imported})")
  endif()
  file(GLOB LISTS RELATIVE "${ROOT}" "${ROOT}/tools/mesh_parts/lists/*.tres")
  foreach(list IN LISTS LISTS)
    execute_process(COMMAND "${GODOT}" --headless --path "${ROOT}" res://tools/mesh_parts/mesh_part_tool.tscn
                            -- "res://${list}" extract
                    RESULT_VARIABLE extracted)
    if(NOT extracted EQUAL 0)
      message(FATAL_ERROR "The mesh part tool failed on ${list}")
    endif()
  endforeach()
  message(STATUS "Mesh parts extracted into ${DEST}/parts")
else()
  message(STATUS "Next: Godot import, then the mesh part tool (or run this again with -DGODOT=<godot>)")
endif()
