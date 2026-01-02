using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using FFXIVClientStructs.FFXIV.Component.GUI;
using wahventory.Core;
using wahventory.Modules.Search.Inventories;

namespace wahventory.Modules.Search.Windows;

public unsafe class SearchBarWindow : Window
{
    public string SearchTerm = "";
    public GameInventory? Inventory = null;
    private bool _needsFocus = false;
    private bool _canShow = true;
    public bool CanShow => _canShow;

    private readonly SearchBarSettings _settings;
    private readonly SearchModule _searchModule;

    public SearchBarWindow(SearchBarSettings settings, SearchModule searchModule)
        : base("WahVentorySearchBar", ImGuiWindowFlags.NoScrollbar
            | ImGuiWindowFlags.NoCollapse
            | ImGuiWindowFlags.NoResize
            | ImGuiWindowFlags.NoScrollWithMouse
            | ImGuiWindowFlags.NoBackground
            | ImGuiWindowFlags.NoTitleBar
            | ImGuiWindowFlags.NoSavedSettings)
    {
        _settings = settings;
        _searchModule = searchModule;

        Size = new Vector2(_settings.SearchBarWidth * 0.86f, 22);
        SizeCondition = ImGuiCond.Always;

        RespectCloseHotkey = false;
    }

    public void UpdateCanShow()
    {
        if (_settings.KeybindOnly)
        {
            _canShow = _canShow || _searchModule.IsKeybindActive;
        }
        else
        {
            _canShow = true;
        }
    }

    public override void OnOpen()
    {
        if (_settings.AutoFocus)
        {
            _needsFocus = true;
        }

        _canShow = !_settings.KeybindOnly;
    }

    public override void OnClose()
    {
        if (_settings.AutoClear)
        {
            SearchTerm = "";
        }
    }

    public override void PreDraw()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(0, 0));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowMinSize, new Vector2(1, 1));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 0);

        if (Inventory != null && Inventory.Addon != 0)
        {
            var inventory = (AtkUnitBase*)Inventory.Addon;
            var window = inventory->WindowCollisionNode;
            if (window == null) return;

            var width = window->AtkResNode.Width * inventory->Scale;
            var x = inventory->X + width / 2f - _settings.SearchBarWidth / 2f + Inventory.OffsetX;
            var y = inventory->Y + 13 * inventory->Scale;

            Position = new Vector2(x, y);
            Size = new Vector2(_settings.SearchBarWidth * 0.86f, 22);
        }
    }

    public override void PostDraw()
    {
        ImGui.PopStyleVar(5);
    }

    public override void Draw()
    {
        if (!_canShow) return;

        ImGui.PushItemWidth(_settings.SearchBarWidth);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, ImGui.ColorConvertFloat4ToU32(_settings.SearchBarBackgroundColor));
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, ImGui.ColorConvertFloat4ToU32(_settings.SearchBarBackgroundColor));
        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.ColorConvertFloat4ToU32(_settings.SearchBarTextColor));

        if (_searchModule.IsKeybindActive || _needsFocus)
        {
            ImGui.SetKeyboardFocusHere(0);
            _needsFocus = false;
        }

        ImGui.InputText("", ref SearchTerm, 100);

        if (_settings.KeybindOnly && !ImGui.IsItemActive() && SearchTerm.Length == 0)
        {
            _canShow = false;
        }

        ImGui.PopStyleColor(3);
    }
}
