using System;
using System.Threading.Tasks;
using Godot;
using Xpace.Computer;
using Xpace.Ship;
using Xpace.Space;

namespace Xpace.Tests;

/// <summary>
/// The radar warning receiver and RWR.COM: radars round the ship, some
/// searching, one locked on it, one of a type the library does not know,
/// one looking the other way and one too far to hear. Checks what the
/// receiver makes of them (bearing, range guessed, type, lock) and what
/// RWR.COM draws: a circle for a search, a diamond for a lock, at the
/// right place; a radar that goes quiet leaves the screen.
/// Run: Godot --headless --fixed-fps 60 --path . res://tests/rwr_test.tscn
/// Prints the table, saves the screen as user://rwr_test.png; exits with 0
/// if the checks passed.
/// </summary>
public partial class RwrTest : Node3D
{
    private ShipBody _ship;
    private RwrDevice _rwr;
    private DisplayDevice _display;
    private int _failures;

    private sealed record Case(string Name, string Signature, float BearingDegrees, float Range, bool LocksUs, bool FacesUs, bool Heard, char Letter);

    private static readonly Case[] Cases =
    {
        new("drone searching", "D", 45, 3000, false, true, true, 'D'),
        new("fighter locked", "F", -120, 6000, true, true, true, 'F'),
        new("unknown searching", "Q", 180, 2000, false, true, true, 'U'),
        new("drone looking away", "D", -60, 2000, false, false, false, 'D'),
        new("drone too far", "D", 10, 40000, false, true, false, 'D'),
    };

    private readonly RadarEmitter[] _emitters = new RadarEmitter[Cases.Length];

