using Godot;
using Xpace.Ship;

namespace Xpace.World;

/// <summary>
/// A kind of working ship, a .tres in assets/ship/models: what it is built of, for the universe to reckon its voyages
/// by the real numbers, the scene that shows it, and how its crew works. A station's fleet is made of these
/// (<see cref="FleetLine"/>).
/// </summary>
[GlobalClass]
public partial class ShipModel : Resource
{
    [Export] public string Name = "";
    [Export] public ShipSpec Hull;
    [Export] public EngineSpec Engine;
    [Export] public GeneratorSpec Generator;
    /// <summary>The ship as it flies, when someone is there to see it.</summary>
    [Export] public PackedScene Scene;

    [ExportGroup("Building")]
    /// <summary>What a shipyard builds it of.</summary>
    [Export] public Recipe Recipe;
    /// <summary>What the yard asks for its work, on top of the materials and the pieces fitted.</summary>
    [Export] public long BuildFee = 250_000;
    /// <summary>How long it takes on the slip, hours of universe time.</summary>
    [Export] public double BuildHours = 6;
    /// <summary>What one fetches used, of its fee.</summary>
    [Export(PropertyHint.Range, "0,1,0.05")] public double Resale = 0.7;

    [ExportGroup("Crew")]
    /// <summary>The most of its owner's money a crew spends on fuel at a time: it needs the rest for what it came to do.</summary>
    [Export(PropertyHint.Range, "0,1,0.05")] public double FuelMoneyShare = 0.25;
    /// <summary>What it takes on top of what its way needs, of that.</summary>
    [Export(PropertyHint.Range, "0,1,0.05")] public double FuelMargin = 0.25;
    /// <summary>A voyage's time at the crew's hands: launch and door, spooling, approach, docking.</summary>
    [Export] public double HandlingSeconds = 240;
    /// <summary>How long it waits before looking again when nothing needs carrying.</summary>
    [Export] public double WaitSeconds = 600;
    /// <summary>It fetches a good its home uses when the stock is under this share of what the market wants.</summary>
    [Export(PropertyHint.Range, "0,1,0.05")] public double RestockBelow = 0.8;
    /// <summary>News of a market older than this is no use to its crew: they go and look (hours).</summary>
    [Export] public double NewsHours = 6;
    /// <summary>How near a way a ship lost counts as lost on that way (m).</summary>
    [Export] public float DangerMeters = 30000f;
    /// <summary>How long its crew takes to read an interdictor's frequency and set the generator to it (s): a beam that wanders faster than that gets through.</summary>
    [Export] public double CrewTuneSeconds = 2;
    /// <summary>It trades nothing: it goes with its owner's ships and fights who attacks them.</summary>
    [Export] public bool Escort;
    /// <summary>Its pilot fights back under attack; else it runs (see <see cref="Xpace.Space.CargoView"/>).</summary>
    [Export] public bool Fights;
    /// <summary>How far from a station it asks for a pad, and how soon it asks again when there is none, metres and seconds.</summary>
    [Export] public float AskAtMeters = 2000;
    [Export] public double AskEverySeconds = 30;
    /// <summary>How long after it leaves a pad the station won't give that pad to anyone, seconds: from its clearance to leave.</summary>
    [Export] public double PadRestSeconds = 90;
    /// <summary>How long it waits on the pad, cleared to leave, for the door.</summary>
    [Export] public double DoorSeconds = 14;
    /// <summary>How fast it goes through a hangar, and over its pad, m/s.</summary>
    [Export] public float HangarSpeed = 15;
    [Export] public float PadSpeed = 3;
    /// <summary>The share of a seller's stock it may take in one go.</summary>
    [Export(PropertyHint.Range, "0,1,0.05")] public double TakeShare = 0.5;
}
