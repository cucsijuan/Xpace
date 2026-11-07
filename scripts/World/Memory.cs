using System;
using System.Collections.Generic;
using System.Linq;

namespace Xpace.World;

/// <summary>Something an actor remembers of another: who, what happened, how much it mattered (below 0: it holds it against them), when and where.</summary>
public sealed class MemoryEntry
{
    public string About { get; set; } = "";
    public string What { get; set; } = "";
    public double Weight { get; set; }
    public double Clock { get; set; }
    public string Sector { get; set; } = "";
}

/// <summary>Whoever keeps memories: anyone in the universe. How much they keep is theirs: the important keep more.</summary>
public interface IRemembers
{
    List<MemoryEntry> Memories { get; }
    /// <summary>How many things it can keep; past that the one that matters least goes.</summary>
    int MemoryRoom { get; }
}

/// <summary>
/// What everyone remembers of everyone, one system for all: an actor keeps what happened with others, by actor id
/// (<see cref="Universe.ActorOf"/>), and what it feels about them now is what it remembers, fading with time. What it
/// does about that is up to who it is: its <see cref="Personality"/> and its way of life. New kinds of memories are just
/// new values of <see cref="MemoryEntry.What"/>.
/// </summary>
public static class Memories
{
    public const string HitMe = "hit me", KilledMine = "destroyed my ship", HitMine = "hit my ship", NothingAboard = "nothing aboard";

    /// <summary>Keeps something that happened. With no room left, what matters least now goes.</summary>
    public static void Note(IRemembers who, string about, string what, double weight, double clock, string sector = "", double fadeHours = 48)
    {
        if (who == null || string.IsNullOrEmpty(about))
            return;
        who.Memories.Add(new MemoryEntry { About = about, What = what, Weight = weight, Clock = clock, Sector = sector ?? "" });
        while (who.Memories.Count > Math.Max(1, who.MemoryRoom))
            who.Memories.Remove(who.Memories.OrderBy(m => Math.Abs(Faded(m, clock, fadeHours))).First());
    }

    /// <summary>It has that memory of an actor, no older than that.</summary>
    public static bool Knows(IRemembers who, string about, string what, double clock, double seconds) =>
        who?.Memories.Any(m => m.About == about && m.What == what && clock - m.Clock <= seconds) ?? false;

    /// <summary>What it feels about an actor now: below 0 a grudge, above 0 goodwill.</summary>
    public static double Feeling(IRemembers who, string about, double clock, double fadeHours = 48) =>
        who?.Memories.Where(m => m.About == about).Sum(m => Faded(m, clock, fadeHours)) ?? 0;

    /// <summary>Who it holds the strongest grudge against, and how strong; none: empty.</summary>
    public static (string Actor, double Feeling) WorstGrudge(IRemembers who, double clock, double fadeHours = 48)
    {
        (string Actor, double Feeling) worst = ("", 0);
        foreach (string actor in who?.Memories.Select(m => m.About).Distinct() ?? Enumerable.Empty<string>())
            if (Feeling(who, actor, clock, fadeHours) is double feeling && feeling < worst.Feeling)
                worst = (actor, feeling);
        return worst;
    }

    private static double Faded(MemoryEntry memory, double clock, double fadeHours) =>
        memory.Weight * Math.Pow(0.5, Math.Max(0, clock - memory.Clock) / 3600 / Math.Max(fadeHours, 0.01));
}
