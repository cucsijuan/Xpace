using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Xpace.Space;

namespace Xpace.World;

/// <summary>
/// What every station and gate of the universe is, read from the sectors' scenes once, so that the universe can work
/// without them loaded: markets, factories, fleets, owners, and the sectors' ways. Plain data after that.
/// </summary>
public sealed class StationBook
{
    public sealed class Station
    {
        public string Id, Title, Sector, Owner;
        public Vector3 At;
        /// <summary>How far round it a passing ship keeps clear, metres.</summary>
        public float Radius;
        public List<Pad> Pads = new();
        public MarketGood[] Goods;
        public OutfitLine[] Outfits;
        public FactoryLine[] Factories;
        public FleetLine[] Fleet;
        public ShipModel[] Shipyard;
        public double Upkeep;
        public long Worth;
    }

    /// <summary>A landing pad of a station, with the way to it from outside its hangar's door.</summary>
    public sealed class Pad
    {
        public int Dock, Number;
        public Transform3D Pose;
        public Vector3 Door, Inside;
        public Vector2 Footprint;
        public float Height, Clearance, Lift;
    }

    public sealed class Gate
    {
        public string Id, Name, Sector, Leads, Exit, Owner;
        public Vector3 At;
        public long Toll;
        public double TransitSeconds;
    }

    /// <summary>A commercial beacon.</summary>
    public sealed class Beacon
    {
        public string Id, Sector;
        public Vector3 At;
        public int Reach;
        public float Heard;
    }

    public readonly List<Station> Stations = new();
    public readonly List<Gate> Gates = new();
    public readonly List<Beacon> Beacons = new();
    public readonly Dictionary<string, Voyage.Space> Space = new();

    public Station Find(string id) => Stations.FirstOrDefault(s => s.Id == id);

    public static StationBook Read(SectorMap map)
    {
        var book = new StationBook();
        foreach (Sector sector in map?.Sectors ?? new Godot.Collections.Array<Sector>())
            if (!string.IsNullOrEmpty(sector?.Scene) && GD.Load<PackedScene>(sector.Scene)?.Instantiate() is Node root)
            {
                book.Add(sector.Id, root);
                root.Free();
            }
        return book;
    }

    private void Add(string sector, Node root)
    {
        var space = new Voyage.Space();
        Space[sector] = space;
        foreach (Node node in root.FindChildren("*", "", true, false))
        {
            if (node is MediumMass mass)
                space.Masses.Add((At(mass), mass.Reach));
            if (node is InterdictorBuoy interdictor)
                space.Beams.Add((At(interdictor), interdictor.Model));
            if (node is not StationRadio radio)
                continue;
            Node parent = radio.GetParent();
            Vector3 at = parent is Node3D place ? At(place) : At(radio);
            if (parent is CommercialBeacon beacon)
                Beacons.Add(new Beacon { Id = radio.Id, Sector = sector, At = at, Reach = beacon.ReachJumps, Heard = beacon.HeardMeters });
            if (radio.Laws is StationLaws laws && parent is not JumpGate)
                space.Limits.Add((at, laws.SpeedRadius, laws.SpeedLimit));
            if (parent is JumpGate gate)
                Gates.Add(new Gate
                {
                    Id = radio.Id, Name = gate.Name, Sector = sector, Leads = gate.Destination, Exit = gate.ExitGate, At = at, Owner = radio.OwnedBy?.Id ?? "",
                    Toll = radio.Laws?.Toll ?? 0, TransitSeconds = gate.TransitSeconds,
                });
            else if (parent.GetNodeOrNull<StationMarket>("Market") is StationMarket market)
                Stations.Add(new Station
                {
                    Pads = PadsOf(parent), Radius = RadiusOf(parent, at),
                    Id = radio.Id, Title = radio.ShownName, Sector = sector, At = at, Owner = radio.OwnedBy?.Id ?? "", Upkeep = market.UpkeepPerHour, Worth = market.Worth,
                    Goods = market.Goods.Where(g => g?.Good != null).ToArray(), Outfits = market.Outfits, Factories = market.Factories, Fleet = market.Fleet, Shipyard = market.Shipyard,
                });
        }
    }

