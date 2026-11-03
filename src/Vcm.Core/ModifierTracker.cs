namespace Vcm.Core;

/// <summary>
/// Follows which modifier keys are held from the key events themselves. GetAsyncKeyState can be stale inside a
/// low-level keyboard hook, which made repeated shortcuts fall through to Windows.
/// </summary>
public sealed class ModifierTracker
{
    private bool _lwin, _rwin, _lctrl, _rctrl, _lalt, _ralt, _lshift, _rshift;

    public bool Win => _lwin || _rwin;
    public bool Ctrl => _lctrl || _rctrl;
    public bool Alt => _lalt || _ralt;
    public bool Shift => _lshift || _rshift;

    /// <summary>Records a key event; keys that are not modifiers are ignored.</summary>
    public void Update(int vk, bool down)
    {
        switch (vk)
        {
            case KeyNames.VK_LWIN: _lwin = down; break;
            case KeyNames.VK_RWIN: _rwin = down; break;
            case KeyNames.VK_LCONTROL or KeyNames.VK_CONTROL: _lctrl = down; break;
            case KeyNames.VK_RCONTROL: _rctrl = down; break;
            case KeyNames.VK_LMENU or KeyNames.VK_MENU: _lalt = down; break;
            case KeyNames.VK_RMENU: _ralt = down; break;
            case KeyNames.VK_LSHIFT or KeyNames.VK_SHIFT: _lshift = down; break;
            case KeyNames.VK_RSHIFT: _rshift = down; break;
        }
    }

    /// <summary>Replaces the state, e.g. with the real keyboard state when the hook starts.</summary>
    public void Reset(Func<int, bool> isDown)
    {
        _lwin = isDown(KeyNames.VK_LWIN);
        _rwin = isDown(KeyNames.VK_RWIN);
        _lctrl = isDown(KeyNames.VK_LCONTROL);
        _rctrl = isDown(KeyNames.VK_RCONTROL);
        _lalt = isDown(KeyNames.VK_LMENU);
        _ralt = isDown(KeyNames.VK_RMENU);
        _lshift = isDown(KeyNames.VK_LSHIFT);
        _rshift = isDown(KeyNames.VK_RSHIFT);
    }
}
