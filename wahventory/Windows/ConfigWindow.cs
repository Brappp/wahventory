using System;
using System.Numerics;
using Dalamud.Interface.Windowing;
using ImGuiNET;

namespace WahVentory.Windows;

public class ConfigWindow : Window, IDisposable
{
    private Configuration Configuration;
    private Plugin Plugin;

    public ConfigWindow(Plugin plugin) : base("WahVentory Configuration")
    {
        Flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar |
                ImGuiWindowFlags.NoScrollWithMouse;

        Size = new Vector2(450, 500);
        SizeCondition = ImGuiCond.Always;

        Plugin = plugin;
        Configuration = plugin.Configuration;
    }

    public void Dispose() { }

    public override void Draw()
    {
        // Safety Filters Section
        ImGui.Text("Safety Filters");
        ImGui.Separator();
        
        var filters = Configuration.InventorySettings.SafetyFilters;
        bool changed = false;
        
        var filterUltimate = filters.FilterUltimateTokens;
        if (ImGui.Checkbox("Filter Ultimate Tokens", ref filterUltimate))
        {
            filters.FilterUltimateTokens = filterUltimate;
            changed = true;
        }
        var filterCurrency = filters.FilterCurrencyItems;
        if (ImGui.Checkbox("Filter Currency Items", ref filterCurrency))
        {
            filters.FilterCurrencyItems = filterCurrency;
            changed = true;
        }
        var filterCrystals = filters.FilterCrystalsAndShards;
        if (ImGui.Checkbox("Filter Crystals and Shards", ref filterCrystals))
        {
            filters.FilterCrystalsAndShards = filterCrystals;
            changed = true;
        }
        var filterGearset = filters.FilterGearsetItems;
        if (ImGui.Checkbox("Filter Gearset Items", ref filterGearset))
        {
            filters.FilterGearsetItems = filterGearset;
            changed = true;
        }
        var filterIndisposable = filters.FilterIndisposableItems;
        if (ImGui.Checkbox("Filter Indisposable Items", ref filterIndisposable))
        {
            filters.FilterIndisposableItems = filterIndisposable;
            changed = true;
        }
        var filterHighLevel = filters.FilterHighLevelGear;
        if (ImGui.Checkbox("Filter High Level Gear", ref filterHighLevel))
        {
            filters.FilterHighLevelGear = filterHighLevel;
            changed = true;
        }
        
        if (filters.FilterHighLevelGear)
        {
            ImGui.Indent();
            ImGui.SetNextItemWidth(100);
            var maxLevel = (int)filters.MaxGearItemLevel;
            if (ImGui.InputInt("Max Item Level", ref maxLevel))
            {
                filters.MaxGearItemLevel = (uint)Math.Max(1, maxLevel);
                changed = true;
            }
            ImGui.Unindent();
        }
        
        var filterUnique = filters.FilterUniqueUntradeable;
        if (ImGui.Checkbox("Filter Unique & Untradeable", ref filterUnique))
        {
            filters.FilterUniqueUntradeable = filterUnique;
            changed = true;
        }
        var filterHQ = filters.FilterHQItems;
        if (ImGui.Checkbox("Filter HQ Items", ref filterHQ))
        {
            filters.FilterHQItems = filterHQ;
            changed = true;
        }
        var filterCollectables = filters.FilterCollectables;
        if (ImGui.Checkbox("Filter Collectables", ref filterCollectables))
        {
            filters.FilterCollectables = filterCollectables;
            changed = true;
        }
        var filterSpiritbond = filters.FilterSpiritbondedItems;
        if (ImGui.Checkbox("Filter Spiritbonded Items", ref filterSpiritbond))
        {
            filters.FilterSpiritbondedItems = filterSpiritbond;
            changed = true;
        }
        
        if (filters.FilterSpiritbondedItems)
        {
            ImGui.Indent();
            ImGui.SetNextItemWidth(100);
            var minSpiritbond = filters.MinSpiritbondToFilter;
            if (ImGui.SliderFloat("Min Spiritbond %", ref minSpiritbond, 0f, 100f))
            {
                filters.MinSpiritbondToFilter = minSpiritbond;
                changed = true;
            }
            ImGui.Unindent();
        }
        
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Text("Market Price Settings");
        ImGui.Separator();
        
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
        
        // Window Settings
        var movable = Configuration.IsConfigWindowMovable;
        if (ImGui.Checkbox("Movable Config Window", ref movable))
        {
            Configuration.IsConfigWindowMovable = movable;
            changed = true;
        }
        
        if (changed)
        {
            Configuration.Save();
        }
        
        ImGui.Spacing();
        if (ImGui.Button("Close"))
        {
            Toggle();
        }
    }
}
