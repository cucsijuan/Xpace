using System;
using Godot;

namespace Xpace.World;

/// <summary>
/// How someone behaves, an organisation, a person or a crew, a .tres in assets/personalities: the same rules for all,
/// with these numbers telling them apart when they meet damage or competition. Each 0 to 1 unless it says otherwise.
/// </summary>
[GlobalClass]
public partial class Personality : Resource
{
    [Export] public string Name = "";

    /// <summary>How readily it answers with force.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float Aggression = 0.5f;

    /// <summary>How readily it talks and pays its way out instead.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float Diplomacy = 0.5f;

    /// <summary>How far outside the law it will go.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float Shadiness = 0.1f;

    /// <summary>What it asks on top of what a thing costs it to make, of that cost.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float Margin = 0.15f;

    /// <summary>How much of its money it risks to grow.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float Ambition = 0.5f;

    /// <summary>How long a lost ship would keep it off that route with no aggression at all, hours; aggression shortens it.</summary>
    [Export] public double FearHours = 24;

    /// <summary>How much being out of something shortens that: with none left and this at 1, it sends a ship at once.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float NeedOverFear = 0.9f;

    /// <summary>The aggression from which it answers a loss by buying escorts.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float EscortAbove = 0.3f;

    /// <summary>What it puts on a pilot's head for a ship lost, of the ship's build fee, at full aggression.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public float BountyShare = 0.2f;

    /// <summary>The same for a ship hit and not lost.</summary>
    [Export(PropertyHint.Range, "0,1,0.001")] public float HitBountyShare = 0.01f;

    /// <summary>How long what happened takes to matter half as much, hours.</summary>
    [Export] public double MemoryHours = 48;

    [ExportGroup("Growing")]
    /// <summary>A good it uses counts as short under this share of what its market wants.</summary>
    [Export(PropertyHint.Range, "0,1,0.01")] public double GrowShortBelow = 0.1;
    /// <summary>How long a shortage has to last, with all its ships at work and free traders not making up for it, hours.</summary>
    [Export] public double GrowAfterHours = 72;
    /// <summary>The most ships a station gets on top of its fleet this way.</summary>
    [Export] public int MostExtraShips = 2;

    [ExportGroup("Shipyard")]
    /// <summary>What a yard asks on top of its fee, of the upkeep it has paid since it last sold a ship: little sold, more on each.</summary>
    [Export(PropertyHint.Range, "0,1,0.05")] public double YardRecovers = 0.25;
    /// <summary>Its fee is never more than this many times the model's: above that nobody would ever buy.</summary>
    [Export] public double YardMostFee = 3;

    [ExportGroup("Short of money")]
    /// <summary>With less than this many hours of its upkeep in hand it counts itself broke.</summary>
    [Export] public double ReserveHours = 24;
    /// <summary>How long it holds out broke before it starts selling, hours.</summary>
    [Export] public double GraceHours = 48;
    /// <summary>What it will take in the end, of what a thing is worth, and how long its price takes to fall there, hours.</summary>
    [Export(PropertyHint.Range, "0,1,0.05")] public double SaleFloor = 0.3;
    [Export] public double SaleFallHours = 72;

    [ExportGroup("Going for ships")]
    /// <summary>A pirate goes for a ship only with this much aboard, as its scanner reads it.</summary>
    [Export] public int PrizeTonnes = 1;
    /// <summary>Hours before it looks again at a ship it found not worth it.</summary>
    [Export] public double LookAgainHours = 3;

    [ExportGroup("Under fire")]
    /// <summary>With a ship that can't fight, they start thinking of throwing its cargo out with this much of the hull left, in the hope that is what they came for.</summary>
    [Export(PropertyHint.Range, "0,1,0.05")] public float JettisonBelowHull = 0.7f;
    /// <summary>With this much left they throw it out whatever it is and whatever is coming.</summary>
    [Export(PropertyHint.Range, "0,1,0.05")] public float JettisonLastHull = 0.2f;
    /// <summary>Between the two, the chance of giving in each time they think about it, and how often that is, seconds.</summary>
    [Export(PropertyHint.Range, "0,1,0.05")] public double JettisonChance = 0.4;
    [Export] public double JettisonThinkSeconds = 3;
    /// <summary>What cargo has to be worth for them to hold on to it as hard as it can, credits.</summary>
    [Export] public double ValuableCargo = 50000;
    /// <summary>How far each reason moves it from the first limit towards the last: 1 is all the way. They add up.</summary>
    [Export(PropertyHint.Range, "0,1,0.05")] public double HoldForValue = 0.6;
    [Export(PropertyHint.Range, "0,1,0.05")] public double HoldForShelter = 0.5;
    [Export(PropertyHint.Range, "0,1,0.05")] public double HoldForHelp = 0.5;
    /// <summary>How far outside a protected zone they start counting on reaching it (m).</summary>
    [Export] public float ShelterHoldMeters = 8000f;
    /// <summary>In a ship that fights, with this much of the hull left they run instead.</summary>
    [Export(PropertyHint.Range, "0,1,0.05")] public float RunBelowHull = 0.35f;

    /// <summary>The hull under which they may give up their cargo: lower the more it is worth, the nearer a protected zone (m away) and with help coming.</summary>
    public float JettisonAt(double worth, float zoneMeters, bool help)
    {
        double hold = HoldForValue * Math.Min(1, worth / Math.Max(ValuableCargo, 1)) + HoldForShelter * Math.Clamp(1 - zoneMeters / Math.Max(ShelterHoldMeters, 1), 0, 1) + (help ? HoldForHelp : 0);
        return Mathf.Lerp(JettisonBelowHull, JettisonLastHull, (float)Math.Min(1, hold));
    }
}
