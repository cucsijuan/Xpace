using Godot;
using Xpace.Computer;
using Xpace.Space;

namespace Xpace.Ship;

/// <summary>
/// The countermeasures dispenser, bus slot 8 (180h): a load of chaff
/// bundles and a load of flares, thrown out behind the ship when the
/// program says so. Whatever it throws keeps the ship's velocity plus the
/// dispenser's kick (there is no air to slow it): it is the ship's turn
/// that parts the two. It takes <see cref="CycleSeconds"/> to bring the
/// next of a kind to the chute; asked sooner, it throws nothing.
///
/// Registers: +0 type 10  +2 chaff left  +3 flares left
/// +4 write: bit 0 throw a bundle of chaff, bit 1 a flare
/// +5 how many it has thrown, counting up (the program sees a throw by it)
/// </summary>
public partial class DispenserDevice : Node3D, IShipDevice
{
    public const int Slot = 8;
    public const byte Type = 10;

    [Export] public PackedScene ChaffScene;
    [Export] public PackedScene FlareScene;
    [Export] public int Chaff = 24;
    [Export] public int Flares = 24;
    [Export] public float ChaffKick = 15f;          // m/s
    [Export] public float FlareKick = 20f;
    [Export] public double CycleSeconds = 0.25;

    public int ChaffLeft { get; private set; }
    public int FlaresLeft { get; private set; }
    public int Thrown { get; private set; }

    private double _chaffWait, _flareWait;
    private readonly RandomNumberGenerator _rng = new() { Seed = 4231 };

    public override void _Ready()
    {
        ChaffScene ??= GD.Load<PackedScene>("res://scenes/weapons/chaff.tscn");
        FlareScene ??= GD.Load<PackedScene>("res://scenes/weapons/flare.tscn");
        ChaffLeft = Chaff;
        FlaresLeft = Flares;
    }

    /// <summary>A full load again (a test, a resupply).</summary>
    public void Rearm()
    {
        ChaffLeft = Chaff;
        FlaresLeft = Flares;
    }

    public void BeforeStep(ShipBody ship, double delta)
    {
        ShipBus bus = ship.Computer.Bus;
        bus[Slot, 0] = Type;
        bus[Slot, 1] = 1;
        bus[Slot, 2] = (byte)ChaffLeft;
        bus[Slot, 3] = (byte)FlaresLeft;
        bus[Slot, 5] = (byte)Thrown;
    }

    public void AfterStep(ShipBody ship, double delta)
    {
        _chaffWait -= delta;
        _flareWait -= delta;
        ShipBus bus = ship.Computer.Bus;
        if (!bus.TakeWritten(Slot, 4))
            return;
        int asked = bus[Slot, 4];
        if ((asked & 1) != 0 && ChaffLeft > 0 && _chaffWait <= 0)
        {
            ChaffLeft--;
            _chaffWait = CycleSeconds;
            Throw(ship, ChaffScene, ChaffKick);
        }
        if ((asked & 2) != 0 && FlaresLeft > 0 && _flareWait <= 0)
        {
            FlaresLeft--;
            _flareWait = CycleSeconds;
            Throw(ship, FlareScene, FlareKick);
        }
    }

    private void Throw(ShipBody ship, PackedScene scene, float kick)
    {
        Vector3 back = GlobalBasis.Z.Normalized();             // the chute points aft
        var scatter = new Vector3(_rng.Randfn(), _rng.Randfn(), _rng.Randfn()).Normalized() * 0.5f;
        Countermeasures.Drop(ship.GetParent(), scene, GlobalPosition + back * 2, ship.LinearVelocity + (back + scatter).Normalized() * kick);
        Thrown++;
    }
}