    // Numbered as the station and its hangars number them when they start
    private static List<Pad> PadsOf(Node station)
    {
        var pads = new List<Pad>();
        List<HangarDock> docks = station.FindChildren("*", "", true, false).OfType<HangarDock>().ToList();
        for (int d = 0; d < docks.Count; d++)
        {
            HangarDock dock = docks[d];
            Transform3D pose = Pose(dock);
            List<LandingPad> here = (dock.GetParent() ?? dock).FindChildren("*", "", true, false).OfType<LandingPad>().ToList();
            for (int p = 0; p < here.Count; p++)
                pads.Add(new Pad
                {
                    Dock = dock.Number != 0 ? dock.Number : d + 1, Number = here[p].Number != 0 ? here[p].Number : p + 1, Pose = Pose(here[p]),
                    Door = pose * dock.Approach, Inside = pose * dock.Inside, Footprint = here[p].MaxFootprint, Height = here[p].MaxHeight,
                    Clearance = here[p].Clearance, Lift = here[p].LaunchHeight,
                });
        }
        return pads;
    }

    private static float RadiusOf(Node station, Vector3 at)
    {
        float radius = 0;
        foreach (AvoidCapsule capsule in station.FindChildren("*", "", true, false).OfType<AvoidCapsule>())
            radius = Mathf.Max(radius, Pose(capsule).Origin.DistanceTo(at) + capsule.Radius + capsule.Height / 2);
        return radius;
    }

    // Out of the tree a node has no global position: its transforms up to the scene's root
    private static Vector3 At(Node3D node) => Pose(node).Origin;

    private static Transform3D Pose(Node3D node)
    {
        Transform3D t = node.Transform;
        for (Node up = node.GetParent(); up != null; up = up.GetParent())
            if (up is Node3D n)
                t = n.Transform * t;
        return t;
    }

    /// <summary>The gates a ship goes through between two sectors, in order (none: the same sector; null: no way).</summary>
    public List<Gate> Way(string from, string to)
    {
        var came = new Dictionary<string, Gate> { [from] = null };
        var open = new Queue<string>();
        open.Enqueue(from);
        while (open.Count > 0 && !came.ContainsKey(to))
        {
            string here = open.Dequeue();
            foreach (Gate gate in Gates.Where(g => g.Sector == here && !came.ContainsKey(g.Leads)))
            {
                came[gate.Leads] = gate;
                open.Enqueue(gate.Leads);
            }
        }
        if (!came.ContainsKey(to))
            return null;
        var way = new List<Gate>();
        for (string at = to; came[at] != null; at = came[at].Sector)
            way.Insert(0, came[at]);
        return way;
    }

    /// <summary>Those legs pass within reach of a fixed interdictor.</summary>
    public bool Beset(List<Leg> legs) => legs.Any(l => Space[l.Sector].Beams.Any(b =>
        b.Spec.Density(Geometry3D.GetClosestPointToSegment(b.At, l.From, l.To).DistanceSquaredTo(b.At)) > 0));

    /// <summary>The stations a beacon reports.</summary>
    public IEnumerable<Station> ReportedBy(Beacon beacon) => Stations.Where(s => Way(beacon.Sector, s.Sector)?.Count <= beacon.Reach);

    /// <summary>The beacons a ship on those legs flies past.</summary>
    public IEnumerable<Beacon> BeaconsOn(List<Leg> legs) =>
        Beacons.Where(b => legs.Any(l => l.Sector == b.Sector && Geometry3D.GetClosestPointToSegment(b.At, l.From, l.To).DistanceTo(b.At) <= b.Heard));

    /// <summary>One straight stretch of a voyage, inside a sector.</summary>
    public sealed class Leg
    {
        public string Sector;
        public Vector3 From, To;
        public double Seconds;
        /// <summary>In a gate's tunnel after it.</summary>
        public double Transit;
        /// <summary>The most it goes here (0: as fast as it can), and which way it faces (none: where it goes).</summary>
        public float Cap;
        public Vector3 Facing;
        /// <summary>Out in the open: it keeps clear of any station but this one.</summary>
        public bool Open;
        public string Own;
    }

    /// <summary>The pad of that dock and number, or null.</summary>
    public static Pad PadOf(Station station, int dock, int number) => station.Pads.FirstOrDefault(p => p.Dock == dock && p.Number == number);

    private readonly Dictionary<ShipModel, Aabb> _bounds = new();

    private Aabb BoundsOf(ShipModel model)
    {
        if (!_bounds.TryGetValue(model, out Aabb bounds))
        {
            if (model.Hull?.Exterior?.Instantiate() is Node3D exterior)
            {
                bounds = Xpace.Ship.ShipPart.Bounds(exterior);
                exterior.Free();
            }
            _bounds[model] = bounds;
        }
        return bounds;
    }

    /// <summary>The ship fits on the pad.</summary>
    public bool Fits(Pad pad, ShipModel model)
    {
        Vector3 size = BoundsOf(model).Size;
        return size.X <= pad.Footprint.X && size.Z <= pad.Footprint.Y && size.Y <= pad.Height;
    }

