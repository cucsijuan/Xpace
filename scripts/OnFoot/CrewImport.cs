using Godot;
using GodotCharacters;

namespace Xpace.OnFoot;

/// <summary>Runs the character import for the crew (<c>tools/import_characters.gd</c>).</summary>
[GlobalClass]
public partial class CrewImport : RefCounted
{
    public void Run() => new CharacterImport(Crew.Plan()).Run();
}
