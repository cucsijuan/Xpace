using Godot;
using Xpace.Computer;
using Xpace.Space;
using Xpace.World;

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
///
/// Its model is the one the hull comes with (<see cref="ShipSpec.Dispenser"/>) unless it is given its own. A player's
/// ship keeps its chaff and flares in the pilot's record (<see cref="Xpace.World.ShipRecord.Magazine"/>), bought at a
/// station's arms kiosk; any other ship, here.
/// </summary>
public partial class DispenserDevice : Node3D, IShipDevice, IRearmable
{
    public const int Slot = 8;
    public const byte Type = 10;

    [Export] public PackedScene ChaffScene;
    [Export] public PackedScene FlareScene;
    /// <summary>The model.</summary>
    [Export] public DispenserSpec Spec;
    private DispenserSpec S => Spec ?? _ship?.Spec?.Dispenser ?? (_default ??= new DispenserSpec());
    private DispenserSpec _default;

    /// <summary>What a full load is: the model's, unless a test or a fitting says otherwise.</summary>
    public int Chaff { get => _chaff ?? S.Chaff; set => _chaff = value; }
    public int Flares { get => _flares ?? S.Flares; set => _flares = value; }
    private int? _chaff, _flares;
    public float ChaffKick => S.ChaffKick;
    public float FlareKick => S.FlareKick;
    public double CycleSeconds => S.CycleSeconds;

    public int ChaffLeft => Left(_chaffSpec, _chaffLeft);
    public int FlaresLeft => Left(_flareSpec, _flaresLeft);
    public int Thrown { get; private set; }

    private ShipBody _ship;
    private ChaffSpec _chaffSpec;
    private FlareSpec _flareSpec;
    private int _chaffLeft, _flaresLeft;

    private double _chaffWait, _flareWait;
    private readonly RandomNumberGenerator _rng = new() { Seed = 4231 };

    public override void _Ready()
    {
        ChaffScene ??= GD.Load<PackedScene>("res://scenes/weapons/chaff.tscn");
        FlareScene ??= GD.Load<PackedScene>("res://scenes/weapons/flare.tscn");
        _ship = GetParentOrNull<ShipBody>();
        _chaffSpec = ModelOf(ChaffScene) as ChaffSpec;
        _flareSpec = ModelOf(FlareScene) as FlareSpec;
        _chaffLeft = Chaff;
        _flaresLeft = Flares;
    }

    private static Outfit ModelOf(PackedScene scene)
    {
        if (scene?.Instantiate() is not Node made)
            return null;
        Outfit item = (made as IDropped)?.Item;
        made.Free();
        return item;
    }

    /// <summary>A full load again: the insurer's new ship, or a test.</summary>
    public void Rearm()
    {
        _chaffLeft = Chaff;
        _flaresLeft = Flares;
    }

    private PilotRecord Pilot => _ship != null ? Universe.Current?.RecordOf(_ship) : null;

    // A pilot's ship: what their record has (a model not in it, a full load); any other, its own count
    private int Left(Outfit spec, int own) =>
        spec?.Id is string model && Pilot is PilotRecord pilot ? pilot.Ship.Rack(model, Universe.RackSize(pilot, spec)) : own;

    private void Spent(ShipBody ship, Outfit spec, ref int own)
    {
        if (spec?.Id != null && Pilot != null)
            Universe.Current.Fired(ship, spec);
        else
            own--;
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
        Dispense(ship, (asked & 1) != 0, (asked & 2) != 0);
    }

    /// <summary>Throws a bundle of chaff and/or a flare, if there are some left and the chute is ready.</summary>
    public void Dispense(ShipBody ship, bool chaff, bool flare)
    {
        if (chaff && ChaffLeft > 0 && _chaffWait <= 0)
        {
            Spent(ship, _chaffSpec, ref _chaffLeft);
            _chaffWait = CycleSeconds;
            Throw(ship, ChaffScene, ChaffKick);
        }
        if (flare && FlaresLeft > 0 && _flareWait <= 0)
        {
            Spent(ship, _flareSpec, ref _flaresLeft);
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
