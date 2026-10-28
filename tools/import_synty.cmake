# Copies the Synty POLYGON Sci-Fi Space files the game uses from the pack's
# source files into assets/synty/. The pack's licence does not allow
# redistributing its files, so they stay out of the repository; without them
# the game falls back to placeholder geometry. Files whose import settings are
# not Godot's defaults get them from tools/synty_imports.
#
#   cmake -DPACK=<path to SourceFiles> -DGODOT=<path to Godot .NET> -DBLENDER=<blender> -P tools/import_synty.cmake
#
# PACK defaults to .raw-assets/SourceFiles in the project. Collision meshes
# are not among the source files: they come out of the Unity package
# (PACKAGE, by default the .unitypackage in .raw-assets; a tar.gz with each
# asset in a folder named by its GUID, its path in the folder's pathname). With GODOT, it
# then has Godot import them and runs the mesh part tool on every list in
# tools/mesh_parts/lists (the cockpit's sliders and switches, as meshes of
# their own, into assets/synty/parts; see docs/tools/mesh-parts.md). The
# C# project must be built first (dotnet build). With BLENDER, it then builds
# the big hangar out of Synty's and cuts its room out of the station's ring
# (tools/station_bay/build_hangar.py and cut_station.py, into
# assets/synty/cut), and makes hull panels in the station's style
# (tools/station_bay/hull_panels.py; see docs/stations.md).

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
  SM_Ship_Cruiser_03
  FX_Meshes/SM_Flame_Mesh_New
  FX_Meshes/SM_Pebble_Debris_03
  SM_Prop_Missile_01
  SM_Prop_Missile_03
  SM_Env_Astroid_01
  SM_Env_Astroid_02
  SM_Env_Astroid_03
  SM_Env_Astroid_04
  SM_Env_Astroid_05
  SM_Env_Astroid_Large_01
  SM_Env_Astroid_Large_02
  SM_Env_Astroid_Holes_03
  SM_Env_Astroid_Large_Holes_02
  SM_Ship_Station_01
  SM_Ship_Station_02
  SM_Ship_Station_03
  SM_Ship_Station_04
  SM_Ship_Station_05
  SM_Ship_Station_06
  SM_Prop_Turret_Base_Double_06
  SM_Prop_Turret_Base_Double_08
  SM_Prop_SpaceWalk_Tube_01
  SM_Bld_Landing_Platform_01
  SM_Bld_Hangar_01
  SM_Bld_Ceiling_Pipes_Straight_01
  SM_Prop_AirVent_Large_01
  SM_Prop_Antenna_01
  SM_Prop_Detail_Machine_01
  SM_Prop_Detail_Pipes_02
  SM_Prop_Detail_Wire_03
  SM_Prop_Detail_Wirebox_01
  SM_Prop_Wires_01
  SM_Prop_Wires_03
  SM_Prop_Wires_05
  SM_Chr_Attach_Crew_Male_Hair_01
  SM_Prop_Stairs_01
)
set(TEXTURES
  PolygonSciFiSpace_Texture_01_A
  PolygonSciFiSpace_Emissive_01
  Alts/PolygonSciFiSpace_Texture_01_B
  PolygonSciFiSpace_Ship_Mask_Station_01
  FX_Textures/Gradient_Boost
  FX_Textures/Explosion
  FX_Textures/ExplosionCloud
  FX_Textures/SphereGlow
  FX_Textures/Transparent_Line
  FX_Textures/PolygonSciFiSpace_Skybox_04_Front
  FX_Textures/PolygonSciFiSpace_Skybox_04_Back
  FX_Textures/PolygonSciFiSpace_Skybox_04_Left
  FX_Textures/PolygonSciFiSpace_Skybox_04_Right
  FX_Textures/PolygonSciFiSpace_Skybox_04_Up
  FX_Textures/PolygonSciFiSpace_Skybox_04_Down
)
# Out of the Unity package's Models/Collision
set(COLLISIONS
  SM_Ship_Station_01_Collision
  SM_Ship_Station_02_Collision
  SM_Ship_Station_03_Collision
  SM_Ship_Station_04_Collision
  SM_Ship_Station_05_Collision
  SM_Ship_Station_06_Collision
  SM_Bld_Hanger_01_Collision
  SM_Bld_HangerFloor_01_Collision
)
# Out of the Unity package's Models, renamed: every character (bodies, heads,
# helmets) on one skeleton, which the source files only have one by one
set(PACKAGE_MODELS Characters)
set(NAME_Characters SM_Chr_Characters)

