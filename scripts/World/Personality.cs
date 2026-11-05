using Godot;

namespace Xpace.World;

/// <summary>
/// How an organisation behaves, a .tres in assets/personalities: the same rules for all, with these numbers telling
/// them apart when they meet damage or competition. Each 0 to 1.
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
}
