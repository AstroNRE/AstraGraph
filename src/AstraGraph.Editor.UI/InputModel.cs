namespace AstraGraph.Editor.UI;

public enum MouseButton
{
    Left,
    Right,
    Middle
}

public enum EditorKeyCode
{
    None,
    Delete,
    Escape,
    Space,
    A,
    C,
    V,
    Z,
    Y,
    F5,
    F9,
    F10,
    F11
}

public readonly record struct ModifierKeys(bool Ctrl = false, bool Shift = false, bool Alt = false)
{
    public static ModifierKeys None => new();
}
