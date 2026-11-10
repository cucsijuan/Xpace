using Godot;
using Xpace.World;

namespace Xpace.Space;

/// <summary>
/// A program on a diskette, as a dealer sells it (assets/outfits): carried by the pilot, put in the ship's second drive
/// and copied onto the ship's own diskette, after which the ship has it for good. A copy is a thing like any other:
/// buy as many as wanted, sell them on.
/// </summary>
[GlobalClass]
public partial class SoftwareSpec : Outfit
{
    /// <summary>The diskette's image: disks/&lt;Image&gt;.img.</summary>
    [Export] public string Image = "";
    [Export(PropertyHint.MultilineText)] public string Description = "";

    public override string About => Description;
    public override string Details => "On a diskette: copy it to the ship's own and it stays there.";
    public override bool Carried => true;

    public override ItemRecord MakeItem(UniverseState state) => new() { Kind = ItemRecord.Diskette, Name = Model, Data = Image };

    public override bool Makes(ItemRecord item) => item.Kind == ItemRecord.Diskette && item.Data == Image;
}
