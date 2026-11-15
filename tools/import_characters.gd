# Makes the crew's bodies (scripts/OnFoot/Crew.cs) with the character library: models retargeted to the humanoid
# profile, the animation library with its natural speeds. Run by tools/import_synty.cmake after the Synty files are
# in place, or by hand (the C# project built first):
#
#   godot --headless --path . --script res://tools/import_characters.gd
extends SceneTree

func _initialize():
	load("res://scripts/OnFoot/CrewImport.cs").new().Run()
	quit()
