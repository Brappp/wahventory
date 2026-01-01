using Dalamud.Bindings.ImGui;
using Dalamud.Plugin.Services;
using wahventory.Core;
using wahventory.Modules.Search.Helpers;

namespace wahventory.Modules.Search;

public class KeyBind
{
    private readonly SearchBarSettings _settings;
    private readonly IKeyState _keyState;

    public KeyBind(SearchBarSettings settings, IKeyState keyState)
    {
        _settings = settings;
        _keyState = keyState;
    }

    public override string ToString()
    {
        var ctrl = _settings.Keybind.Ctrl ? "Ctrl + " : "";
        var alt = _settings.Keybind.Alt ? "Alt + " : "";
        var shift = _settings.Keybind.Shift ? "Shift + " : "";
        var key = ((Keys)_settings.Keybind.Key).ToString();

        return ctrl + alt + shift + key;
    }

    public bool IsActive()
    {
        if (ChatHelper.IsInputTextActive() || ImGui.GetIO().WantCaptureKeyboard)
        {
            return false;
        }

        var io = ImGui.GetIO();
        var ctrl = _settings.Keybind.Ctrl ? io.KeyCtrl : !io.KeyCtrl;
        var alt = _settings.Keybind.Alt ? io.KeyAlt : !io.KeyAlt;
        var shift = _settings.Keybind.Shift ? io.KeyShift : !io.KeyShift;
        var key = KeyboardHelper.Instance?.IsKeyPressed(_settings.Keybind.Key) == true;
        var active = ctrl && alt && shift && key;

        // Block keybind for the game if passthrough is disabled
        if (active && !_settings.KeybindPassthrough && _settings.Keybind.Key is >= 0 and <= 254)
        {
            _keyState[_settings.Keybind.Key] = false;
        }

        return active;
    }

    public bool Draw(float width)
    {
        var io = ImGui.GetIO();
        var dispKey = ToString();

        ImGui.PushItemWidth(width);
        ImGui.InputText("Keybind##SearchBar", ref dispKey, 200, ImGuiInputTextFlags.ReadOnly);
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Press a key combination to set the keybind. Backspace to clear.");
        }

        if (ImGui.IsItemActive())
        {
            if (KeyboardHelper.Instance?.IsKeyPressed((int)Keys.Back) == true)
            {
                Reset();
                return true;
            }

            var keyPressed = KeyboardHelper.Instance?.GetKeyPressed() ?? 0;
            if (keyPressed > 0)
            {
                _settings.Keybind.Ctrl = io.KeyCtrl;
                _settings.Keybind.Alt = io.KeyAlt;
                _settings.Keybind.Shift = io.KeyShift;
                _settings.Keybind.Key = keyPressed;
                return true;
            }
        }

        return false;
    }

    public void Reset()
    {
        _settings.Keybind.Key = 0;
        _settings.Keybind.Ctrl = false;
        _settings.Keybind.Alt = false;
        _settings.Keybind.Shift = false;
    }
}
