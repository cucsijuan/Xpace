using System.Text;
using Xpace.Ship;

namespace Xpace.Tests;

/// <summary>
/// What FIRECTL is doing, read from the resident program along the IRQ 5
/// chain: its page on the WEAPON screen is a picture, not text.
/// </summary>
public static class FireControl
{
    private static readonly string[] Weapons = { "GUN", "MSL", "IR", "LSR" };

    /// <summary>The program's segment times 16, or -1 if it is not resident.</summary>
    private static unsafe int Find(ShipBody ship)
    {
        byte* m = ship.Computer.Memory;
        int vector = (8 + 5) * 4;
        int offset = m[vector] | m[vector + 1] << 8, segment = m[vector + 2] | m[vector + 3] << 8;
        for (int i = 0; i < 8; i++)
        {
            int at = segment * 16;
            if (m[at + 0x103] != 'X' || m[at + 0x104] != 'P')
                return -1;
            if (m[at + 0x105] == 'F' && m[at + 0x106] == 'C')
                return at;
            offset = m[at + 0x109] | m[at + 0x10A] << 8;
            segment = m[at + 0x10B] | m[at + 0x10C] << 8;
        }
        return -1;
    }

    public static unsafe bool Running(ShipBody ship) => ship.Computer != null && ship.Computer.Memory != null && Find(ship) >= 0;

    /// <summary>The weapon selected, as the page names it; "" if it is not running.</summary>
    public static unsafe string Weapon(ShipBody ship)
    {
        int at = Find(ship);
        return at < 0 ? "" : Weapons[ship.Computer.Memory[at + 0x10D] & 3];
    }

    /// <summary>The word at the bottom of the page; "" for none.</summary>
    public static unsafe string Status(ShipBody ship)
    {
        int at = Find(ship);
        if (at < 0)
            return "";
        byte* m = ship.Computer.Memory;
        int word = m[at + 0x10F] | m[at + 0x110] << 8;
        if (word == 0)
            return "";
        var text = new StringBuilder();
        for (byte* c = m + at + word; *c != 0; c++)
            text.Append((char)*c);
        return text.ToString();
    }

    /// <summary>% of the processor it took, last time it drew.</summary>
    public static unsafe int Load(ShipBody ship)
    {
        int at = Find(ship);
        return at < 0 ? 0 : ship.Computer.Memory[at + 0x10E];
    }

    /// <summary>How many pixels of that value there are in the rows from y0 to y1 of the page.</summary>
    public static int Count(DisplayDevice display, int value, int y0, int y1)
    {
        int count = 0;
        for (int y = y0; y <= y1; y++)
            for (int x = 0; x < 112; x++)
                if (display.Pixel("WEAPON", x, y) == value)
                    count++;
        return count;
    }

    /// <summary>The page is up: the weapon's name is on the screen, in graphics.</summary>
    public static bool PageDrawn(DisplayDevice display) => Count(display, 2, 1, 58) > 500;
}
