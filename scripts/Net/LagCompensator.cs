using System;
using System.Collections.Generic;
using Xpace.Space;

namespace Xpace.Net;

/// <summary>
/// The host's judge of rounds. Every ship's recent past is kept (the
/// snapshots in its <see cref="RemoteTrack"/>; the host's own ships are fed
/// theirs every tick), and every round is flown step by step against each
/// ship <b>as the shooter saw it</b>: if the shooter showed a ship 12 ticks
/// in the past, the round's step n is tested against that ship at
/// (fire tick + n - 12). What the shooter saw hit, hits (Source-style lag
/// compensation, favouring the shooter); the cost is that the target may
/// already have moved on, on its own screen, by its latency's worth.
///
/// A round is only tested once the target's history reaches the tick it
/// needs; until then it waits (the target's snapshots are late too). The
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

    private sealed class Target
    {
        public byte Id;
        public RemoteTrack History;
        public Hitbox[] Boxes;
    }

    private sealed class Round
    {
        public FireEvent Event;
        public int Step;                    // the next step to test
        public Dictionary<byte, double> Rewind;     // per target, ticks into the past
    }

    private readonly List<Target> _targets = new();
    private readonly List<Round> _rounds = new();

    public void Register(byte id, RemoteTrack history, params Hitbox[] boxes) =>
        _targets.Add(new Target { Id = id, History = history, Boxes = boxes });

    public void Unregister(byte id) => _targets.RemoveAll(t => t.Id == id);

    public int Flying => _rounds.Count;

    public void Fire(FireEvent e)
    {
        var round = new Round { Event = e, Rewind = new() };
        if (Compensate && e.Views != null)
            foreach (FireEvent.View v in e.Views)
                round.Rewind[v.Ship] = Math.Min(v.DelayTicks, MaxRewindTicks);
        _rounds.Add(round);
    }

    /// <summary>Flies the rounds as far as the histories allow, at the host's tick <paramref name="now"/>.</summary>
    public void Step(uint now)
    {
        for (int r = _rounds.Count - 1; r >= 0; r--)
        {
            Round round = _rounds[r];
            if (Fly(round) || round.Step >= LifeTicks || now > round.Event.Tick + LifeTicks + MaxWaitTicks)
                _rounds.RemoveAt(r);
        }
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
                if (t.Id == e.Shooter)
                    continue;
                double after = e.Tick + n + 1 - round.Rewind.GetValueOrDefault(t.Id);
                if (t.History.NewestTick is not uint newest || after > newest)
                    return false;           // its past has not arrived yet: wait
            }

            SectorPoint from = e.At(n, TickSeconds), to = e.At(n + 1, TickSeconds);
            foreach (Target t in _targets)
            {
                if (t.Id == e.Shooter)
                    continue;
                double after = e.Tick + n + 1 - round.Rewind.GetValueOrDefault(t.Id);
                RemoteTrack.Pose p0 = t.History.Sample(after - 1), p1 = t.History.Sample(after);
                foreach (Hitbox box in t.Boxes)
                    if (box.Swept(from, to, p0, p1))
                    {
                        Hit?.Invoke(new HitEvent
                        {
                            Shooter = e.Shooter, Sequence = e.Sequence, Target = t.Id,
                            Tick = (uint)(e.Tick + n + 1), Damage = e.Damage,
                        });
                        return true;
                    }
            }
            round.Step++;
        }
        return true;
    }
}
