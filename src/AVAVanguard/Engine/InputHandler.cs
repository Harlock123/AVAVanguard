using Avalonia.Input;

namespace AVAVanguard.Engine;

/// <summary>
/// Keyboard and gamepad boiled down to the controls: the four directions to fly, and the four guns.
/// </summary>
public sealed class InputHandler : IDisposable
{
    private readonly List<Key> _held = [];
    private readonly ControllerManager _pad = new(0);

    public void OnKeyDown(Key key)
    {
        _held.Remove(key);
        _held.Add(key);
    }

    public void OnKeyUp(Key key) => _held.Remove(key);
    public bool Has(Key key) => _held.Contains(key);
    private bool Any(params Key[] keys) => keys.Any(_held.Contains);

    public void Update() => _pad.Update();

    public Controls Controls
    {
        get
        {
            int dx = (Any(Key.Right, Key.NumPad6) ? 1 : 0) - (Any(Key.Left, Key.NumPad4) ? 1 : 0);
            if (dx == 0) dx = _pad.DPadLeft || _pad.LeftStickX < -0.4f ? -1 : _pad.DPadRight || _pad.LeftStickX > 0.4f ? 1 : 0;
            int dy = (Any(Key.Down, Key.NumPad2) ? 1 : 0) - (Any(Key.Up, Key.NumPad8) ? 1 : 0);
            if (dy == 0) dy = _pad.DPadUp || _pad.LeftStickY < -0.4f ? -1 : _pad.DPadDown || _pad.LeftStickY > 0.4f ? 1 : 0;
            // Four guns: W A S D (or I J K L), Space for straight ahead; on a pad the face buttons or the right stick.
            int ax = (Any(Key.D, Key.L, Key.Space, Key.LeftCtrl, Key.RightCtrl) || _pad.ButtonB ? 1 : 0) - (Any(Key.A, Key.J) || _pad.ButtonX ? 1 : 0);
            int ay = (Any(Key.S, Key.K) || _pad.ButtonA ? 1 : 0) - (Any(Key.W, Key.I) || _pad.ButtonY ? 1 : 0);
            if (ax == 0 && ay == 0)
            {
                if (Math.Abs(_pad.RightStickX) > 0.5f || Math.Abs(_pad.RightStickY) > 0.5f)
                {
                    if (Math.Abs(_pad.RightStickX) >= Math.Abs(_pad.RightStickY)) ax = Math.Sign(_pad.RightStickX);
                    else ay = Math.Sign(_pad.RightStickY);
                }
                else if (_pad.RightTrigger > 0.5f) ax = 1;
            }
            return new Controls(dx, dy, ax, ay);
        }
    }

    public bool Start => Any(Key.Enter) || _pad.ButtonStart;

    public bool PadUp => _pad.DPadUp || _pad.LeftStickY < -0.5f;
    public bool PadDown => _pad.DPadDown || _pad.LeftStickY > 0.5f;
    public bool PadLeft => _pad.DPadLeft || _pad.LeftStickX < -0.5f;
    public bool PadRight => _pad.DPadRight || _pad.LeftStickX > 0.5f;
    public bool PadAccept => _pad.ButtonA || _pad.ButtonX || _pad.ButtonB;
    public bool PadDone => _pad.ButtonStart;
    public bool PadPause => _pad.ButtonStart;
    public bool PadBack => _pad.ButtonBack;

    public void Dispose() => _pad.Dispose();
}
