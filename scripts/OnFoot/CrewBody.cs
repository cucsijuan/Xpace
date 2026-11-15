using GodotCharacters;

namespace Xpace.OnFoot;

/// <summary>A crew member's body (<see cref="Crew"/>), for the walker and for the other players on foot.</summary>
public partial class CrewBody : Body
{
    public override void _Ready() => Wear(Crew.Look, Crew.Moving);
}
