using System;
using System.Numerics;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Bindings.ImGui;
using wahventory.Core;
using wahventory.Services;

namespace wahventory.UI.Components;

public class FilterPanelComponent
{
    public event Action? OnFiltersChanged;
    
    public void Draw(InventorySettings settings, bool compact = false)
    {
        if (compact)
        {
            DrawCompactFilters(settings);
        }
        else
        {
            DrawFullFilters(settings);
        }
    }
    
    private void DrawFullFilters(InventorySettings settings)
    {
        using var style = ImRaii.PushStyle(ImGuiStyleVar.FramePadding, new Vector2(6, 5));
        using var color = ImRaii.PushColor(ImGuiCol.ChildBg, new Vector4(0.145f, 0.145f, 0.145f, 1f));
        
        using (var child = ImRaii.Child("FiltersSection", new Vector2(0, 130), true))
        {
            using (var font = ImRaii.PushFont(UiBuilder.IconFont))
            {
                ImGui.TextColored(Theme.ColorBlue, FontAwesomeIcon.Shield.ToIconString());
            }
            ImGui.SameLine();
            ImGui.Text("Safety Filters");
            ImGui.SameLine();
            
            var activeCount = CountActiveFilters(settings.SafetyFilters);
            ImGui.TextColored(Theme.ColorInfo, $"({activeCount}/9 active)");
            
            ImGui.Spacing();
            
            DrawFilterGrid(settings.SafetyFilters);
        }
    }
    
    private void DrawCompactFilters(InventorySettings settings)
    {
        ImGui.Text("Safety Filters");
        ImGui.Separator();
        
        var filters = settings.SafetyFilters;
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
            if (ImGui.SliderInt("Min Spiritbond %", ref minSpiritbond, 0, 100))
            {
                filters.MinSpiritbondToFilter = minSpiritbond;
                changed = true;
            }
            ImGui.Unindent();
        }
        