    /// <summary>Where the ship stands on the pad.</summary>
    public Vector3 Rest(Pad pad, ShipModel model) => pad.Pose * new Vector3(0, pad.Clearance - BoundsOf(model).Position.Y, 0);

    private static Vector3 Nose(Pad pad) => -pad.Pose.Basis.Z.Normalized();

    private Leg Stretch(string sector, Vector3 a, Vector3 b, ShipModel model, int tonnes, float cap = 0, double transit = 0, Vector3 facing = default)
    {
        float mass = model.Hull.Mass + tonnes * Xpace.Ship.CargoHold.KilogramsPerTonne;
        float thrust = model.Engine?.RatedThrust ?? model.Hull.MainThrust;
        return new Leg
        {
            Sector = sector, From = a, To = b, Transit = transit, Cap = cap, Facing = facing,
            Seconds = Voyage.Seconds(Space[sector], a, b, model.Hull, mass, model.Generator, thrust, cap),
        };
    }

    /// <summary>Where a ship coming from <paramref name="from"/> asks a station for a pad.</summary>
    public static Vector3 Contact(Station station, Vector3 from, ShipModel model) => station.At + (from - station.At).Normalized() * model.AskAtMeters;

    /// <summary>
    /// From a pad to where it asks the next station for one: it waits for the door, lifts off, goes out of the hangar and
    /// across the gates on the way. With no pad (none was free where it started) it sets out from the station.
    /// </summary>
    public List<Leg> Departure(Station from, Pad pad, Station to, ShipModel model, int tonnes, out Vector3 contact)
    {
        var legs = new List<Leg>();
        Vector3 at = from.At;
        if (pad != null)
        {
            Vector3 rest = Rest(pad, model), hover = rest + pad.Pose.Basis.Y.Normalized() * pad.Lift;
            legs.Add(new Leg { Sector = from.Sector, From = rest, To = rest, Seconds = model.DoorSeconds, Facing = Nose(pad) });
            legs.Add(Stretch(from.Sector, rest, hover, model, tonnes, model.PadSpeed, 0, Nose(pad)));
            legs.Add(Stretch(from.Sector, hover, pad.Inside, model, tonnes, model.HangarSpeed));
            legs.Add(Stretch(from.Sector, pad.Inside, pad.Door, model, tonnes, model.HangarSpeed));
            at = pad.Door;
        }
        string sector = from.Sector;
        foreach (Gate gate in Way(from.Sector, to.Sector) ?? new List<Gate>())
        {
            legs.Add(Stretch(sector, at, gate.At, model, tonnes, 0, gate.TransitSeconds));
            legs[^1].Open = true;
            legs[^1].Own = from.Id;
            sector = gate.Leads;
            at = Gates.FirstOrDefault(g => g.Sector == sector && g.Name == gate.Exit)?.At ?? Vector3.Zero;
        }
        contact = Contact(to, at, model);
        legs.Add(Stretch(sector, at, contact, model, tonnes));
        legs[^1].Open = true;
        legs[^1].Own = from.Id;
        return legs;
    }

    /// <summary>From where it asked to its pad: to the door, into the hangar, over the pad, and down.</summary>
    public List<Leg> Landing(Station to, Pad pad, Vector3 contact, ShipModel model, int tonnes)
    {
        Vector3 rest = Rest(pad, model), hover = rest + pad.Pose.Basis.Y.Normalized() * pad.Lift;
        return new List<Leg>
        {
            Stretch(to.Sector, contact, pad.Door, model, tonnes),
            Stretch(to.Sector, pad.Door, pad.Inside, model, tonnes, model.HangarSpeed),
            Stretch(to.Sector, pad.Inside, hover, model, tonnes, model.HangarSpeed),
            Stretch(to.Sector, hover, rest, model, tonnes, model.PadSpeed, 0, Nose(pad)),
        };
    }

    /// <summary>How long a voyage between two stations takes this ship with so many tonnes aboard, pad to pad, with the crew's handling.</summary>
    public double Seconds(Station from, Station to, ShipModel model, int tonnes)
    {
        Pad start = from.Pads.FirstOrDefault(), end = to.Pads.FirstOrDefault();
        double seconds = model.HandlingSeconds + Departure(from, start, to, model, tonnes, out Vector3 contact).Sum(l => l.Seconds + l.Transit);
        return end != null ? seconds + Landing(to, end, contact, model, tonnes).Sum(l => l.Seconds) : seconds;
    }

