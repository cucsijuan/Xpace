using Godot;

namespace Xpace.World;

/// <summary>How people of a kind live: what they do for money.</summary>
public enum Livelihood { Trader, Hunter, Pirate }

/// <summary>People of one kind the universe keeps going: how many, with what ship and what they start with.</summary>
[GlobalClass]
public partial class PeopleLine : Resource
{
    [Export] public Livelihood Livelihood;
    [Export] public int Count = 2;
    [Export] public ShipModel Model;
    /// <summary>Their characters, in turn.</summary>
    [Export] public Personality[] Personalities = [];
    /// <summary>What one has saved when they arrive, a ship of their own aside.</summary>
    [Export] public long Savings = 5000;
    /// <summary>What a newcomer brings to pay for their ship from a yard: they come from elsewhere.</summary>
    [Export] public long NewcomerSavings = 300_000;
    /// <summary>The least bounty worth a trip, for those who hunt.</summary>
    [Export] public long LeastReward = 500;
    /// <summary>Who pays for their head from the day they appear, and how much (none: nobody yet).</summary>
    [Export] public Organisation WantedBy;
    [Export] public long Reward;
    [Export] public int MemoryRoom = 16;
    /// <summary>How long after one of them is lost someone new comes, hours.</summary>
    [Export] public double ReplaceHours = 12;
}
