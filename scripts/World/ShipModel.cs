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

    [ExportGroup("Crew")]
    /// <summary>A voyage's time at the crew's hands: launch and door, spooling, approach, docking.</summary>
    [Export] public double HandlingSeconds = 240;
    /// <summary>How long it waits before looking again when nothing needs carrying.</summary>
    [Export] public double WaitSeconds = 600;
    /// <summary>It fetches a good its home uses when the stock is under this share of what the market wants.</summary>
    [Export(PropertyHint.Range, "0,1,0.05")] public double RestockBelow = 0.8;
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
