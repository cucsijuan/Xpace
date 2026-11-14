using System;
using System.Threading.Tasks;
using Godot;
using Xpace.Space;

namespace Xpace.Tests;

/// <summary>
/// Heat seeker heads against flares, the same missile with each kind of head, fired at a drone crossing 2 km off:
///
///   - colours: a flare burns hotter than a plume, a warm hull is cooler (Planck at the two bands);
///   - every head kills a drone that drops no flares;
///   - against a drone that breaks and drops flares, a two-colour head (it weighs out what glows another colour) and an
///     imaging head (it keeps to the blob where its target should be) kill more often than a plain one, which follows
///     the centre of all the heat in its cone;
///   - a cooler flare, the colour of a plume, fools a two-colour head again.
///
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/seeker_test.tscn
/// Exits with 0 if every check passed.
/// </summary>
public partial class SeekerTest : Node3D
{
    private const int Trials = 6;
    private const float Range = 2000, Lethal = 100;
    private int _failures;
    private Drone _drone;
    private MissileSpec _stock;

    public override async void _Ready()
    {
        AddChild(new Projectiles());
        _stock = GD.Load<MissileSpec>("res://assets/weapons/missiles/ir_1.tres");
        float plume = Heat.Index(1, Heat.PlumeShare), flare = Heat.Index(1, Heat.Share(Heat.FlareKelvin)), hull = Heat.Index(1, Heat.HullShare);
        Check($"a flare is bluer than a plume, a warm hull redder (ln short/long: flare {flare:0.00}, plume {plume:0.00}, hull {hull:0.00})",
              flare > plume + 0.5f && hull < plume - 1);

        _drone = new Drone { Straight = true, Speed = 120, Health = 1e9f, Position = new Vector3(0, 0, -Range) };
        AddChild(_drone);
        await Seconds(0.5);

        GD.Print("     head        flares   kills   on a flare");
        int sumClean = await Trials_(SeekerKind.Sum, flares: false);
        int colourClean = await Trials_(SeekerKind.TwoColour, flares: false);
        int imagingClean = await Trials_(SeekerKind.Imaging, flares: false);
        Check($"every head kills a drone with no flares (plain {sumClean}, two-colour {colourClean}, imaging {imagingClean} of {Trials})",
              Math.Min(sumClean, Math.Min(colourClean, imagingClean)) >= Trials - 1);

        int sum = await Trials_(SeekerKind.Sum, flares: true);
        int colour = await Trials_(SeekerKind.TwoColour, flares: true);
        int imaging = await Trials_(SeekerKind.Imaging, flares: true);
        Check($"against flares a two-colour head does better than a plain one ({colour} against {sum} of {Trials})", colour > sum);
        Check($"and so does an imaging head ({imaging} against {sum} of {Trials})", imaging > sum);

        int fooled = await Trials_(SeekerKind.TwoColour, flares: true, flareKelvin: Heat.PlumeKelvin);
        Check($"a flare the colour of a plume fools the two-colour head again ({fooled} against {colour} of {Trials})", fooled < colour);

        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private async Task<int> Trials_(SeekerKind kind, bool flares, float flareKelvin = Heat.FlareKelvin)
    {
        var spec = (MissileSpec)_stock.Duplicate();
        spec.SeekerKind = kind;
        var flare = (FlareSpec)GD.Load<FlareSpec>("res://assets/weapons/decoys/flare_1.tres").Duplicate();
        flare.Kelvin = flareKelvin;
        PackedScene flareScene = GD.Load<PackedScene>("res://scenes/weapons/flare.tscn");
        int kills = 0, seduced = 0;
        for (int trial = 0; trial < Trials; trial++)
        {
            _drone.Defends = flares;
            _drone.Evades = true;
            _drone.ChaffCharges = 0;
            _drone.FlareCharges = flares ? 6 : 0;
            _drone.FlareScene = flareScene;
            _drone.Rearm();
            _drone.GlobalPosition = new Vector3(-0.1f * Range, 0.2f * Range, -Range);
            _drone.LookAt(_drone.GlobalPosition + Vector3.Right, Vector3.Up);
            _drone.Fly(Vector3.Right * _drone.Speed);
            await Seconds(0.2);

            var m = GD.Load<PackedScene>("res://scenes/weapons/ir_missile.tscn").Instantiate<Missile>();
            m.Spec = spec;
            m.Shooter = this;
            m.Target = _drone;
            AddChild(m);
            Vector3 to = (_drone.GlobalPosition - Vector3.Zero).Normalized();
            m.LookAt(to * 10, Vector3.Up);
            m.Velocity = to * 50;
            m.SeekerLook = to;
            bool onFlare = false;
            float dealt = 0;
            m.TreeExiting += () => dealt = m.DamageDealt;
            Flares(flare);
            for (int i = 0; i < 30 * 60 && IsInstanceValid(m) && m.Result == Missile.Outcome.Flying; i++)
            {
                onFlare |= m.SeekerSees is Flare;
                Flares(flare);
                await Seconds(1.0 / 60);
            }
            if (IsInstanceValid(m))
                dealt = m.DamageDealt;
            if (dealt >= Lethal)
                kills++;
            if (onFlare)
                seduced++;
            if (IsInstanceValid(m))
                m.QueueFree();
            await Seconds(3);                   // the flares burn out
        }
        GD.Print($"     {kind,-10}  {(flares ? flareKelvin.ToString("0") + " K" : "none"),-7}  {kills}/{Trials}   {seduced}");
        return kills;
    }

    // The drone's flares take this test's colour
    private void Flares(FlareSpec spec)
    {
        foreach (Node n in GetChildren())
            if (n is Flare f && f.Spec != spec)
                f.Spec = spec;
    }

    private async Task Seconds(double seconds)
    {
        for (int i = 0; i < Math.Max(1, (int)Math.Round(seconds * Engine.PhysicsTicksPerSecond)); i++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private void Check(string what, bool ok)
    {
        GD.Print($"{(ok ? "ok  " : "FAIL")} {what}");
        if (!ok)
            _failures++;
    }
}
