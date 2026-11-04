using Godot;

namespace Xpace.World;

/// <summary>
/// A company, corporation, guild or faction as a designer sets it up: a .tres in assets/organisations, listed in its
/// catalog.tres (its id is the file's name). What it is now (its money, what it owns) is its record in the universe
/// (<see cref="OrganisationRecord"/>); an organisation born in play has a record and no resource.
/// </summary>
[GlobalClass]
public partial class Organisation : Resource
{
    [Export] public string Name = "";
    /// <summary>What it is, as players read it: Corporation, Mining company, Guild...</summary>
    [Export] public string Kind = "Company";
    [Export(PropertyHint.MultilineText)] public string About = "";
    [Export] public Texture2D Logo;
    [Export] public Personality Personality;
    /// <summary>The faction or corporation it belongs to (none: its own).</summary>
    [Export] public Organisation Parent;
    /// <summary>The money it has when a universe starts.</summary>
    [Export] public long StartingCredits = 1_000_000;
    /// <summary>One of the universe's main powers: however badly it does, it doesn't disappear.</summary>
    [Export] public bool Permanent;

    public string Id => ResourcePath.GetFile().GetBaseName();
}
