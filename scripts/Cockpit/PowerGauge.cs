using System;
using Godot;
using Xpace.Ship;

namespace Xpace.Cockpit;

/// <summary>
/// The ship's power, shields and heat, on the head-up display (bottom right,
/// as in Elite): for systems, engines and weapons, four pips (a half pip
/// half lit) over how full its capacitor is; then the shield's strength
/// (dim while it is down and building back, blinking while collapsed) and
/// the heat, marked at 100% (red past it), with the heat sinks left. It
/// reads the devices themselves, as a gauge on the dashboard would.
/// </summary>
public partial class PowerGauge : Control
{
    [Export] public NodePath ShipPath = "../../..";
    [Export] public Color Colour = new(1, 0.6f, 0.15f, 0.9f);
    [Export] public Color ShieldColour = new(0.35f, 0.7f, 1, 0.9f);
    [Export] public Color HotColour = new(1, 0.2f, 0.15f, 0.95f);
    [Export] public int FontSize = 12;

    private PowerDevice _power;
    private ShieldDevice _shield;
    private HeatDevice _heat;
    private double _time;

    private static readonly string[] Names = { "SYS", "ENG", "WEP" };

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        foreach (Node n in GetNodeOrNull(ShipPath)?.GetChildren() ?? new Godot.Collections.Array<Node>())
        {
            _power ??= n as PowerDevice;
            _shield ??= n as ShieldDevice;
            _heat ??= n as HeatDevice;
        }
        Visible = _power != null || _shield != null || _heat != null;
    }

    public override void _Process(double delta)
    {
        _time += delta;
        QueueRedraw();
    }

    public override void _Draw()
    {
        Font font = ThemeDB.FallbackFont;
        float column = 34, pip = 7, gap = 3, top = FontSize + 4;
        float x = 0;
        if (_power != null)
        {
            for (int i = 0; i < 3; i++, x += column)
            {
                DrawString(font, new Vector2(x, FontSize), Names[i], HorizontalAlignment.Left, -1, FontSize, Colour);
                int halves = _power.Pips[i];
                for (int p = 0; p < 4; p++)
                {
                    var box = new Rect2(x, top + (3 - p) * (pip + gap), column - 10, pip);
                    DrawRect(box, new Color(Colour, 0.25f), false, 1);
                    int lit = Math.Clamp(halves - p * 2, 0, 2);
                    if (lit > 0)
                        DrawRect(new Rect2(box.Position, new Vector2(box.Size.X * lit / 2, box.Size.Y)), Colour);
                }
                Bar(new Rect2(x, top + 4 * (pip + gap) + 2, column - 10, 4), _power.Fill((PowerStore)i), Colour, null);
            }
            x += 6;
        }
        float height = top + 4 * (pip + gap) + 6;
        if (_shield != null)
        {
            bool blink = _shield.Collapsed && (int)(_time * 3) % 2 == 0;
            Color c = _shield.Collapsed ? new Color(ShieldColour, blink ? 0.9f : 0.3f) : _shield.Fraction < 1 ? new Color(ShieldColour, 0.7f) : ShieldColour;
            DrawString(font, new Vector2(x, FontSize), "SHD", HorizontalAlignment.Left, -1, FontSize, c);
            Upright(new Rect2(x + 6, top, 10, height - top), _shield.Fraction, c, null);
            x += column;
        }
        if (_heat != null)
        {
            Color c = _heat.Level > 1 ? HotColour : Colour;
            DrawString(font, new Vector2(x, FontSize), "HEAT", HorizontalAlignment.Left, -1, FontSize, c);
            Upright(new Rect2(x + 6, top, 10, height - top), _heat.Level / 1.5f, c, 1 / 1.5f);
            DrawString(font, new Vector2(x + 20, height), $"{_heat.SinksLeft}", HorizontalAlignment.Left, -1, FontSize, new Color(Colour, _heat.Sinking ? 1 : 0.6f));
        }
    }

    /// <summary>A bar filling to the right, with a mark at <paramref name="mark"/> (0 to 1).</summary>
    private void Bar(Rect2 r, float fill, Color c, float? mark)
    {
        DrawRect(r, new Color(c, 0.2f), false, 1);
        DrawRect(new Rect2(r.Position, new Vector2(r.Size.X * Math.Clamp(fill, 0, 1), r.Size.Y)), c);
        if (mark is float m)
            DrawLine(new Vector2(r.Position.X + r.Size.X * m, r.Position.Y - 2), new Vector2(r.Position.X + r.Size.X * m, r.End.Y + 2), c, 1);
    }

    /// <summary>A bar filling upwards, with a mark at <paramref name="mark"/> (0 to 1).</summary>
    private void Upright(Rect2 r, float fill, Color c, float? mark)
    {
        DrawRect(r, new Color(c, 0.2f), false, 1);
        float h = r.Size.Y * Math.Clamp(fill, 0, 1);
        DrawRect(new Rect2(r.Position.X, r.End.Y - h, r.Size.X, h), c);
        if (mark is float m)
            DrawLine(new Vector2(r.Position.X - 3, r.End.Y - r.Size.Y * m), new Vector2(r.End.X + 3, r.End.Y - r.Size.Y * m), Colors.White, 1);
    }
}