file(MAKE_DIRECTORY "${DEST}")
foreach(m IN LISTS MODELS)
  file(COPY "${PACK}/FBX/${m}.fbx" DESTINATION "${DEST}")
endforeach()
foreach(t IN LISTS TEXTURES)
  file(COPY "${PACK}/Textures/${t}.png" DESTINATION "${DEST}")
endforeach()

if(NOT PACKAGE)
  file(GLOB PACKAGE "${ROOT}/.raw-assets/*SciFi_Space*.unitypackage")
  list(GET PACKAGE 0 PACKAGE)
endif()
if(NOT EXISTS "${PACKAGE}")
  message(FATAL_ERROR "No Synty Unity package for the collision meshes (pass -DPACKAGE=...)")
endif()
set(UNPACK "${ROOT}/.godot/synty_package")
file(REMOVE_RECURSE "${UNPACK}")
file(ARCHIVE_EXTRACT INPUT "${PACKAGE}" DESTINATION "${UNPACK}" PATTERNS "*/pathname")
file(GLOB NAMES "${UNPACK}/*/pathname")
set(WANTED "")
foreach(n IN LISTS NAMES)
  file(READ "${n}" asset)
  string(STRIP "${asset}" asset)
  foreach(c IN LISTS COLLISIONS)
    if(asset STREQUAL "Assets/Synty/PolygonSciFiSpace/Models/Collision/${c}.fbx")
      get_filename_component(dir "${n}" DIRECTORY)
      get_filename_component(guid "${dir}" NAME)
      list(APPEND WANTED "${guid}/asset")
      set(FROM_${guid} "${c}")
    endif()
  endforeach()
  foreach(m IN LISTS PACKAGE_MODELS)
    if(asset STREQUAL "Assets/Synty/PolygonSciFiSpace/Models/${m}.fbx")
      get_filename_component(dir "${n}" DIRECTORY)
      get_filename_component(guid "${dir}" NAME)
      list(APPEND WANTED "${guid}/asset")
      set(FROM_${guid} "${NAME_${m}}")
    endif()
  endforeach()
endforeach()
list(LENGTH COLLISIONS want)
list(LENGTH PACKAGE_MODELS more)
math(EXPR want "${want} + ${more}")
list(LENGTH WANTED found)
if(NOT found EQUAL want)
  message(FATAL_ERROR "Found ${found} of the ${want} collision meshes and models in ${PACKAGE}")
endif()
file(ARCHIVE_EXTRACT INPUT "${PACKAGE}" DESTINATION "${UNPACK}" PATTERNS ${WANTED})
foreach(w IN LISTS WANTED)
  get_filename_component(dir "${w}" DIRECTORY)
  file(COPY_FILE "${UNPACK}/${w}" "${DEST}/${FROM_${dir}}.fbx")
endforeach()
file(REMOVE_RECURSE "${UNPACK}")

# Import settings that are not Godot's defaults (the characters' skeleton
# retargeted to Godot's humanoid profile, so the animations in
# assets/animations fit it); a file Godot already made is left alone
file(GLOB IMPORTS "${ROOT}/tools/synty_imports/*.import")
foreach(i IN LISTS IMPORTS)
  get_filename_component(name "${i}" NAME)
  if(NOT EXISTS "${DEST}/${name}")
    file(COPY "${i}" DESTINATION "${DEST}")
  endif()
endforeach()
message(STATUS "Synty files copied to ${DEST}")

if(BLENDER)
  foreach(step build_hangar cut_station hull_panels)
    execute_process(COMMAND "${BLENDER}" -b -P "${ROOT}/tools/station_bay/${step}.py"
                    RESULT_VARIABLE made)
    if(NOT made EQUAL 0)
      message(FATAL_ERROR "Blender failed on tools/station_bay/${step}.py (${made})")
    endif()
  endforeach()
  message(STATUS "Hangar built and cut into the station: ${DEST}/cut")
else()
  message(STATUS "The hangar is not built (run this again with -DBLENDER=<blender>)")
endif()

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