    public override async void _Ready()
    {
        AddChild(new Projectiles());
        _ship = new ShipBody();
        _ship.AddChild(new ShipComputer { Name = "ShipComputer" });
        _ship.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(5, 3, 10) } });
        _display = new DisplayDevice { Screens = DisplayScreens() };
        _ship.AddChild(_display);
        _ship.AddChild(new TurretDevice { Name = "Turret", Position = new Vector3(0.9f, 0.8f, -3f) });
        _ship.AddChild(new RadarDevice { Name = "Radar", Position = new Vector3(0, 0.5f, -5f), TrackOriginPath = "../Turret" });
        _rwr = new RwrDevice { Name = "Rwr" };
        _ship.AddChild(_rwr);
        AddChild(_ship);

        for (int i = 0; i < Cases.Length; i++)
        {
            Case c = Cases[i];
            float b = Mathf.DegToRad(c.BearingDegrees);
            var host = new Node3D { Position = new Vector3(MathF.Sin(b), 0.05f, -MathF.Cos(b)) * c.Range };
            AddChild(host);
            host.LookAt(c.FacesUs ? Vector3.Zero : host.Position * 2, Vector3.Up);
            _emitters[i] = new RadarEmitter
            {
                Signature = c.Signature,
                PowerAt1Km = c.Signature == "F" ? 4e4f : 1e4f,
                LockRange = c.LocksUs ? 10000 : 0,
                Target = _ship,
            };
            host.AddChild(_emitters[i]);
        }

        Check("RWR.COM is resident", await Until(() => _ship.Computer.ScreenText().Contains("Threat warning resident"), 40));

        // How often each one is heard: a search is a ping now and then, a lock is steady
        var heardSteps = new int[Cases.Length];
        int steps = 0;
        for (; steps < 6 * 60; steps++)
        {
            await Seconds(1.0 / 60);
            for (int i = 0; i < Cases.Length; i++)
                if (Find(_emitters[i])?.HeardNow == true)
                    heardSteps[i]++;
        }
        await Seconds(0.5);                       // RWR.COM looks at every line in about 0.27 s

        GD.Print("     emitter               heard   bearing (true / seen)   range (true / guessed)   type   lock   on screen");
        for (int i = 0; i < Cases.Length; i++)
        {
            Case c = Cases[i];
            RwrDevice.Contact contact = Find(_emitters[i]);
            if (contact == null)
            {
                GD.Print($"     {c.Name,-20}  -");
                Check($"{c.Name}: not heard", !c.Heard);
                continue;
            }
            float bearing = Mathf.RadToDeg(contact.Bearing);
            float bearingError = MathF.Abs(Mathf.Wrap(bearing - c.BearingDegrees, -180, 180));
            float rangeError = MathF.Abs(contact.Range / c.Range - 1);
            string symbol = SymbolAt(contact, out int px, out int py);
            GD.Print($"     {c.Name,-20}  {100.0 * heardSteps[i] / steps,4:0}%   {c.BearingDegrees,6:0} / {bearing,6:0.0}         {c.Range,6:0} / {contact.Range,6:0}            {contact.Letter}      {(contact.Locked ? "yes" : "no "),-3}   {symbol} at ({px}, {py})");
            Check($"{c.Name}: heard", c.Heard);
            Check($"{c.Name}: bearing within 15 degrees ({bearingError:0.0})", bearingError < 15);
            Check($"{c.Name}: range within 35% ({rangeError * 100:0}%)", rangeError < 0.35f);
            Check($"{c.Name}: type {c.Letter} ({contact.Letter})", contact.Letter == c.Letter);
            Check($"{c.Name}: locked {c.LocksUs} ({contact.Locked})", contact.Locked == c.LocksUs);
            Check($"{c.Name}: a {(c.LocksUs ? "diamond" : "circle")} on the screen ({symbol})", symbol == (c.LocksUs ? "diamond" : "circle"));
            if (c.LocksUs)
                Check($"{c.Name}: a lock is heard all the time ({100.0 * heardSteps[i] / steps:0}%)", heardSteps[i] > steps * 0.95);
            else
                Check($"{c.Name}: a search is heard only as the beam passes ({100.0 * heardSteps[i] / steps:0}%)", heardSteps[i] > 0 && heardSteps[i] < steps * 0.1);
        }
        Check("the lock warning is on", _rwr.LockWarning && (_ship.Computer.Bus[RwrDevice.Slot, 4] & 1) != 0);
        SavePicture("user://rwr_test.png");

        // The ship turns 90 degrees right: everything moves 90 degrees left
        var before = Find(_emitters[0]).Bearing;
        _ship.Basis = new Basis(Vector3.Up, -Mathf.Pi / 2);
        await Seconds(3);
        float turned = Mathf.RadToDeg(Mathf.Wrap(Find(_emitters[0]).Bearing - before, -Mathf.Pi, Mathf.Pi));
        Check($"the ship turns 90 right, the drone moves 90 left ({turned:0})", MathF.Abs(turned + 90) < 15);
        _ship.Basis = Basis.Identity;
        await Seconds(3);

        // The fighter's radar goes off: the lock goes at once, the symbol a while later
        RwrDevice.Contact fighter = Find(_emitters[1]);
        _emitters[1].On = false;
        await Seconds(_rwr.LockHoldSeconds + 0.1);
        Check("the radar off: the lock warning goes", !_rwr.LockWarning && !fighter.Locked);
        await Seconds(_rwr.HoldSeconds + 0.5);
        Check("and after a while the fighter leaves the table", Find(_emitters[1]) == null);
        Check("and the screen", CountBright(fighter) == 0);

        GD.Print(_failures == 0 ? "ALL PASSED" : $"{_failures} FAILED");
        GetTree().Quit(_failures == 0 ? 0 : 1);
    }

    private RwrDevice.Contact Find(RadarEmitter e)
    {
        foreach (RwrDevice.Contact c in _rwr.Contacts)
            if (c.Emitter == e)
                return c;
        return null;
    }

    // ---------- the screen ----------

    private const int Size = 128, Radius = Size / 2 - 1, EdgeRange = 8000, Half = 6;

    private static readonly ushort[] Circle = { 0x0F80, 0x3060, 0x6030, 0x4010, 0x8008, 0x8008, 0x8008, 0x8008, 0x8008, 0x4010, 0x6030, 0x3060, 0x0F80 };
    private static readonly ushort[] Diamond = { 0x0200, 0x0500, 0x0880, 0x1040, 0x2020, 0x4010, 0x8008, 0x4010, 0x2020, 0x1040, 0x0880, 0x0500, 0x0200 };

    /// <summary>Where RWR.COM puts a contact (as it works it out, near enough).</summary>
    private static (int x, int y) Expected(RwrDevice.Contact c)
    {
        int r = (int)(Math.Min(c.Range, EdgeRange) * Radius / EdgeRange);
        r = Math.Clamp(r, Half + 3, Radius - Half - 1);
        return (Size / 2 + (int)MathF.Round(r * MathF.Sin(c.Bearing)), Size / 2 - (int)MathF.Round(r * MathF.Cos(c.Bearing)));
    }

    private unsafe int Pixel(int x, int y)
    {
        if (x < 0 || y < 0 || x >= Size || y >= Size)
            return 0;
        byte* screen = Screen();
        return (screen[y * Size / 4 + x / 4] >> (6 - 2 * (x & 3))) & 3;
    }

    private unsafe byte* Screen()
    {
        byte* memory = _ship.Computer.Memory + ShipMemory.Base;
        for (int i = 0; i < _ship.Computer.Bus[DisplayDevice.Slot, 6]; i++)
        {
            byte* e = memory + ShipMemory.Screens + i * DisplayDevice.EntrySize;
            if (e[0] == 'R' && e[1] == 'W' && e[2] == 'R' && e[3] == ' ')
                return memory + *(ushort*)(e + 14);
        }
        throw new InvalidOperationException("no RWR screen");
    }

    /// <summary>The shape found near where the contact should be: circle, diamond or nothing.</summary>
    private string SymbolAt(RwrDevice.Contact c, out int foundX, out int foundY)
    {
        (int x0, int y0) = Expected(c);
        foundX = x0;
        foundY = y0;
        for (int dy = -3; dy <= 3; dy++)
            for (int dx = -3; dx <= 3; dx++)
            {
                foreach ((string name, ushort[] shape) in new[] { ("diamond", Diamond), ("circle", Circle) })
                    if (Matches(shape, x0 + dx, y0 + dy))
                    {
                        foundX = x0 + dx;
                        foundY = y0 + dy;
                        return name;
                    }
            }
        return "nothing";
    }

    // Every pixel of the outline lit (XORed over a dim ring it is still half bright);
    // the two outlines differ enough that one never fits the other
    private bool Matches(ushort[] shape, int x, int y)
    {
        for (int row = 0; row < 13; row++)
            for (int col = 0; col < 13; col++)
                if ((shape[row] & (0x8000 >> col)) != 0 && Pixel(x - Half + col, y - Half + row) < 2)
                    return false;
        return true;
    }

    private int CountBright(RwrDevice.Contact c)
    {
        (int x0, int y0) = Expected(c);
        int n = 0;
        for (int y = y0 - 9; y <= y0 + 9; y++)
            for (int x = x0 - 9; x <= x0 + 9; x++)
                if (Pixel(x, y) == 3)
                    n++;
        return n;
    }

    private void SavePicture(string path)
    {
        var image = Image.CreateEmpty(Size * 2, Size * 2, false, Image.Format.Rgb8);
        for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float v = Pixel(x, y) / 3f;
                var colour = new Color(0.35f * v, v, 0.4f * v);
                image.FillRect(new Rect2I(x * 2, y * 2, 2, 2), colour);
            }
        image.SavePng(path);
        GD.Print($"     the screen: {ProjectSettings.GlobalizePath(path)}");
    }

    // ---------- the rest ----------

    private async Task Seconds(double seconds)
    {
        for (int i = 0; i < Math.Max(1, (int)Math.Round(seconds * Engine.PhysicsTicksPerSecond)); i++)
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
    }

    private async Task<bool> Until(Func<bool> done, double seconds)
    {
        for (int i = 0; i < seconds * Engine.PhysicsTicksPerSecond; i++)
        {
            if (done())
                return true;
            await ToSignal(GetTree(), SceneTree.SignalName.PhysicsFrame);
        }
        GD.Print(_ship.Computer.ScreenText());
        return false;
    }

    private void Check(string what, bool ok)
    {
        GD.Print($"{(ok ? "ok  " : "FAIL")} {what}");
        if (!ok)
            _failures++;
    }

    private static Godot.Collections.Array<DisplayScreen> DisplayScreens() => new()
    {
        GD.Load<DisplayScreen>("res://assets/ship/displays/flight.tres"),
        GD.Load<DisplayScreen>("res://assets/ship/displays/radar.tres"),
        GD.Load<DisplayScreen>("res://assets/ship/displays/weapon.tres"),
        GD.Load<DisplayScreen>("res://assets/ship/displays/rwr.tres"),
    };
}
