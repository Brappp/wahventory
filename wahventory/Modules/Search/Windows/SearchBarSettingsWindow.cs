using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using wahventory.Core;
using wahventory.Modules.Search.Filters;

namespace wahventory.Modules.Search.Windows;

public class SearchBarSettingsWindow : Window
{
    private readonly SearchBarSettings _settings;
    private readonly KeyBind _keybind;
    private readonly Filter[] _filters;

    public SearchBarSettingsWindow(SearchBarSettings settings, KeyBind keybind, Filter[] filters)
        : base("Search Bar Settings##WahVentory", ImGuiWindowFlags.None)
    {
        _settings = settings;
        _keybind = keybind;
        _filters = filters;

        Size = new Vector2(400, 500);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        if (ImGui.BeginTabBar("SearchBarSettingsTabs"))
        {
            if (ImGui.BeginTabItem("General"))
            {
                DrawGeneralTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Keybind"))
            {
                DrawKeybindTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Style"))
            {
                DrawStyleTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Offsets"))
            {
                DrawOffsetsTab();
                ImGui.EndTabItem();
            }

            if (ImGui.BeginTabItem("Filters"))
            {
                DrawFiltersTab();
                ImGui.EndTabItem();
            }

            ImGui.EndTabBar();
        }
    }

    private void DrawGeneralTab()
    {
        var autoClear = _settings.AutoClear;
        if (ImGui.Checkbox("Auto Clear", ref autoClear))
        {
            _settings.AutoClear = autoClear;
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Clear the search bar when it closes.");
        }

        var autoFocus = _settings.AutoFocus;
        if (ImGui.Checkbox("Auto Focus", ref autoFocus))
        {
            _settings.AutoFocus = autoFocus;
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Automatically focus the search bar when it opens.");
        }

        var highlightTabs = _settings.HighlightTabs;
        if (ImGui.Checkbox("Highlight Tabs", ref highlightTabs))
        {
            _settings.HighlightTabs = highlightTabs;
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Highlight inventory tabs that contain matching items.");
        }
    }

    private void DrawKeybindTab()
    {
        _keybind.Draw(200);

        var keybindOnly = _settings.KeybindOnly;
        if (ImGui.Checkbox("Keybind Only", ref keybindOnly))
        {
            _settings.KeybindOnly = keybindOnly;
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Only show the search bar when the keybind is pressed.");
        }

        var keybindPassthrough = _settings.KeybindPassthrough;
        if (ImGui.Checkbox("Keybind Passthrough", ref keybindPassthrough))
        {
            _settings.KeybindPassthrough = keybindPassthrough;
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Allow the game to receive the keybind press as well.");
        }
    }

    private void DrawStyleTab()
    {
        var searchBarWidth = _settings.SearchBarWidth;
        if (ImGui.SliderInt("Search Bar Width", ref searchBarWidth, 50, 300))
        {
            _settings.SearchBarWidth = searchBarWidth;
        }

        var bgColor = _settings.SearchBarBackgroundColor;
        if (ImGui.ColorEdit4("Background Color", ref bgColor))
        {
            _settings.SearchBarBackgroundColor = bgColor;
        }

        var textColor = _settings.SearchBarTextColor;
        if (ImGui.ColorEdit4("Text Color", ref textColor))
        {
            _settings.SearchBarTextColor = textColor;
        }
    }

    private void DrawOffsetsTab()
    {
        ImGui.TextWrapped("Adjust the horizontal position of the search bar for each inventory type.");
        ImGui.Separator();

        var normalOffset = _settings.NormalInventoryOffset;
        if (ImGui.SliderInt("Normal Inventory", ref normalOffset, -100, 100))
        {
            _settings.NormalInventoryOffset = normalOffset;
        }

        var largeOffset = _settings.LargeInventoryOffset;
        if (ImGui.SliderInt("Large Inventory", ref largeOffset, -100, 100))
        {
            _settings.LargeInventoryOffset = largeOffset;
        }

        var largestOffset = _settings.LargestInventoryOffset;
        if (ImGui.SliderInt("Largest Inventory", ref largestOffset, -100, 100))
        {
            _settings.LargestInventoryOffset = largestOffset;
        }

        var chocoboOffset = _settings.ChocoboInventoryOffset;
        if (ImGui.SliderInt("Chocobo Saddlebag", ref chocoboOffset, -100, 100))
        {
            _settings.ChocoboInventoryOffset = chocoboOffset;
        }

        var retainerOffset = _settings.RetainerInventoryOffset;
        if (ImGui.SliderInt("Retainer Inventory", ref retainerOffset, -100, 100))
        {
            _settings.RetainerInventoryOffset = retainerOffset;
        }

        var largeRetainerOffset = _settings.LargeRetainerInventoryOffset;
        if (ImGui.SliderInt("Large Retainer Inventory", ref largeRetainerOffset, -100, 100))
        {
            _settings.LargeRetainerInventoryOffset = largeRetainerOffset;
        }

        var armouryOffset = _settings.ArmouryInventoryOffset;
        if (ImGui.SliderInt("Armoury Chest", ref armouryOffset, -100, 100))
        {
            _settings.ArmouryInventoryOffset = armouryOffset;
        }
    }

    private void DrawFiltersTab()
    {
        ImGui.TextWrapped("Configure search filters. Use tags like 'n:' for name, 't:' for type, 'j:' for job, 'l:' for level.");
        ImGui.Separator();

        var tagSeparator = _settings.TagSeparatorCharacter;
        ImGui.PushItemWidth(50);
        if (ImGui.InputText("Tag Separator", ref tagSeparator, 1))
        {
            if (tagSeparator.Length > 0)
            {
                _settings.TagSeparatorCharacter = tagSeparator;
            }
        }

        var termsSeparator = _settings.SearchTermsSeparatorCharacter;
        if (ImGui.InputText("Terms Separator", ref termsSeparator, 1))
        {
            if (termsSeparator.Length > 0)
            {
                _settings.SearchTermsSeparatorCharacter = termsSeparator;
            }
        }
        ImGui.PopItemWidth();

        ImGui.Separator();

        foreach (var filter in _filters)
        {
            if (ImGui.CollapsingHeader(filter.Name))
            {
                ImGui.Indent();
                ImGui.TextWrapped(filter.HelpText);
                ImGui.Spacing();
                filter.Draw();
                ImGui.Unindent();
            }
        }
    }
}
