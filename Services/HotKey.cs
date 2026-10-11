using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;

namespace Tendedero.Services;

/// <summary>
/// Global hotkey. Default: Ctrl+Alt+T (mirrors the Mac's ^⌥T).
/// Uses a hidden WinForms message-only window to receive WM_HOTKEY.
/// </summary>
internal sealed class HotKey : IDisposable
{
    private readonly MessageWindow _window;
    private readonly int _id;
    private readonly Action _onPressed;
    private bool _registered;
    private bool _disposed;

    public HotKey(uint modifiers, uint vk, Action onPressed)
    {
        _onPressed = onPressed;
        _id = GetHashCode();
        _window = new MessageWindow(OnHotKey);
        _registered = NativeMethods.RegisterHotKey(_window.Handle, _id, modifiers, vk);
    }

    private void OnHotKey(int id)
    {
        if (id == _id) _onPressed();
    }

    public bool IsRegistered => _registered;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_registered) NativeMethods.UnregisterHotKey(_window.Handle, _id);
        _window.Dispose();
    }

    public static HotKey? RegisterDefault(Action onPressed)
    {
        uint vk = (uint)KeyInterop.VirtualKeyFromKey(Key.T);
        var hk = new HotKey(NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT, vk, onPressed);
        return hk.IsRegistered ? hk : null;
    }

    private sealed class MessageWindow : NativeWindow, IDisposable
    {
        private readonly Action<int> _onHotKey;

        public MessageWindow(Action<int> onHotKey)
        {
            _onHotKey = onHotKey;
            CreateHandle(new CreateParams { Caption = "TendederoHotKey" });
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_HOTKEY)
                _onHotKey(m.WParam.ToInt32());
            base.WndProc(ref m);
        }

        public void Dispose() => DestroyHandle();
    }
}
