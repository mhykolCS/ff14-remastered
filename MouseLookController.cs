using System.Runtime.InteropServices;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace CinematicMode;

/// <summary>Maps a held Mouse4 to native camera dragging while FFXIV is focused.</summary>
public sealed unsafe class MouseLookController : IDisposable
{
    const int LeftButton = 0x01, Mouse4 = 0x05;
    const uint LeftDown = 0x0002, LeftUp = 0x0004;
    bool wasHeld;
    bool injected;
    public int LookStarts { get; private set; }
    public bool Holding => injected;
    public string LastPress { get; private set; } = "";

    [DllImport("user32.dll")]
    static extern short GetAsyncKeyState(int key);

    [DllImport("user32.dll")]
    static extern void mouse_event(uint flags, uint dx, uint dy, uint data, nuint extraInfo);

    static bool Held(int key) => (GetAsyncKeyState(key) & 0x8000) != 0;
    public static bool PhysicalKeyDown(int key) => Held(key);

    public void Tick(bool enabled, bool focused)
    {
        var held = Held(Mouse4);
        if (!enabled || !focused || !held)
        {
            Release();
            // A button held while another app has focus must be released before
            // it can start dragging in the game again.
            wasHeld = held;
            return;
        }
        var stage = AtkStage.Instance();
        var overUi = stage == null || stage->AtkCollisionManager == null
            || stage->AtkCollisionManager->IntersectingCollisionNode != null;
        if (!wasHeld) LastPress = $"left={Held(LeftButton)} ui={overUi} addon="
            + (stage != null && stage->AtkCollisionManager != null && stage->AtkCollisionManager->IntersectingAddon != null
                ? stage->AtkCollisionManager->IntersectingAddon->NameString : "none");
        if (!wasHeld && !Held(LeftButton) && !overUi)
        {
            mouse_event(LeftDown, 0, 0, 0, 0);
            injected = true;
            LookStarts++;
        }
        wasHeld = held;
    }

    void Release()
    {
        if (!injected) return;
        mouse_event(LeftUp, 0, 0, 0, 0);
        injected = false;
    }

    public void Dispose() => Release();
}
