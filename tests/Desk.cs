using System.Threading.Tasks;
using Godot;
using Xpace.Ship;

namespace Xpace.Tests;

/// <summary>Through the desktop, as a pilot goes: its logo gone, SELECT on the first application, Comms.</summary>
public static class Desk
{
    public static async Task OpenComms(Node test, DisplayDevice display)
    {
        for (int i = 0; i < 900 && display.Pixel("COMMS", 2, 2) != 1; i++)
            await test.ToSignal(test.GetTree(), SceneTree.SignalName.PhysicsFrame);
        for (int i = 0; i < 30; i++)
            await test.ToSignal(test.GetTree(), SceneTree.SignalName.PhysicsFrame);
        display.PressKeyOn("COMMS", DisplayKey.Select);
        for (int i = 0; i < 60 && (display.Lent("COMMS") != 'C' || !display.Text("COMMS").Contains("RADIO")); i++)
            await test.ToSignal(test.GetTree(), SceneTree.SignalName.PhysicsFrame);
    }
}
