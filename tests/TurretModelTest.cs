using System;
using System.Threading.Tasks;
using Godot;
using Xpace.Computer;
using Xpace.Ship;
using Xpace.Space;

namespace Xpace.Tests;

/// <summary>
/// A turret model from its spec: it turns and elevates as the bus asks, its
/// muzzle looks where the angles say, rounds leave from the barrels' end, and
/// the barrels spin while it fires. A turret with no model still works.
///
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/turret_model_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class TurretModelTest : Node3D
{
    private int _failures;

    public override async void _Ready()
    {
        var rounds = new Projectiles();
        AddChild(rounds);
        var ship = new ShipBody();
        ship.AddChild(new ShipComputer { Name = "ShipComputer", PowerOnAtStart = false });
        var turret = new TurretDevice { Name = "Turret", Position = new Vector3(1, 2, -3), Spec = GD.Load<TurretSpec>("res://assets/weapons/turrets/gun_1.tres") };
        ship.AddChild(turret);
        AddChild(ship);
        await Seconds(0.1);
        var rig = turret.GetNodeOrNull<TurretRig>("GatlingT1");
        Check("the spec's model is on the turret", rig != null && turret.Muzzle.GetParent() == rig.Cradle);
        Check("the Gatling's meshes are under it", rig.Cradle.GetChildCount() >= 2);

        ShipBus bus = ship.Computer.Bus;
        foreach (var (az, el) in new[] { (0f, 0f), (40f, 20f), (-90f, 60f), (150f, 5f) })
        {
            for (int i = 0; i < 200; i++)
            {
                bus.SetWord(TurretDevice.Slot, 2, (int)(az / 360 * 65536));
                bus.SetWord(TurretDevice.Slot, 4, (int)(el / 360 * 65536));
                await Seconds(1.0 / 60);
            }
            float a = Mathf.DegToRad(az), e = Mathf.DegToRad(el);
            var wanted = new Vector3(Mathf.Sin(a) * Mathf.Cos(e), Mathf.Sin(e), -Mathf.Cos(a) * Mathf.Cos(e));
            Vector3 looks = -turret.Muzzle.GlobalBasis.Z.Normalized();
            float off = Mathf.RadToDeg(looks.AngleTo(wanted));
            Check($"asked for {az:0} right, {el:0} up, the muzzle looks there ({off:0.00} degrees off)", off < 0.5f);
        }

        Vector3 hub = turret.GlobalPosition;
        float reach = turret.Muzzle.GlobalPosition.DistanceTo(hub);
        Check($"rounds leave from the barrels' end ({reach:0.00} m from the mount)", reach is > 1 and < 4);

        Check("a round's streak grows out of the muzzle, not from behind it",
              Projectiles.StreakLength(0, 1200, 28) < 1 && Mathf.IsEqualApprox(Projectiles.StreakLength(0.01f, 1200, 28), 12) && Projectiles.StreakLength(1, 1200, 28) == 28);

        var barrel = rig.GetNode<Node3D>(rig.BarrelPath);
        Basis before = barrel.Basis;
        for (int i = 0; i < 40; i++)
        {
            bus.Out(TurretDevice.Slot, 12, 1);
            await Seconds(1.0 / 60);
        }
        Check($"firing, it fires ({turret.RoundsFired} rounds)", turret.RoundsFired > 3);
        Check("and the barrels spin", !barrel.Basis.IsEqualApprox(before));

        var plain = new TurretDevice { Name = "Plain" };
        ship.AddChild(plain);
        await Seconds(0.1);
        Check("a turret with no model still has a muzzle", plain.Muzzle != null);

        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private async Task Seconds(double s)
    {
        for (int i = 0; i < Math.Max(1, (int)Math.Round(s * Engine.PhysicsTicksPerSecond)); i++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private void Check(string what, bool ok)
    {
        GD.Print($"{(ok ? "ok  " : "FAIL")} {what}");
        if (!ok)
            _failures++;
    }
}
