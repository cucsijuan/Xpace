using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Xpace.World;

/// <summary>Something done to an owner's ship: where, when, by which pilot (empty: nobody known), and whether it was lost.</summary>
public sealed class Incident
{
    public string Sector { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public double Clock { get; set; }
    public string By { get; set; } = "";
    public bool Lost { get; set; }
}

/// <summary>
/// How an organisation answers damage, by its <see cref="Personality"/>: the same rules for all. It keeps what was done
/// to its ships, stays off a route where it lost one for as long as its nerve says, less the more it needs what lies that way, and puts money on the head of the
/// pilot who did it, paid from its own account to whichever pilot destroys them. Pilots who own ships decide for
/// themselves.
/// </summary>
public static class Reactions
{
    private const int MostIncidents = 32;

    /// <summary>A ship of the owner's was hit or lost.</summary>
    public static void Happened(OrganisationRecord owner, Personality personality, ShipModel model, string sector, Vector3 at, string by, double clock, bool lost)
    {
        if (owner == null || personality == null)
            return;
        owner.Incidents.Add(new Incident { Sector = sector, X = at.X, Y = at.Y, Z = at.Z, Clock = clock, By = by ?? "", Lost = lost });
        if (owner.Incidents.Count > MostIncidents)
            owner.Incidents.RemoveAt(0);
        if (string.IsNullOrEmpty(by))
            return;
        double share = lost ? personality.BountyShare : personality.HitBountyShare;
        long amount = (long)(share * personality.Aggression * model.BuildFee);
        long free = owner.Credits - owner.Bounties.Values.Sum();
        if (Math.Min(amount, free) > 0)
            owner.Bounties[by] = owner.Bounties.GetValueOrDefault(by) + Math.Min(amount, free);
    }

    /// <summary>How long a lost ship keeps the owner off that way, hours, with <paramref name="need"/> (0 to 1) of what it wants missing.</summary>
    public static double FearHours(Personality personality, double need = 0) =>
        personality.FearHours * (1 - personality.Aggression) * (1 - Math.Clamp(need, 0, 1) * personality.NeedOverFear);

    /// <summary>The owner lost a ship on those legs lately, and hasn't the nerve to send another yet.</summary>
    public static bool Avoids(OrganisationRecord owner, Personality personality, IEnumerable<StationBook.Leg> legs, float danger, double clock, double need = 0)
    {
        if (owner == null || personality == null)
            return false;
        double since = clock - FearHours(personality, need) * 3600;
        List<Incident> losses = owner.Incidents.Where(i => i.Lost && i.Clock >= since).ToList();
        return losses.Count > 0 && legs.Where(l => l.Open).Any(l => losses.Any(i => i.Sector == l.Sector && Off(l, i) <= danger));
    }

    private static float Off(StationBook.Leg leg, Incident incident)
    {
        var at = new Vector3((float)incident.X, (float)incident.Y, (float)incident.Z);
        return Geometry3D.GetClosestPointToSegment(at, leg.From, leg.To).DistanceTo(at);
    }

    /// <summary>It lost ships lately and is the kind to answer with escorts.</summary>
    public static bool Threatened(OrganisationRecord owner, Personality personality, double clock) =>
        owner != null && personality != null && personality.Aggression >= personality.EscortAbove &&
        owner.Incidents.Any(i => i.Lost && i.Clock >= clock - personality.FearHours * 3600);

    /// <summary>What organisations offer for the pilot.</summary>
    public static long Hunted(UniverseState state, string pilot) => state.Organisations.Values.Sum(o => o.Bounties.GetValueOrDefault(pilot));

    /// <summary>A pilot destroyed another: what was on the head of the second is theirs, as far as those who offered it can pay. Returns what was paid.</summary>
    public static long Collect(UniverseState state, string killed, PilotRecord killer)
    {
        long paid = 0;
        foreach (OrganisationRecord owner in state.Organisations.Values)
            if (owner.Bounties.Remove(killed, out long offered))
            {
                long pays = Math.Min(offered, owner.Credits);
                owner.Credits -= pays;
                killer.Credits += pays;
                paid += pays;
            }
        return paid;
    }
}
