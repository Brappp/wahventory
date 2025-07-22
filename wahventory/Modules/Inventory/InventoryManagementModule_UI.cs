using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;
using WahVentory.External;
using WahVentory.Helpers;
using WahVentory.Models;

namespace WahVentory.Modules.Inventory;

public partial class InventoryManagementModule
{
    private void DrawTopControls()
    {
        // Create a styled top bar with better spacing
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(6, 4));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8, 4));
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.145f, 0.145f, 0.145f, 1f));
        
        ImGui.BeginChild("TopBar", new Vector2(0, 34), true, ImGuiWindowFlags.NoScrollbar);
        
        // Search box with icon
        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.Text(FontAwesomeIcon.Search.ToIconString());
        ImGui.PopFont();
        
        ImGui.SameLine();
        ImGui.SetNextItemWidth(180f);
        if (ImGui.InputTextWithHint("##Search", "Search items...", ref _searchFilter, 100))
        {
            _lastCategoryUpdate = DateTime.Now;
        }
        
        // Only update categories after a delay
        if (_lastCategoryUpdate != DateTime.MinValue && DateTime.Now - _lastCategoryUpdate > _categoryUpdateInterval)
        {
            UpdateCategories();
            _lastCategoryUpdate = DateTime.MinValue;
        }
        
        ImGui.SameLine();
        ImGui.PushFont(UiBuilder.IconFont);
        if (ImGui.Button(FontAwesomeIcon.Sync.ToIconString() + "##Refresh"))
        {
            RefreshInventory();
        }
        ImGui.PopFont();
        ImGui.SameLine();
        ImGui.Text("Refresh");
        
        // Divider
        ImGui.SameLine();
        ImGui.TextColored(new Vector4(0.3f, 0.3f, 0.3f, 1f), "|");
        
        // Quick filters
        ImGui.SameLine();
        if (ImGui.Checkbox("Armory", ref _showArmory)) 
        {
            RefreshInventory();
        }
        
        ImGui.SameLine();
        if (ImGui.Checkbox("HQ Only", ref _showOnlyHQ)) UpdateCategories();
        
        ImGui.SameLine();
        if (ImGui.Checkbox("Show Flagged", ref _showOnlyFlagged)) UpdateCategories();
        
        ImGui.EndChild();
        ImGui.PopStyleColor();
        ImGui.PopStyleVar(2);
    }
    
    private void DrawFiltersAndSettings()
    {
        // Safety Filters Section
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(6, 4));
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.145f, 0.145f, 0.145f, 1f));
        
        ImGui.BeginChild("FiltersSection", new Vector2(0, 120), true);
        
        // Header
        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.TextColored(ColorBlue, FontAwesomeIcon.Shield.ToIconString());
        ImGui.PopFont();
        ImGui.SameLine();
        ImGui.Text("Safety Filters");
        ImGui.SameLine();
        
        var activeCount = CountActiveFilters();
        ImGui.TextColored(ColorInfo, $"({activeCount}/9 active)");
        
        ImGui.Spacing();
        
        // Draw filter grid
        DrawFilterGrid();
        
        ImGui.EndChild();
        ImGui.PopStyleColor();
        ImGui.PopStyleVar();
        
        // Market Settings Bar
        DrawMarketSettingsBar();
    }
    
    private int CountActiveFilters()
    {
        var filters = Settings.SafetyFilters;
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
    
    private void ResetAllFilters()
    {
        var filters = Settings.SafetyFilters;
        filters.FilterUltimateTokens = false;
        filters.FilterCurrencyItems = false;
        filters.FilterCrystalsAndShards = false;
        filters.FilterGearsetItems = false;
        filters.FilterIndisposableItems = false;
        filters.FilterHighLevelGear = false;
        filters.FilterUniqueUntradeable = false;
        filters.FilterHQItems = false;
        filters.FilterCollectables = false;
        filters.FilterSpiritbondedItems = false;
        _plugin.Configuration.Save();
        RefreshInventory();
    }
    
    private void DrawFilterGrid()
    {
        var filters = Settings.SafetyFilters;
        bool changed = false;
        var windowWidth = ImGui.GetWindowWidth();
        var columnWidth = (windowWidth - 40) / 3f;
        
        ImGui.Columns(3, "FilterColumns", false);
        ImGui.SetColumnWidth(0, columnWidth);
        ImGui.SetColumnWidth(1, columnWidth);
        ImGui.SetColumnWidth(2, columnWidth);
        
        // Column 1
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
        
        // Column 2
        var filterCrystals = filters.FilterCrystalsAndShards;
        if (DrawFilterItem("Crystals & Shards", ref filterCrystals, "Crafting materials", "?"))
        {
            filters.FilterCrystalsAndShards = filterCrystals;
            changed = true;
        }
        
        var filterHighLevel = filters.FilterHighLevelGear;
        if (DrawFilterItem($"High Level Gear (i{filters.MaxGearItemLevel}+)", ref filterHighLevel, 
            $"Equipment above item level {filters.MaxGearItemLevel}", "?"))
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
        
        // Column 3
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
            _plugin.Configuration.Save();
            RefreshInventory();
        }
    }
    
    private bool DrawFilterItem(string label, ref bool value, string tooltip, string helpText)
    {
        var changed = false;
        
        ImGui.BeginGroup();
        
        // Checkbox
        if (ImGui.Checkbox($"##{label}", ref value))
        {
            changed = true;
        }
        
        // Label
        ImGui.SameLine();
        ImGui.Text(label);
        
        // Help icon
        ImGui.SameLine();
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.6f, 0.6f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.2f, 0.2f, 0.2f, 1f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 8f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(2, 0));
        
        ImGui.SmallButton(helpText);
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(tooltip);
        }
        
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(2);
        
        ImGui.EndGroup();
        
        return changed;
    }
    
    private void DrawMarketSettingsBar()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(6, 4));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8, 4));
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.145f, 0.145f, 0.145f, 1f));
        
        ImGui.BeginChild("MarketSettings", new Vector2(0, 34), true, ImGuiWindowFlags.NoScrollbar);
        
        var showPrices = Settings.ShowMarketPrices;
        if (ImGui.Checkbox("Show Prices", ref showPrices))
        {
            Settings.ShowMarketPrices = showPrices;
            _plugin.Configuration.Save();
        }
        
        if (Settings.ShowMarketPrices)
        {
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.3f, 0.3f, 0.3f, 1f), "|");
            
            ImGui.SameLine();
            ImGui.Text("World:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(100);
            if (ImGui.BeginCombo("##World", _selectedWorld))
            {
                foreach (var world in _availableWorlds)
                {
                    bool isSelected = world == _selectedWorld;
                    if (ImGui.Selectable(world, isSelected))
                    {
                        _selectedWorld = world;
                        _universalisClient.Dispose();
                        _universalisClient = new UniversalisClient(Plugin.Log, _selectedWorld);
                        _priceCache.Clear();
                        foreach (var item in _allItems)
                        {
                            item.MarketPrice = null;
                            item.MarketPriceFetchTime = null;
                        }
                    }
                }
                ImGui.EndCombo();
            }
            
            ImGui.SameLine();
            ImGui.Text("Cache:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(50);
            var cacheMinutes = Settings.PriceCacheDurationMinutes;
            if (ImGui.InputInt("##Cache", ref cacheMinutes, 0))
            {
                Settings.PriceCacheDurationMinutes = Math.Max(1, cacheMinutes);
                _plugin.Configuration.Save();
            }
            ImGui.SameLine();
            ImGui.Text("min");
            
            ImGui.SameLine();
            var autoRefresh = Settings.AutoRefreshPrices;
            if (ImGui.Checkbox("Auto-refresh", ref autoRefresh))
            {
                Settings.AutoRefreshPrices = autoRefresh;
                _plugin.Configuration.Save();
            }
        }
        
        ImGui.EndChild();
        ImGui.PopStyleColor();
        ImGui.PopStyleVar(2);
    }
    
    private void DrawCompactMarketSettings()
    {
        ImGui.Text("Market:");
        ImGui.SameLine();
        
        var showPrices = Settings.ShowMarketPrices;
        if (ImGui.Checkbox("Prices", ref showPrices))
        {
            Settings.ShowMarketPrices = showPrices;
            _plugin.Configuration.Save();
        }
        
        ImGui.SameLine();
        ImGui.Text("World:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100);
        if (ImGui.BeginCombo("##World", _selectedWorld))
        {
            foreach (var world in _availableWorlds)
            {
                bool isSelected = world == _selectedWorld;
                if (ImGui.Selectable(world, isSelected))
                {
                    _selectedWorld = world;
                    _universalisClient.Dispose();
                    _universalisClient = new UniversalisClient(Plugin.Log, _selectedWorld);
                    _priceCache.Clear();
                    foreach (var item in _allItems)
                    {
                        item.MarketPrice = null;
                        item.MarketPriceFetchTime = null;
                    }
                }
            }
            ImGui.EndCombo();
        }
        
        ImGui.SameLine();
        ImGui.Text("Cache:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(50);
        var cacheMinutes = Settings.PriceCacheDurationMinutes;
        if (ImGui.InputInt("##Cache", ref cacheMinutes, 0))
        {
            Settings.PriceCacheDurationMinutes = Math.Max(1, cacheMinutes);
            _plugin.Configuration.Save();
        }
        ImGui.SameLine();
        ImGui.Text("min");
        
        ImGui.SameLine();
        var autoRefresh = Settings.AutoRefreshPrices;
        if (ImGui.Checkbox("Auto-refresh", ref autoRefresh))
        {
            Settings.AutoRefreshPrices = autoRefresh;
            _plugin.Configuration.Save();
        }
    }
    
    private void DrawTabBar()
    {
        // This method is no longer used since we removed the top buttons
    }

    
    private void DrawAvailableItemsTab()
    {
        foreach (var category in _categories)
        {
            if (category.Items.Count == 0) continue;
            
            var isExpanded = ExpandedCategories.GetValueOrDefault(category.CategoryId, true);
            
            ImGui.PushID(category.Name);
            
            // Create a custom header with integrated controls
            var nodeFlags = ImGuiTreeNodeFlags.Framed | ImGuiTreeNodeFlags.AllowItemOverlap;
            if (isExpanded) nodeFlags |= ImGuiTreeNodeFlags.DefaultOpen;
            
            var open = ImGui.TreeNodeEx($"{category.Name}###{category.CategoryId}", nodeFlags);
            
            // Stats on the same line
            ImGui.SameLine();
            ImGui.TextColored(ColorInfo, $"({category.Items.Count} items, {category.TotalQuantity} total)");
            
            if (Settings.ShowMarketPrices && category.TotalValue.HasValue)
            {
                ImGui.SameLine();
                ImGui.TextColored(ColorPrice, $"{category.TotalValue.Value:N0} gil");
            }
            
            // Select All button - positioned with better spacing
            var selectAllWidth = 90f;
            var windowWidth = ImGui.GetWindowContentRegionMax().X;
            ImGui.SameLine(windowWidth - selectAllWidth - 10);
            DrawCategoryControls(category);
            
            if (open)
            {
                ExpandedCategories[category.CategoryId] = true;
                _expandedCategoriesChanged = true;
                DrawCategoryItems(category);
                
                // Immediately fetch prices for visible tradable items in this category
                if (Settings.ShowMarketPrices)
                {
                    var tradableItems = category.Items.Where(i => i.CanBeTraded && !i.MarketPrice.HasValue && !_fetchingPrices.Contains(i.ItemId)).Take(2);
                    foreach (var item in tradableItems)
                    {
                        _ = FetchMarketPrice(item);
                    }
                }
                
                ImGui.TreePop();
            }
            else
            {
                ExpandedCategories[category.CategoryId] = false;
                _expandedCategoriesChanged = true;
            }
            
            ImGui.PopID();
        }
    }
    
    private void DrawCategoryItems(CategoryGroup category)
    {
        ImGui.Indent();
        
        // Make table more compact
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(4, 2));
        
        if (ImGui.BeginTable($"ItemTable_{category.Name}", Settings.ShowMarketPrices ? 6 : 5, 
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.Resizable))
        {
            ImGui.TableSetupColumn("##checkbox", ImGuiTableColumnFlags.WidthFixed, 25);
            ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthFixed, 250);
            ImGui.TableSetupColumn("Qty", ImGuiTableColumnFlags.WidthFixed, 40);
            ImGui.TableSetupColumn("Location", ImGuiTableColumnFlags.WidthFixed, 100);
            if (Settings.ShowMarketPrices)
            {
                ImGui.TableSetupColumn("Price", ImGuiTableColumnFlags.WidthFixed, 80);
                ImGui.TableSetupColumn("Total", ImGuiTableColumnFlags.WidthFixed, 80);
            }
            else
            {
                ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, 120);
            }
            
            ImGui.TableHeadersRow();
            
            foreach (var item in category.Items)
            {
                DrawItemRow(item, category);
            }
            
            ImGui.EndTable();
        }
        
        ImGui.PopStyleVar(); // Pop CellPadding
        ImGui.Unindent();
    }
    
    private void DrawCategoryControls(CategoryGroup category)
    {
        var selectedInCategory = category.Items.Count(i => _selectedItems.Contains(i.ItemId));
        var allSelectedInCategory = category.Items.Count > 0 && category.Items.All(i => _selectedItems.Contains(i.ItemId));
        
        // Show selection info inline with category header
        var buttonText = allSelectedInCategory ? "Deselect All" : "Select All";
        
        if (ImGui.SmallButton(buttonText))
        {
            if (allSelectedInCategory)
            {
                // Deselect all items in this category
                foreach (var item in category.Items)
                {
                    _selectedItems.Remove(item.ItemId);
                    item.IsSelected = false;
                }
            }
            else
            {
                // Select all items in this category
                foreach (var item in category.Items)
                {
                    _selectedItems.Add(item.ItemId);
                    item.IsSelected = true;
                }
            }
        }
        
        // Add warning for dangerous categories
        if (IsDangerousCategory(category))
        {
            ImGui.SameLine();
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.TextColored(ColorWarning, FontAwesomeIcon.ExclamationTriangle.ToIconString());
            ImGui.PopFont();
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("This category may contain valuable items. Please review carefully!");
            }
        }
    }
    
    private bool IsDangerousCategory(CategoryGroup category)
    {
        var dangerousCategories = new[]
        {
            "Weapons", "Tools", "Armor", "Accessories", "Materia", "Crystals",
            "Medicine & Meals", "Materials", "Other"
        };
        
        return dangerousCategories.Any(dangerous => 
            category.Name.Contains(dangerous, StringComparison.OrdinalIgnoreCase));
    }
    
    private void DrawItemRow(InventoryItemInfo item, CategoryGroup category)
    {
        ImGui.TableNextRow();
        ImGui.PushID(item.GetUniqueKey());
        
        // Check if this row is selected
        var isSelected = _selectedItems.Contains(item.ItemId);
        
        // Apply selection background if selected
        if (isSelected)
        {
            var rowMin = ImGui.GetCursorScreenPos();
            rowMin.Y -= ImGui.GetStyle().CellPadding.Y;
            var rowMax = new Vector2(rowMin.X + ImGui.GetWindowWidth(), rowMin.Y + ImGui.GetTextLineHeightWithSpacing() + ImGui.GetStyle().CellPadding.Y * 2);
            ImGui.GetWindowDrawList().AddRectFilled(rowMin, rowMax, ImGui.GetColorU32(new Vector4(0.3f, 0.5f, 0.7f, 0.3f)));
        }
        
        // Checkbox column
        ImGui.TableNextColumn();
        if (ImGui.Checkbox($"##check_{item.GetUniqueKey()}", ref isSelected))
        {
            if (isSelected)
            {
                _selectedItems.Add(item.ItemId);
                item.IsSelected = true;
            }
            else
            {
                _selectedItems.Remove(item.ItemId);
                item.IsSelected = false;
            }
        }
        
        // Item name with icon
        ImGui.TableNextColumn();
        
        // Item icon and name aligned properly
        var iconSize = new Vector2(16, 16);
        if (item.IconId > 0)
        {
            var icon = _iconCache.GetIcon(item.IconId);
            if (icon != null)
            {
                var cursorPos = ImGui.GetCursorPos();
                ImGui.Image(icon.ImGuiHandle, iconSize);
                ImGui.SameLine();
                // Align text vertically with icon
                ImGui.SetCursorPosY(cursorPos.Y + (iconSize.Y - ImGui.GetTextLineHeight()) / 2);
            }
            else
            {
                // Reserve space for missing icon
                ImGui.Dummy(iconSize);
                ImGui.SameLine();
            }
        }
        else
        {
            // Reserve space for missing icon
            ImGui.Dummy(iconSize);
            ImGui.SameLine();
        }
        
        ImGui.Text(item.Name);
        
        // Add filter tags right after item name
        DrawItemFilterTags(item);
        
        if (item.IsHQ)
        {
            ImGui.SameLine();
            ImGui.TextColored(ColorHQItem, "[HQ]");
        }
        
        // Safety flags (keep for additional info)
        DrawItemSafetyFlags(item);
        
        // Quantity
        ImGui.TableNextColumn();
        ImGui.Text(item.Quantity.ToString());
        
        // Location
        ImGui.TableNextColumn();
        ImGui.Text(GetLocationName(item.Container));
        
        if (Settings.ShowMarketPrices && item.CanBeTraded)
        {
            // Unit price
            ImGui.TableNextColumn();
            
            if (_fetchingPrices.Contains(item.ItemId))
            {
                // Show loading spinner
                ImGui.PushFont(UiBuilder.IconFont);
                var time = ImGui.GetTime();
                var spinnerIcon = time % 1.0 < 0.5 ? FontAwesomeIcon.CircleNotch : FontAwesomeIcon.Circle;
                ImGui.TextColored(ColorLoading, spinnerIcon.ToIconString());
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextColored(ColorLoading, "Loading...");
            }
            else
            {
                var priceText = item.GetFormattedPrice();
                if (priceText == "N/A")
                {
                    ImGui.TextColored(ColorNotTradeable, priceText);
                }
                else if (priceText != "---")
                {
                    ImGui.Text(priceText);
                }
                else
                {
                    ImGui.TextColored(ColorSubdued, priceText);
                    
                    // Small fetch button
                    ImGui.SameLine();
                    ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(2, 0));
                    ImGui.PushFont(UiBuilder.IconFont);
                    if (ImGui.SmallButton(FontAwesomeIcon.DollarSign.ToIconString() + $"##fetch_{item.GetUniqueKey()}"))
                    {
                        _ = FetchMarketPrice(item);
                    }
                    ImGui.PopFont();
                    ImGui.PopStyleVar();
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Fetch current market price");
                }
            }
            
            // Total value
            ImGui.TableNextColumn();
            if (item.MarketPrice.HasValue)
            {
                if (item.MarketPrice.Value == -1)
                {
                    ImGui.TextColored(ColorNotTradeable, "N/A");
                }
                else
                {
                    var total = item.MarketPrice.Value * item.Quantity;
                    ImGui.Text($"{total:N0}g");
                }
            }
            else
            {
                ImGui.TextColored(ColorNotTradeable, "---");
            }
        }
        else
        {
            // Status column
            ImGui.TableNextColumn();
            
            if (!item.CanBeTraded)
            {
                ImGui.TextColored(ColorError, "Not Tradeable");
            }
            else if (!item.CanBeDiscarded)
            {
                ImGui.TextColored(ColorError, "Not Discardable");
            }
            else if (item.IsCollectable)
            {
                ImGui.TextColored(ColorLoading, "Collectable");
            }
            else if (item.SpiritBond >= 100)
            {
                ImGui.TextColored(ColorSuccess, "Spiritbonded");
            }
            else if (Settings.BlacklistedItems.Contains(item.ItemId))
            {
                ImGui.TextColored(ColorError, "Blacklisted");
            }
        }
        
        ImGui.PopID();
    }
    
    private void DrawItemFilterTags(InventoryItemInfo item)
    {
        var appliedFilters = GetAppliedFilters(item);
        
        if (!appliedFilters.Any())
            return;
        
        foreach (var (filterName, isActive) in appliedFilters)
        {
            ImGui.SameLine();
            
            var tagColor = isActive ? 
                new Vector4(0.8f, 0.2f, 0.2f, 1) :
                new Vector4(0.6f, 0.6f, 0.6f, 1);
            
            ImGui.PushStyleColor(ImGuiCol.Text, tagColor);
            
            var shortName = GetShortFilterName(filterName);
            ImGui.Text($"[{shortName}]");
            
            ImGui.PopStyleColor();
            
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(isActive ? 
                    $"HIDDEN by '{filterName}' filter" :
                    $"Would be hidden by '{filterName}' filter (disabled)");
            }
        }
    }
    
    private void DrawItemSafetyFlags(InventoryItemInfo item)
    {
        if (item.SafetyAssessment?.SafetyFlags.Any() != true)
            return;
        
        var additionalFlags = item.SafetyAssessment.SafetyFlags
            .Where(flag => !IsFilterRelatedFlag(flag))
            .ToList();
        
        if (!additionalFlags.Any())
            return;
        
        ImGui.SameLine();
        
        var flagColor = item.SafetyAssessment.FlagColor switch
        {
            SafetyFlagColor.Critical => new Vector4(0.8f, 0.2f, 0.2f, 1),
            SafetyFlagColor.Warning => new Vector4(0.9f, 0.5f, 0.1f, 1),
            SafetyFlagColor.Caution => new Vector4(0.9f, 0.9f, 0.2f, 1),
            SafetyFlagColor.Info => new Vector4(0.3f, 0.7f, 1.0f, 1),
            _ => ImGui.GetStyle().Colors[(int)ImGuiCol.Text]
        };
        
        ImGui.PushStyleColor(ImGuiCol.Text, flagColor);
        
        var flagText = string.Join(", ", additionalFlags);
        ImGui.Text($"[{flagText}]");
        
        if (ImGui.IsItemHovered())
        {
            ImGui.BeginTooltip();
            ImGui.Text("Additional Safety Info:");
            foreach (var flag in additionalFlags)
            {
                ImGui.BulletText(flag);
            }
            ImGui.EndTooltip();
        }
        
        ImGui.PopStyleColor();
    }
    
    private List<(string filterName, bool isActive)> GetAppliedFilters(InventoryItemInfo item)
    {
        var appliedFilters = new List<(string, bool)>();
        var filters = Settings.SafetyFilters;
        
        if (InventoryHelpers.HardcodedBlacklist.Contains(item.ItemId))
            appliedFilters.Add(("Ultimate Tokens", filters.FilterUltimateTokens));
        
        if (InventoryHelpers.CurrencyRange.Contains(item.ItemId))
            appliedFilters.Add(("Currency", filters.FilterCurrencyItems));
        
        if (item.ItemUICategory == 63 || item.ItemUICategory == 64)
            appliedFilters.Add(("Crystals", filters.FilterCrystalsAndShards));
        
        if (InventoryHelpers.IsInGearset(item.ItemId))
            appliedFilters.Add(("Gearset", filters.FilterGearsetItems));
        
        if (item.IsIndisposable)
            appliedFilters.Add(("Indisposable", filters.FilterIndisposableItems));
        
        if (item.EquipSlotCategory > 0 && item.ItemLevel >= filters.MaxGearItemLevel)
            appliedFilters.Add(("High-Level", filters.FilterHighLevelGear));
        
        if (item.IsUnique && item.IsUntradable && !InventoryHelpers.SafeUniqueItems.Contains(item.ItemId))
            appliedFilters.Add(("Unique", filters.FilterUniqueUntradeable));
        
        if (item.IsHQ)
            appliedFilters.Add(("HQ", filters.FilterHQItems));
        
        if (item.IsCollectable)
            appliedFilters.Add(("Collectable", filters.FilterCollectables));
        
        if (item.SpiritBond >= filters.MinSpiritbondToFilter)
            appliedFilters.Add(("Spiritbond", filters.FilterSpiritbondedItems));
        
        return appliedFilters;
    }
    
    private bool IsFilterRelatedFlag(string flag)
    {
        return flag.Contains("Ultimate") ||
               flag.Contains("Currency") ||
               flag.Contains("Crystal") ||
               flag.Contains("Gearset") ||
               flag.Contains("Indisposable") ||
               flag.Contains("High Level") ||
               flag.Contains("Unique") ||
               flag.Contains("High Quality") ||
               flag.Contains("Collectable") ||
               flag.Contains("Spiritbond");
    }
    
    private string GetShortFilterName(string filterName)
    {
        return filterName switch
        {
            "Ultimate Tokens" => "Ultimate",
            "Currency" => "Currency",
            "Crystals" => "Crystals", 
            "Gearset" => "Gearset",
            "Indisposable" => "Protected",
            "High-Level" => "HiLvl",
            "Unique" => "Unique",
            "HQ" => "HQ",
            "Collectable" => "Collect",
            "Spiritbond" => "SB",
            _ => filterName
        };
    }
    
    private string GetLocationName(InventoryType type)
    {
        return type switch
        {
            InventoryType.Inventory1 => "Inventory 1",
            InventoryType.Inventory2 => "Inventory 2",
            InventoryType.Inventory3 => "Inventory 3",
            InventoryType.Inventory4 => "Inventory 4",
            InventoryType.Crystals => "Crystals",
            InventoryType.Currency => "Currency",
            InventoryType.SaddleBag1 => "Saddlebag 1",
            InventoryType.SaddleBag2 => "Saddlebag 2",
            InventoryType.PremiumSaddleBag1 => "P.Saddlebag 1",
            InventoryType.PremiumSaddleBag2 => "P.Saddlebag 2",
            InventoryType.ArmoryMainHand => "Armory (Main)",
            InventoryType.ArmoryOffHand => "Armory (Off)",
            InventoryType.ArmoryHead => "Armory (Head)",
            InventoryType.ArmoryBody => "Armory (Body)",
            InventoryType.ArmoryHands => "Armory (Hands)",
            InventoryType.ArmoryLegs => "Armory (Legs)",
            InventoryType.ArmoryFeets => "Armory (Feet)",
            InventoryType.ArmoryEar => "Armory (Ears)",
            InventoryType.ArmoryNeck => "Armory (Neck)",
            InventoryType.ArmoryWrist => "Armory (Wrist)",
            InventoryType.ArmoryRings => "Armory (Rings)",
            _ => type.ToString()
        };
    }
    
    private void DrawFilteredItemsTab(List<InventoryItemInfo> filteredItems)
    {
        if (!filteredItems.Any())
        {
            ImGui.TextColored(new Vector4(0.6f, 0.8f, 0.6f, 1), "No items are currently being filtered out.");
            ImGui.Text("All items in your inventory are available for selection.");
            return;
        }
        
        var filteredCategories = filteredItems
            .GroupBy(i => new { i.ItemUICategory, i.CategoryName })
            .Select(categoryGroup => new
            {
                CategoryId = categoryGroup.Key.ItemUICategory,
                CategoryName = categoryGroup.Key.CategoryName,
                Items = categoryGroup.ToList()
            })
            .OrderBy(c => c.CategoryName)
            .ToList();
        
        foreach (var category in filteredCategories)
        {
            var isExpanded = ExpandedCategories.GetValueOrDefault(category.CategoryId, true);
            
            ImGui.PushID($"Filtered_{category.CategoryName}");
            
            var categoryHeaderText = $"{category.CategoryName} ({category.Items.Count} protected)";
            
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.8f, 0.6f, 0.6f, 1));
            var headerOpen = ImGui.CollapsingHeader(categoryHeaderText, isExpanded ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None);
            ImGui.PopStyleColor();
            
            if (headerOpen)
            {
                ExpandedCategories[category.CategoryId] = true;
                _expandedCategoriesChanged = true;
                
                ImGui.Indent();
                DrawFilteredItemsTable(category.Items);
                ImGui.Unindent();
            }
            else
            {
                ExpandedCategories[category.CategoryId] = false;
                _expandedCategoriesChanged = true;
            }
            
            ImGui.PopID();
        }
    }
    
    private void DrawFilteredItemsTable(List<InventoryItemInfo> items)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(4, 2));
        
        if (ImGui.BeginTable("FilteredItemsTable", Settings.ShowMarketPrices ? 5 : 4, 
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit))
        {
            ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthFixed, 275);
            ImGui.TableSetupColumn("Qty", ImGuiTableColumnFlags.WidthFixed, 40);
            ImGui.TableSetupColumn("Location", ImGuiTableColumnFlags.WidthFixed, 100);
            if (Settings.ShowMarketPrices)
            {
                ImGui.TableSetupColumn("Price", ImGuiTableColumnFlags.WidthFixed, 80);
                ImGui.TableSetupColumn("Total", ImGuiTableColumnFlags.WidthFixed, 80);
            }
            else
            {
                ImGui.TableSetupColumn("Reason", ImGuiTableColumnFlags.WidthFixed, 120);
            }
            
            ImGui.TableHeadersRow();
            
            foreach (var item in items)
            {
                DrawFilteredItemRow(item);
            }
            
            ImGui.EndTable();
        }
        
        ImGui.PopStyleVar(); // Pop CellPadding
    }
    
    private void DrawFilteredItemRow(InventoryItemInfo item)
    {
        ImGui.TableNextRow();
        
        ImGui.TableNextColumn();
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.6f, 0.6f, 1));
        
        if (item.IconId > 0)
        {
            var icon = _iconCache.GetIcon(item.IconId);
            if (icon != null)
            {
                ImGui.Image(icon.ImGuiHandle, new Vector2(16, 16));
                ImGui.SameLine();
            }
        }
        
        ImGui.Text(item.Name);
        ImGui.PopStyleColor();
        
        DrawItemFilterTags(item);
        
        if (item.IsHQ)
        {
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.4f, 0.6f, 0.4f, 1), "[HQ]");
        }
        
        ImGui.TableNextColumn();
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.6f, 0.6f, 1));
        ImGui.Text(item.Quantity.ToString());
        ImGui.PopStyleColor();
        
        ImGui.TableNextColumn();
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.6f, 0.6f, 1));
        ImGui.Text(GetContainerDisplayName(item.Container));
        ImGui.PopStyleColor();
        
        if (Settings.ShowMarketPrices)
        {
            ImGui.TableNextColumn();
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.6f, 0.6f, 1));
            var priceText = item.GetFormattedPrice();
            ImGui.Text(priceText);
            ImGui.PopStyleColor();
            
            ImGui.TableNextColumn();
            if (item.MarketPrice.HasValue && item.MarketPrice.Value > 0)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.6f, 0.6f, 1));
                ImGui.Text($"{item.MarketPrice.Value * item.Quantity:N0}g");
                ImGui.PopStyleColor();
            }
        }
        else
        {
            ImGui.TableNextColumn();
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.8f, 0.6f, 0.6f, 1));
            var reason = GetFilterReason(item);
            ImGui.Text(reason);
            ImGui.PopStyleColor();
        }
    }
    
    private string GetFilterReason(InventoryItemInfo item)
    {
        var filters = Settings.SafetyFilters;
        
        if (filters.FilterUltimateTokens && InventoryHelpers.HardcodedBlacklist.Contains(item.ItemId))
            return "Ultimate/Special";
        if (filters.FilterCurrencyItems && InventoryHelpers.CurrencyRange.Contains(item.ItemId))
            return "Currency";
        if (filters.FilterCrystalsAndShards && (item.ItemUICategory == 63 || item.ItemUICategory == 64))
            return "Crystal/Shard";
        if (filters.FilterGearsetItems && InventoryHelpers.IsInGearset(item.ItemId))
            return "In Gearset";
        if (filters.FilterIndisposableItems && item.IsIndisposable)
            return "Indisposable";
        if (filters.FilterHighLevelGear && item.EquipSlotCategory > 0 && item.ItemLevel >= filters.MaxGearItemLevel)
            return $"High Level (i{item.ItemLevel})";
        if (filters.FilterUniqueUntradeable && item.IsUnique && item.IsUntradable && !InventoryHelpers.SafeUniqueItems.Contains(item.ItemId))
            return "Unique & Untradeable";
        if (filters.FilterHQItems && item.IsHQ)
            return "High Quality";
        if (filters.FilterCollectables && item.IsCollectable)
            return "Collectable";
        if (filters.FilterSpiritbondedItems && item.SpiritBond >= filters.MinSpiritbondToFilter)
            return $"Spiritbond {item.SpiritBond}%";
        
        return "Protected";
    }
    
    private string GetContainerDisplayName(InventoryType container)
    {
        return container switch
        {
            InventoryType.Inventory1 or InventoryType.Inventory2 or 
            InventoryType.Inventory3 or InventoryType.Inventory4 => "Inventory",
            InventoryType.ArmoryMainHand => "Armory (Main)",
            InventoryType.ArmoryOffHand => "Armory (Off)",
            InventoryType.ArmoryHead => "Armory (Head)",
            InventoryType.ArmoryBody => "Armory (Body)",
            InventoryType.ArmoryHands => "Armory (Hands)",
            InventoryType.ArmoryLegs => "Armory (Legs)",
            InventoryType.ArmoryFeets => "Armory (Feet)",
            InventoryType.ArmoryEar => "Armory (Ears)",
            InventoryType.ArmoryNeck => "Armory (Neck)",
            InventoryType.ArmoryWrist => "Armory (Wrists)",
            InventoryType.ArmoryRings => "Armory (Rings)",
            InventoryType.ArmorySoulCrystal => "Armory (Soul)",
            _ => container.ToString()
        };
    }
    
    private List<InventoryItemInfo> GetFilteredOutItems()
    {
        var allItems = _originalItems.AsEnumerable();
        var filteredOutItems = new List<InventoryItemInfo>();
        var filters = Settings.SafetyFilters;
        
        foreach (var item in allItems)
        {
            bool isFilteredOut = false;
            
            if (filters.FilterUltimateTokens && InventoryHelpers.HardcodedBlacklist.Contains(item.ItemId))
                isFilteredOut = true;
            else if (filters.FilterCurrencyItems && InventoryHelpers.CurrencyRange.Contains(item.ItemId))
                isFilteredOut = true;
            else if (filters.FilterCrystalsAndShards && (item.ItemUICategory == 63 || item.ItemUICategory == 64))
                isFilteredOut = true;
            else if (filters.FilterGearsetItems && InventoryHelpers.IsInGearset(item.ItemId))
                isFilteredOut = true;
            else if (filters.FilterIndisposableItems && item.IsIndisposable)
                isFilteredOut = true;
            else if (filters.FilterHighLevelGear && item.EquipSlotCategory > 0 && item.ItemLevel >= filters.MaxGearItemLevel)
                isFilteredOut = true;
            else if (filters.FilterUniqueUntradeable && item.IsUnique && item.IsUntradable && !InventoryHelpers.SafeUniqueItems.Contains(item.ItemId))
                isFilteredOut = true;
            else if (filters.FilterHQItems && item.IsHQ)
                isFilteredOut = true;
            else if (filters.FilterCollectables && item.IsCollectable)
                isFilteredOut = true;
            else if (filters.FilterSpiritbondedItems && item.SpiritBond >= filters.MinSpiritbondToFilter)
                isFilteredOut = true;
            
            if (isFilteredOut)
            {
                filteredOutItems.Add(item);
            }
        }
        
        return filteredOutItems;
    }
    
    private void DrawBottomActionBar()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(8, 6));
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.165f, 0.165f, 0.165f, 1f));
        
        ImGui.BeginChild("ActionBar", new Vector2(0, 42), true, ImGuiWindowFlags.NoScrollbar);
        
        // Left side - Action buttons
        if (ImGui.Button("Clear All", new Vector2(80, 0)))
        {
            _selectedItems.Clear();
            foreach (var item in _allItems)
            {
                item.IsSelected = false;
            }
        }
        
        ImGui.SameLine();
        
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.541f, 0.227f, 0.227f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.641f, 0.327f, 0.327f, 1f));
        var discardButtonText = $"Discard ({_selectedItems.Count})";
        
        if (_selectedItems.Count > 0)
        {
            if (ImGui.Button(discardButtonText, new Vector2(80, 0)))
            {
                PrepareDiscard();
            }
        }
        else
        {
            ImGui.BeginDisabled();
            ImGui.Button(discardButtonText, new Vector2(80, 0));
            ImGui.EndDisabled();
        }
        ImGui.PopStyleColor(2);
        
        // Center/Right side - Statistics
        var totalValue = _categories.Sum(c => c.TotalValue ?? 0);
        var availableItems = _categories.Sum(c => c.Items.Count);
        var protectedItems = GetFilteredOutItems().Count;
        
        // Right-align statistics
        var windowWidth = ImGui.GetWindowContentRegionMax().X;
        var statSpacing = 150f;
        
        ImGui.SameLine(windowWidth - statSpacing * 3);
        ImGui.Text("Total Value:");
        ImGui.SameLine();
        ImGui.TextColored(ColorPrice, $"{totalValue:N0} gil");
        
        ImGui.SameLine(windowWidth - statSpacing * 2);
        ImGui.Text("Available:");
        ImGui.SameLine();
        ImGui.TextColored(ColorSuccess, availableItems.ToString());
        
        ImGui.SameLine(windowWidth - statSpacing);
        ImGui.Text("Protected:");
        ImGui.SameLine();
        ImGui.TextColored(ColorWarning, protectedItems.ToString());
        
        ImGui.EndChild();
        ImGui.PopStyleColor();
        ImGui.PopStyleVar();
    }
}
