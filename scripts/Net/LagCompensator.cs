using System;
using System.Collections.Generic;
using Xpace.Space;

namespace Xpace.Net;

/// <summary>
/// The host's judge of rounds and blasts. Every ship's recent past is kept
/// (the snapshots in its <see cref="RemoteTrack"/>; the host's own ships
/// are fed theirs every tick), and every round is flown step by step
/// against each ship <b>as the shooter saw it</b>: if the shooter showed a
/// ship 12 ticks in the past, the round's step n is tested against that
/// ship at (fire tick + n - 12). What the shooter saw hit, hits
/// (Source-style lag compensation, favouring the shooter); the cost is
/// that the target may already have moved on, on its own screen, by its
/// latency's worth.
///
/// A blast is judged the same way: its fragments reach every ship within
/// its radius where the shooter's machine showed that ship when the fuse
/// went off.
///
/// A round or a blast is only judged once the histories reach the ticks it
/// needs; until then it waits (the targets' snapshots are late too). The
/// rewind is capped at <see cref="MaxRewindTicks"/>: a shooter on a worse
/// line has to lead its targets more, as with a real sensor's lag.
/// </summary>
public sealed class LagCompensator
{
    public float TickSeconds = 1f / 60;
    public int LifeTicks = 240;             // a round flies 4 s
    public double MaxRewindTicks = 30;      // 0.5 s
    public int MaxWaitTicks = 120;          // for a target's history to arrive

    /// <summary>False tests every round against the ships as they are now, as without compensation.</summary>
    public bool Compensate = true;

    public event Action<HitEvent> Hit;

    /// <summary>Whether something solid that never moves (rock) lies between two points: a round stops there. Null: nothing.</summary>
    public Func<SectorPoint, SectorPoint, bool> Solid;

    private sealed class Target
    {
        public ushort Id;
        public RemoteTrack History;
        public Hitbox[] Boxes;
    }

    private sealed class Round
    {
        public FireEvent Event;
        public int Step;                    // the next step to test
        public Dictionary<ushort, double> Rewind;   // per target, ticks into the past
    }

    private sealed class Burst
    {
        public BlastEvent Event;
        public Dictionary<ushort, double> Rewind;
    }

    private readonly List<Target> _targets = new();
    private readonly List<Round> _rounds = new();
    private readonly List<Burst> _bursts = new();

    public void Register(ushort id, RemoteTrack history, params Hitbox[] boxes) =>
        _targets.Add(new Target { Id = id, History = history, Boxes = boxes });

    public void Unregister(ushort id) => _targets.RemoveAll(t => t.Id == id);

    public int Flying => _rounds.Count;

    public void Fire(FireEvent e) => _rounds.Add(new Round { Event = e, Rewind = Rewinds(e.Views) });

    public void Blast(BlastEvent e) => _bursts.Add(new Burst { Event = e, Rewind = Rewinds(e.Views) });

    private Dictionary<ushort, double> Rewinds(List<FireEvent.View> views)
    {
        var rewind = new Dictionary<ushort, double>();
        if (Compensate && views != null)
            foreach (FireEvent.View v in views)
                rewind[v.Ship] = Math.Min(v.DelayTicks, MaxRewindTicks);
        return rewind;
    }

    /// <summary>Flies the rounds and sets off the blasts as far as the histories allow, at the host's tick <paramref name="now"/>.</summary>
    public void Step(uint now)
    {
        for (int r = _rounds.Count - 1; r >= 0; r--)
        {
            Round round = _rounds[r];
            if (Fly(round) || round.Step >= LifeTicks || now > round.Event.Tick + LifeTicks + MaxWaitTicks)
                _rounds.RemoveAt(r);
        }
        for (int b = _bursts.Count - 1; b >= 0; b--)
            if (Burn(_bursts[b], late: now > _bursts[b].Event.Tick + MaxWaitTicks))
                _bursts.RemoveAt(b);
    }

    /// <summary>True when the round is done with: it hit something.</summary>
    private bool Fly(Round round)
    {
        FireEvent e = round.Event;
        while (round.Step < LifeTicks)
        {
            int n = round.Step;
            foreach (Target t in _targets)
            {
                if (t.Id == e.Ship || t.Boxes.Length == 0)
                    continue;
                double after = e.Tick + n + 1 - round.Rewind.GetValueOrDefault(t.Id);
                if (t.History.NewestTick is not uint newest || after > newest)
                    return false;           // its past has not arrived yet: wait
            }

            SectorPoint from = e.At(n, TickSeconds), to = e.At(n + 1, TickSeconds);
            foreach (Target t in _targets)
            {
                if (t.Id == e.Ship)
                    continue;
                double after = e.Tick + n + 1 - round.Rewind.GetValueOrDefault(t.Id);
                RemoteTrack.Pose p0 = t.History.Sample(after - 1), p1 = t.History.Sample(after);
                if (p1.Destroyed)
                    continue;               // blown up then: the round goes through where it was
                foreach (Hitbox box in t.Boxes)
                    if (box.Swept(from, to, p0, p1))
                    {
                        Hit?.Invoke(new HitEvent
                        {
                            Shooter = e.Shooter, Ship = e.Ship, Sequence = e.Sequence, Target = t.Id,
                            Tick = (uint)(e.Tick + n + 1), Damage = e.Damage,
                        });
                        return true;
                    }
            }
            if (Solid != null && Solid(from, to))
                return true;                // into rock
            round.Step++;
        }
        return true;
    }

    /// <summary>
    /// The blast, once every ship's past has reached the moment it went off
    /// (or it has waited long enough: the ships whose past is not there are
    /// spared). Ships with nothing to hit (no hitboxes) are not hurt.
    /// </summary>
    private bool Burn(Burst burst, bool late)
    {
        BlastEvent e = burst.Event;
        double At(Target t) => e.Tick + e.TickFraction - burst.Rewind.GetValueOrDefault(t.Id);
        if (!late)
            foreach (Target t in _targets)
                if (t.Id != e.Missile && t.Boxes.Length > 0 && (t.History.NewestTick is not uint newest || At(t) > newest))
                    return false;
        foreach (Target t in _targets)
        {
            if (t.Id == e.Missile || t.Boxes.Length == 0 || t.History.NewestTick is not uint newest || At(t) > newest)
                continue;
            RemoteTrack.Pose p = t.History.Sample(At(t));
            if (p.Destroyed)
                continue;
            float damage = e.DamageAt(p.Position.To(e.Position).Length());
            if (damage > 0)
                Hit?.Invoke(new HitEvent
                {
                    Shooter = e.Shooter, Ship = e.Ship, Sequence = e.Sequence, Target = t.Id, Tick = e.Tick, Damage = damage,
                });
        }
        return true;
    }
}
