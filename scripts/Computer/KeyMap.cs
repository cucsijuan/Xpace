using System.Collections.Generic;
using Godot;

namespace Xpace.Computer;

/// <summary>
/// Godot's physical keys to the key names pc86's keyboard understands
/// (the browser's KeyboardEvent.code names).
/// </summary>
public static class KeyMap
{
    private static readonly Dictionary<Key, string> Names = new()
    {
        [Key.Escape] = "Escape", [Key.Minus] = "Minus", [Key.Equal] = "Equal",
        [Key.Backspace] = "Backspace", [Key.Tab] = "Tab", [Key.Enter] = "Enter",
        [Key.Bracketleft] = "BracketLeft", [Key.Bracketright] = "BracketRight",
        [Key.Semicolon] = "Semicolon", [Key.Apostrophe] = "Quote", [Key.Quoteleft] = "Backquote",
        [Key.Backslash] = "Backslash", [Key.Comma] = "Comma", [Key.Period] = "Period",
        [Key.Slash] = "Slash", [Key.Space] = "Space", [Key.Capslock] = "CapsLock",
        [Key.Numlock] = "NumLock", [Key.Scrolllock] = "ScrollLock",
        [Key.KpMultiply] = "NumpadMultiply", [Key.KpSubtract] = "NumpadSubtract",
        [Key.KpAdd] = "NumpadAdd", [Key.KpPeriod] = "NumpadDecimal",
        [Key.KpDivide] = "NumpadDivide", [Key.KpEnter] = "NumpadEnter",
        [Key.Home] = "Home", [Key.End] = "End", [Key.Pageup] = "PageUp", [Key.Pagedown] = "PageDown",
        [Key.Insert] = "Insert", [Key.Delete] = "Delete",
        [Key.Up] = "ArrowUp", [Key.Down] = "ArrowDown", [Key.Left] = "ArrowLeft", [Key.Right] = "ArrowRight",
        [Key.Menu] = "ContextMenu",
    };

    static KeyMap()
    {
        for (Key k = Key.A; k <= Key.Z; k++)
            Names[k] = "Key" + (char)('A' + (k - Key.A));
        for (Key k = Key.Key0; k <= Key.Key9; k++)
            Names[k] = "Digit" + (char)('0' + (k - Key.Key0));
        for (Key k = Key.Kp0; k <= Key.Kp9; k++)
            Names[k] = "Numpad" + (char)('0' + (k - Key.Kp0));
        for (Key k = Key.F1; k <= Key.F12; k++)
            Names[k] = "F" + (1 + (k - Key.F1));
    }

    /// <summary>The pc86 name of the key, or null if the PC has no such key.</summary>
    public static string CodeOf(InputEventKey e)
    {
        Key key = e.PhysicalKeycode != Key.None ? e.PhysicalKeycode : e.Keycode;
        bool right = e.Location == KeyLocation.Right;
        switch (key)
        {
            case Key.Shift: return right ? "ShiftRight" : "ShiftLeft";
            case Key.Ctrl: return right ? "ControlRight" : "ControlLeft";
            case Key.Alt: return right ? "AltRight" : "AltLeft";
            case Key.Meta: return right ? "MetaRight" : "MetaLeft";
        }
        return Names.TryGetValue(key, out string name) ? name : null;
    }
}
