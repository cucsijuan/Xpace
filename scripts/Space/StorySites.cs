using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Xpace.Net;
using Xpace.World;

namespace Xpace.Space;

/// <summary>
/// The places a contract sends a pilot to, made real while the contract is taken and its sector is loaded: a wreck
/// sending its callsign on a distress beacon (<see cref="ContractRecord.Wreck"/>), the giver's lost cargo drifting round
/// it in pods, as many tonnes as are still out there, and the thing to be found there, if the taker hasn't it yet
/// (<see cref="ContractRecord.Item"/>). By the universe's owner: other machines see them as anything the game brings in.
/// Nothing is kept of them but the contract: made again, they are as the contract says.
/// </summary>
public partial class StorySites : Node
{
    [Export] public PackedScene WreckScene;
    [Export] public PackedScene PodScene;
    /// <summary>Tonnes in each pod of a wreck's cargo.</summary>
    [Export] public int PodTonnes = 2;
    /// <summary>How far round the wreck its pods drift, metres.</summary>
    [Export] public float PodSpread = 150f;
    [Export] public double LookEverySeconds = 1;

    private readonly Dictionary<string, Node3D> _wrecks = new();
    private double _wait;

    public override void _PhysicsProcess(double delta)
    {
        _wait -= delta;
        if (_wait > 0 || Universe.Current is not { IsGuest: false } universe || SectorHost.Current is not SectorHost host)
            return;
        _wait = LookEverySeconds;
        var wanted = new HashSet<string>();
        foreach (ContractRecord c in universe.State.Contracts.Values)
        {
            if (c.State != ContractRecord.Taken || c.Wreck == "" || c.Sector == "" || host.Root(c.Sector) is not FloatingOrigin origin)
                continue;
            wanted.Add(c.Id);
            Keep(c, origin, universe.State.Pilots.GetValueOrDefault(c.TakenBy));
        }
        foreach (string id in _wrecks.Keys.Where(id => !wanted.Contains(id)).ToList())
        {
            if (IsInstanceValid(_wrecks[id]))
            {
                foreach (CargoPod pod in Sectors.InGroup(_wrecks[id], CargoPod.Group).OfType<CargoPod>().Where(p => p.Site == id))
                    pod.QueueFree();
                _wrecks[id].QueueFree();
            }
            _wrecks.Remove(id);
        }
    }

    /// <summary>The wreck of a contract, where it is made (null: not now).</summary>
    public Node3D WreckOf(string contract) => _wrecks.TryGetValue(contract, out Node3D w) && IsInstanceValid(w) ? w : null;

    private void Keep(ContractRecord c, FloatingOrigin origin, PilotRecord taker)
    {
        Vector3 at = origin.Origin.To(new SectorPoint(c.X, c.Y, c.Z));
        if (WreckOf(c.Id) is not Node3D wreck || FloatingOrigin.Of(wreck) != origin)
        {
            wreck = WreckScene.Instantiate<Node3D>();
            wreck.Name = $"Wreck_{c.Id}";
            wreck.Position = at;
            if (wreck.FindChildren("*", "", true, false).OfType<DistressBeacon>().FirstOrDefault() is DistressBeacon beacon)
                beacon.Callsign = c.Wreck;
            Spawn(wreck, origin);
            _wrecks[c.Id] = wreck;
        }
        List<CargoPod> pods = Sectors.InGroup(wreck, CargoPod.Group).OfType<CargoPod>().Where(p => p.Site == c.Id && IsInstanceValid(p) && !p.IsQueuedForDeletion()).ToList();
        int n = pods.Count;
        if (c.Kind == ContractRecord.Recover && c.Good != "")
            for (int missing = c.Tonnes - c.Loaded - pods.Where(p => p.Item == null).Sum(p => p.Tonnes); missing > 0; missing -= PodTonnes)
                Pod(c, origin, wreck, n++, Math.Min(PodTonnes, missing), null);
        if (c.Item != null && !pods.Any(p => p.Item != null) && taker?.Items.Any(i => i.Name == c.Item.Name) != true)
            Pod(c, origin, wreck, n, 0, c.Item);
    }

    private void Pod(ContractRecord c, FloatingOrigin origin, Node3D wreck, int n, int tonnes, ItemRecord item)
    {
        var pod = PodScene.Instantiate<CargoPod>();
        float angle = n * 2.39996f, up = (n % 5 - 2) * 0.25f;     // golden angle: spread round the wreck, never on one another
        var out_ = new Vector3(MathF.Cos(angle), up, MathF.Sin(angle)).Normalized();
        pod.Position = wreck.Position + out_ * PodSpread * (0.4f + 0.6f * ((n * 7 % 10) / 10f));
        pod.Velocity = out_ * 0.3f;
        pod.Good = item == null ? c.Good : "";
        pod.Tonnes = tonnes;
        pod.Owner = c.Giver;
        pod.Item = item;
        pod.Site = c.Id;
        pod.LifeSeconds = 0;
        pod.ClearSeconds = 0;
        Spawn(pod, origin);
    }

    private static void Spawn(Node3D node, FloatingOrigin origin)
    {
        foreach (NetBody body in node.FindChildren("*", "", true, false).OfType<NetBody>())
            body.Spawned = true;
        origin.AddChild(node);
    }
}
