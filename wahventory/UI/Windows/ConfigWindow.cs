using System;
using System.Numerics;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using wahventory.Core;

namespace wahventory.UI.Windows;

public class ConfigWindow : Window, IDisposable
{
    private Configuration Configuration;
    private Plugin Plugin;

    public ConfigWindow(Plugin plugin) : base("wahventory Configuration")
    {
        Flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar |
                ImGuiWindowFlags.NoScrollWithMouse;

        Size = new Vector2(450, 300);
        SizeCondition = ImGuiCond.Always;

        Plugin = plugin;
        Configuration = plugin.Configuration;
    }

    public void Dispose() { }

    public override void Draw()
    {
        ImGui.TextWrapped("Note: Safety filters are now configured in the main window. This window contains advanced settings only.");
        ImGui.Spacing();
        ImGui.Separator();
        
        ImGui.Text("Market Price Settings");
        ImGui.Separator();
        
        bool changed = false;
        var settings = Configuration.InventorySettings;
        var showMarketPrices = settings.ShowMarketPrices;
        if (ImGui.Checkbox("Show Market Prices", ref showMarketPrices))
        {
            settings.ShowMarketPrices = showMarketPrices;
            changed = true;
        }
        
        if (settings.ShowMarketPrices)
        {
            ImGui.Indent();
            ImGui.SetNextItemWidth(100);
            var cacheDuration = settings.PriceCacheDurationMinutes;
            if (ImGui.InputInt("Cache Duration (minutes)", ref cacheDuration))
            {
                settings.PriceCacheDurationMinutes = Math.Max(1, cacheDuration);
                changed = true;
            }
            
            var autoRefresh = settings.AutoRefreshPrices;
            if (ImGui.Checkbox("Auto-refresh Prices", ref autoRefresh))
            {
                settings.AutoRefreshPrices = autoRefresh;
                changed = true;
            }
            ImGui.Unindent();
        }
        
        ImGui.Spacing();
        ImGui.Separator();
        var movable = Configuration.IsConfigWindowMovable;
        if (ImGui.Checkbox("Movable Config Window", ref movable))
        {
            Configuration.IsConfigWindowMovable = movable;
            changed = true;
        }
        
        if (changed)
        {
            Plugin.ConfigManager.SaveConfiguration();
        }
        
        ImGui.Spacing();
        if (ImGui.Button("Close"))
        {
            Toggle();
        }
        ImGui.Spacing();
        
        if (ImGui.Button("Save"))
        {
            Plugin.ConfigManager.SaveConfiguration();
        }
    }
}