        if (changed)
        {
            OnFiltersChanged?.Invoke();
        }
    }
    
    private void DrawFilterGrid(SafetyFilters filters)
    {
        bool changed = false;
        var windowWidth = ImGui.GetWindowWidth();
        var columnWidth = (windowWidth - 40) / 3f;
        
        ImGui.Columns(3, "FilterColumns", false);
        ImGui.SetColumnWidth(0, columnWidth);
        ImGui.SetColumnWidth(1, columnWidth);
        ImGui.SetColumnWidth(2, columnWidth);
        
        var filterUltimate = filters.FilterUltimateTokens;
        if (DrawFilterItem("Ultimate Tokens", ref filterUltimate, "Raid tokens, preorder items", "?"))
        {
            filters.FilterUltimateTokens = filterUltimate;
            changed = true;
        }
        
        var filterCurrency = filters.FilterCurrencyItems;
        if (DrawFilterItem("Currency Items", ref filterCurrency, "Gil, tomestones, MGP, etc.", "?"))
        {
            filters.FilterCurrencyItems = filterCurrency;
            changed = true;
        }
        
        var filterHQ = filters.FilterHQItems;
        if (DrawFilterItem("HQ Items", ref filterHQ, "High Quality items", "?"))
        {
            filters.FilterHQItems = filterHQ;
            changed = true;
        }
        
        ImGui.NextColumn();
        
        var filterCrystals = filters.FilterCrystalsAndShards;
        if (DrawFilterItem("Crystals & Shards", ref filterCrystals, "Crafting materials", "?"))
        {
            filters.FilterCrystalsAndShards = filterCrystals;
            changed = true;
        }
        
        var filterHighLevel = filters.FilterHighLevelGear;
        if (DrawHighLevelGearFilter(ref filterHighLevel, filters))
        {
            filters.FilterHighLevelGear = filterHighLevel;
            changed = true;
        }
        
        var filterCollectable = filters.FilterCollectables;
        if (DrawFilterItem("Collectables", ref filterCollectable, "Turn-in items", "?"))
        {
            filters.FilterCollectables = filterCollectable;
            changed = true;
        }
        
        ImGui.NextColumn();
        
        var filterGearset = filters.FilterGearsetItems;
        if (DrawFilterItem("Gearset Items", ref filterGearset, "Equipment in any gearset", "?"))
        {
            filters.FilterGearsetItems = filterGearset;
            changed = true;
        }
        
        var filterUnique = filters.FilterUniqueUntradeable;
        if (DrawFilterItem("Unique & Untradeable", ref filterUnique, "Cannot be reacquired", "?"))
        {
            filters.FilterUniqueUntradeable = filterUnique;
            changed = true;
        }
        
        var filterIndisposable = filters.FilterIndisposableItems;
        if (DrawFilterItem("Protected Items", ref filterIndisposable, "Cannot be discarded", "?"))
        {
            filters.FilterIndisposableItems = filterIndisposable;
            changed = true;
        }
        
        ImGui.Columns(1);
        
        if (changed)
        {
            OnFiltersChanged?.Invoke();
        }
    }
    
    private bool DrawFilterItem(string label, ref bool value, string tooltip, string helpText)
    {
        var changed = false;
        
        using (var group = ImRaii.Group())
        {
            if (ImGui.Checkbox($"##{label}", ref value))
            {
                changed = true;
            }
            ImGui.SameLine();
            ImGui.Text(label);
            ImGui.SameLine();
            using (var colors = ImRaii.PushColor(ImGuiCol.Text, new Vector4(0.6f, 0.6f, 0.6f, 1f))
                                      .Push(ImGuiCol.Button, new Vector4(0, 0, 0, 0))
                                      .Push(ImGuiCol.ButtonHovered, new Vector4(0.2f, 0.2f, 0.2f, 0.3f)))
            using (var styles = ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, 10f)
                                      .Push(ImGuiStyleVar.FramePadding, new Vector2(3, 1)))
            {
                ImGui.SmallButton(helpText);
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(tooltip);
                }
            }
        }
        
        return changed;
    }
    
    private bool DrawHighLevelGearFilter(ref bool filterHighLevel, SafetyFilters filters)
    {
        var changed = false;
        
        if (ImGui.Checkbox($"##FilterHighLevel", ref filterHighLevel))
        {
            changed = true;
        }
        
        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.Text("High Level Gear (");
        ImGui.SameLine(0, 0);
        ImGui.TextColored(Theme.ColorWarning, "i");
        ImGui.SameLine(0, 2);
        
        using (var styles = ImRaii.PushStyle(ImGuiStyleVar.FramePadding, new Vector2(4, 2))
                                  .Push(ImGuiStyleVar.FrameBorderSize, 0))
        using (var colors = ImRaii.PushColor(ImGuiCol.FrameBg, new Vector4(0.2f, 0.2f, 0.2f, 0.5f))
                                  .Push(ImGuiCol.FrameBgHovered, new Vector4(0.3f, 0.3f, 0.3f, 0.6f))
                                  .Push(ImGuiCol.Text, Theme.ColorWarning))
        {
            ImGui.SetNextItemWidth(40);
            int maxLevel = (int)filters.MaxGearItemLevel;
            if (ImGui.InputInt("##MaxGearItemLevel", ref maxLevel, 0, 0))
            {
                filters.MaxGearItemLevel = (uint)Math.Max(1, Math.Min(999, maxLevel));
                changed = true;
            }
        }
        
        ImGui.SameLine(0, 2);
        ImGui.TextColored(Theme.ColorWarning, "+");
        ImGui.SameLine(0, 0);
        ImGui.Text(")");
        ImGui.SameLine();
        
        using (var colors = ImRaii.PushColor(ImGuiCol.Text, new Vector4(0.6f, 0.6f, 0.6f, 1f))
                                  .Push(ImGuiCol.Button, new Vector4(0, 0, 0, 0))
                                  .Push(ImGuiCol.ButtonHovered, new Vector4(0.2f, 0.2f, 0.2f, 0.3f)))
        using (var styles = ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, 10f)
                                  .Push(ImGuiStyleVar.FramePadding, new Vector2(3, 1)))
        {
            ImGui.SmallButton("?");
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip($"Equipment above item level {filters.MaxGearItemLevel}\nClick the number to change the threshold");
            }
        }
        
        return changed;
    }
    
    
    private int CountActiveFilters(SafetyFilters filters)
    {
        var count = 0;
        if (filters.FilterUltimateTokens) count++;
        if (filters.FilterCurrencyItems) count++;
        if (filters.FilterCrystalsAndShards) count++;
        if (filters.FilterGearsetItems) count++;
        if (filters.FilterIndisposableItems) count++;
        if (filters.FilterHighLevelGear) count++;
        if (filters.FilterUniqueUntradeable) count++;
        if (filters.FilterHQItems) count++;
        if (filters.FilterCollectables) count++;
        return count;
    }

    /// <summary>
    /// Vertical-sidebar layout: one filter per row, short labels, thresholds inline.
    /// Optional counts argument shows per-filter "(N)" indicators on the right.
    /// </summary>
    public void DrawSidebar(InventorySettings settings, FilterHiddenCounts? counts = null)
    {
        var filters = settings.SafetyFilters;
        var active = CountActiveFilters(filters);

        using (ImRaii.PushColor(ImGuiCol.Text, Theme.ColorBlue))
        {
            ImGui.Text("FILTERS");
        }
        ImGui.SameLine();
        ImGui.TextColored(Theme.ColorSubdued, $"{active} / 9 active");
        ImGui.Separator();
        ImGui.Spacing();

        bool changed = false;

        changed |= SidebarCheckbox("Currency",            () => filters.FilterCurrencyItems,    v => filters.FilterCurrencyItems = v,    "Gil, tomestones, MGP, item IDs < 100", counts?.Currency);
        changed |= SidebarCheckbox("Crystals & Shards",   () => filters.FilterCrystalsAndShards, v => filters.FilterCrystalsAndShards = v, "Aether crystals, shards, clusters",    counts?.CrystalsAndShards);
        changed |= SidebarCheckbox("In Gearset",          () => filters.FilterGearsetItems,     v => filters.FilterGearsetItems = v,     "Equipment in any saved gearset",        counts?.InGearset);
        changed |= SidebarCheckbox("Indisposable",        () => filters.FilterIndisposableItems, v => filters.FilterIndisposableItems = v, "Items the game won't let you discard", counts?.Indisposable);
        changed |= SidebarCheckbox("Ultimate / Special",  () => filters.FilterUltimateTokens,   v => filters.FilterUltimateTokens = v,   "Raid tokens, retired tomestones",       counts?.UltimateSpecial);

        ImGui.Spacing();
        using (ImRaii.PushColor(ImGuiCol.Text, Theme.ColorSubdued))
        {
            ImGui.Text("QUALITY & LEVEL");
        }
        ImGui.Separator();

        changed |= SidebarCheckbox("HQ Items",             () => filters.FilterHQItems,           v => filters.FilterHQItems = v,           "High-Quality flagged items",   counts?.HQ);
        changed |= SidebarCheckbox("Collectables",         () => filters.FilterCollectables,      v => filters.FilterCollectables = v,      "Collectability turn-in items", counts?.Collectables);
        changed |= SidebarCheckbox("Unique & Untradeable", () => filters.FilterUniqueUntradeable, v => filters.FilterUniqueUntradeable = v, "Cannot be reacquired or sold", counts?.UniqueUntradeable);

        ImGui.Spacing();
        using (ImRaii.PushColor(ImGuiCol.Text, Theme.ColorSubdued))
        {
            ImGui.Text("THRESHOLDS");
        }
        ImGui.Separator();

        // High-level gear filter + threshold input
        var high = filters.FilterHighLevelGear;
        if (ImGui.Checkbox("##HighLvlGear", ref high))
        {
            filters.FilterHighLevelGear = high;
            changed = true;
        }
        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.Text("High-Level Gear");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(50);
        int maxLvl = (int)filters.MaxGearItemLevel;
        if (ImGui.InputInt("##MaxIlvl", ref maxLvl, 0, 0))
        {
            filters.MaxGearItemLevel = (uint)Math.Max(1, Math.Min(999, maxLvl));
            changed = true;
        }

        // Spiritbond filter + threshold slider
        var sb = filters.FilterSpiritbondedItems;
        if (ImGui.Checkbox("##SpiritbondFilter", ref sb))
        {
            filters.FilterSpiritbondedItems = sb;
            changed = true;
        }
        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.Text("Spiritbonded");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(50);
        int sbPct = filters.MinSpiritbondToFilter;
        if (ImGui.InputInt("##MinSb", ref sbPct, 0, 0))
        {
            filters.MinSpiritbondToFilter = Math.Max(0, Math.Min(100, sbPct));
            changed = true;
        }
        ImGui.SameLine();
        using (ImRaii.PushColor(ImGuiCol.Text, Theme.ColorSubdued))
        {
            ImGui.Text("%");
        }

        if (changed)
        {
            OnFiltersChanged?.Invoke();
        }
    }

    private static bool SidebarCheckbox(string label, Func<bool> get, Action<bool> set, string tooltip, int? count = null)
    {
        var v = get();
        var changed = ImGui.Checkbox(label, ref v);
        if (changed) set(v);
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(tooltip);
        }

        if (count.HasValue && count.Value > 0)
        {
            var text = count.Value.ToString();
            var width = ImGui.CalcTextSize(text).X;
            var rowEnd = ImGui.GetWindowContentRegionMax().X;
            ImGui.SameLine(rowEnd - width - 4);
            ImGui.TextColored(Theme.ColorSubdued, text);
        }

        return changed;
    }
}