    /// <summary>Where a ship is, seen from outside.</summary>
    public readonly record struct Fix(string Sector, Vector3 At, Vector3 Heading, float Speed);

    /// <summary>Where a ship on those legs is so long after it set out; null while it is in a gate's tunnel.</summary>
    public Fix? Where(List<Leg> legs, ShipModel model, int tonnes, double since)
    {
        float mass = model.Hull.Mass + tonnes * Xpace.Ship.CargoHold.KilogramsPerTonne;
        float thrust = model.Engine?.RatedThrust ?? model.Hull.MainThrust;
        double t = since;
        foreach (Leg leg in legs)
        {
            if (t < leg.Seconds)
            {
                Vector3 way = leg.To == leg.From ? leg.Facing : (leg.To - leg.From).Normalized();
                (Vector3 at, float speed) = leg.To == leg.From ? (leg.To, 0f) : Voyage.Along(Space[leg.Sector], leg.From, leg.To, model.Hull, mass, model.Generator, thrust, Math.Max(0, t), leg.Cap);
                return new Fix(leg.Sector, leg.Open ? Clear(leg, at) : at, leg.Facing != default ? leg.Facing : way, speed);
            }
            t -= leg.Seconds;
            if (t < leg.Transit)
                return null;
            t -= leg.Transit;
        }
        Leg last = legs[^1];
        return new Fix(last.Sector, last.To, last.Facing != default ? last.Facing : (last.To - last.From).Normalized(), 0);
    }

    // Out in the open a line can cross a station: the ship goes over it instead
    private Vector3 Clear(Leg leg, Vector3 at)
    {
        foreach (Station station in Stations.Where(s => s.Sector == leg.Sector && s.Id != leg.Own))
        {
            float off = station.At.DistanceTo(at), reach = station.Radius * 1.5f;
            if (off < reach)
                at += Vector3.Up * reach * Mathf.Cos(off / reach * Mathf.Pi / 2);
        }
        return at;
    }

    /// <summary>Where a ship's voyage started: a station, or a point in space (a station of no id).</summary>
    public Station Origin(CargoShipRecord ship) => Find(ship.From) ??
        (string.IsNullOrEmpty(ship.FromSector) ? null : new Station { Id = "", Sector = ship.FromSector, At = new Vector3((float)ship.FromX, (float)ship.FromY, (float)ship.FromZ) });

    /// <summary>The way of a ship's voyage, at its pace: that of the ship it goes with, if any.</summary>
    public List<Leg> Way(CargoShipRecord ship, ShipModel model, out Vector3 contact)
    {
        contact = default;
        if (Origin(ship) is not Station from || Find(ship.Going) is not Station to)
            return null;
        (ShipModel pace, int tonnes) = Pace(ship, model);
        return Departure(from, PadOf(from, ship.LeftDock, ship.LeftPad), to, pace, tonnes, out contact);
    }

    /// <summary>The model and load a ship flies its way at.</summary>
    public static (ShipModel Model, int Tonnes) Pace(CargoShipRecord ship, ShipModel model) =>
        !string.IsNullOrEmpty(ship.PaceModel) && ResourceLoader.Exists(ship.PaceModel) && GD.Load<ShipModel>(ship.PaceModel) is ShipModel pace ? (pace, ship.PaceTonnes) : (model, ship.Tonnes);

    /// <summary>Where a cargo ship is now, whatever it is doing (null: in a tunnel, or nowhere known).</summary>
    public Fix? FixOf(CargoShipRecord ship, ShipModel model, double clock)
    {
        switch (ship.Phase)
        {
            case CargoShipRecord.Flying:
                (ShipModel pace, int load) = Pace(ship, model);
                return Way(ship, model, out _) is List<Leg> legs ? Where(legs, pace, load, clock - ship.Left) : null;
            case CargoShipRecord.Landing:
                return Find(ship.Going) is Station there && PadOf(there, ship.Dock, ship.Pad) is Pad pad
                    ? Where(Landing(there, pad, ship.ContactPoint, model, ship.Tonnes), model, ship.Tonnes, clock - ship.Left) : null;
            case CargoShipRecord.Holding:
                return Find(ship.Going) is Station waits ? new Fix(waits.Sector, ship.ContactPoint, (waits.At - ship.ContactPoint).Normalized(), 0) : null;
            default:
                if (Find(ship.At) is not Station here)
                    return null;
                if (PadOf(here, ship.Dock, ship.Pad) is Pad parked)
                    return new Fix(here.Sector, Rest(parked, model), Nose(parked), 0);
                return null;
        }
    }
}
