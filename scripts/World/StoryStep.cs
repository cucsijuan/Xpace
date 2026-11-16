using Godot;

namespace Xpace.World;

/// <summary>
/// One step of a story chain (<see cref="StoryChain"/>): a contract offered to one pilot only, at a station's contract
/// board, by an owner who pays it from its own money like any other. Kinds as in <see cref="ContractRecord"/>.
/// <see cref="Lead"/> is shown when it is offered (where to get it), <see cref="Guide"/> once it is taken (how to do it).
/// </summary>
[GlobalClass]
public partial class StoryStep : Resource
{
    [Export] public string Id = "";
    [Export] public string Title = "";
    /// <summary>What the giver says on the board.</summary>
    [Export(PropertyHint.MultilineText)] public string Briefing = "";
    /// <summary>The help shown when the step is offered: where to go for it.</summary>
    [Export(PropertyHint.MultilineText)] public string Lead = "";
    /// <summary>The help shown once it is taken: how to do it.</summary>
    [Export(PropertyHint.MultilineText)] public string Guide = "";
    /// <summary>Who offers and pays it: an organisation's id.</summary>
    [Export] public string Giver = "";
    /// <summary>The station whose board offers it.</summary>
    [Export] public string Station = "";
    /// <summary>
    /// <see cref="ContractRecord.Deliver"/>, <see cref="ContractRecord.Haul"/>, <see cref="ContractRecord.Visit"/>,
    /// <see cref="ContractRecord.Recover"/> or <see cref="ContractRecord.Report"/>. Texts may name the chain's facts as {name}.
    /// </summary>
    [Export(PropertyHint.Enum, "haul,deliver,visit,recover,report")] public string Kind = ContractRecord.Haul;
    [Export] public Good Good;
    [Export] public int Tonnes;
    /// <summary>Where a haul's cargo is collected.</summary>
    [Export] public string From = "";
    /// <summary>Where the cargo is handed over.</summary>
    [Export] public string To = "";
    /// <summary>A visit's place: its sector, the point in it and how near counts.</summary>
    [Export] public string Sector = "";
    [Export] public Vector3 Point;
    [Export] public float Radius = 2000;
    /// <summary>
    /// A wreck at the step's place (<see cref="Sector"/>, <see cref="Point"/>) while it is taken: its callsign (its
    /// distress beacon sends it), with a recovery's cargo drifting round it in pods and the thing below, if any.
    /// </summary>
    [Export] public string Wreck = "";
    /// <summary>A diskette found at the wreck (empty name: none): its name, and its one file's name and text.</summary>
    [Export] public string ItemName = "";
    [Export] public string ItemFile = "";
    [Export(PropertyHint.MultilineText)] public string ItemText = "";
    /// <summary>A report's answer, as the pilot types it at the terminal (case and spaces don't matter).</summary>
    [Export] public string Answer = "";
    [Export] public long Reward;
    /// <summary>Hours to do it once taken (0: no limit).</summary>
    [Export] public float Hours;
}
