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
        var availableWidth = ImGui.GetContentRegionAvail().X;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        
        // Compact search bar
        ImGui.SetNextItemWidth(200f);
        if (ImGui.InputTextWithHint("##Search", "Search...", ref _searchFilter, 100))
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
        if (ImGui.Button("Refresh"))
        {
            RefreshInventory();
        }
        
        // Core filters on same line
        ImGui.SameLine();
        ImGui.Text("|");
        
        ImGui.SameLine();
        if (ImGui.Checkbox("Armory", ref _showArmory)) 
        {
            RefreshInventory();
        }
        
        ImGui.SameLine();
        if (ImGui.Checkbox("HQ Only", ref _showOnlyHQ)) UpdateCategories();
        
        ImGui.SameLine();
        if (ImGui.Checkbox("Flagged", ref _showOnlyFlagged)) UpdateCategories();
        
        // Just show item counts inline with the controls
        ImGui.SameLine();
        ImGui.TextColored(ColorInfo, $"({_allItems.Count} items, {_selectedItems.Count} selected)");
    }
    
    private void DrawFiltersAndSettings()
    {
        ImGui.Separator();
        
        // Safety Filters in a compact grid
        ImGui.Text("Safety Filters:");
        ImGui.SameLine();
        DrawCompactSafetyFilters();
        
        // Market price settings on one line if enabled
        if (Settings.ShowMarketPrices)
        {
            DrawCompactMarketSettings();
        }
        else
        {
            var showPrices = Settings.ShowMarketPrices;
            if (ImGui.Checkbox("Show Market Prices", ref showPrices))
            {
                Settings.ShowMarketPrices = showPrices;
                _plugin.Configuration.Save();
            }
        }
    }
    
    private void DrawCompactSafetyFilters()
    {
        var filters = Settings.SafetyFilters;
        bool changed = false;
        
        // Show active filter count
        var activeCount = 0;
        if (filters.FilterUltimateTokens) activeCount++;
        if (filters.FilterCurrencyItems) activeCount++;
        if (filters.FilterCrystalsAndShards) activeCount++;
        if (filters.FilterGearsetItems) activeCount++;
        if (filters.FilterIndisposableItems) activeCount++;
        if (filters.FilterHighLevelGear) activeCount++;
        if (filters.FilterUniqueUntradeable) activeCount++;
        if (filters.FilterHQItems) activeCount++;
        if (filters.FilterCollectables) activeCount++;
        
        ImGui.TextColored(ColorInfo, $"({activeCount}/9 active)");
        ImGui.SameLine();
        
        // Draw filters in a compact 3-column layout
        ImGui.BeginGroup();
        
        // Column 1
        var filterUltimate = filters.FilterUltimateTokens;
        if (ImGui.Checkbox("##UltimateTokens", ref filterUltimate))
        {
            filters.FilterUltimateTokens = filterUltimate;
            changed = true;
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Ultimate Tokens: Raid tokens, preorder items");
        ImGui.SameLine();
        ImGui.Text("Ultimate");
        
        ImGui.SameLine(150);
        var filterCrystals = filters.FilterCrystalsAndShards;
        if (ImGui.Checkbox("##Crystals", ref filterCrystals))
        {
            filters.FilterCrystalsAndShards = filterCrystals;
            changed = true;
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Crystals & Shards: Crafting materials");
        ImGui.SameLine();
        ImGui.Text("Crystals");
        
        ImGui.SameLine(300);
        var filterGearset = filters.FilterGearsetItems;
        if (ImGui.Checkbox("##Gearset", ref filterGearset))
        {
            filters.FilterGearsetItems = filterGearset;
            changed = true;
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Gearset Items: Equipment in any gearset");
        ImGui.SameLine();
        ImGui.Text("Gearsets");
        
        // Column 2
        var filterCurrency = filters.FilterCurrencyItems;
        if (ImGui.Checkbox("##Currency", ref filterCurrency))
        {
            filters.FilterCurrencyItems = filterCurrency;
            changed = true;
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Currency: Gil, tomestones, MGP, etc.");
        ImGui.SameLine();
        ImGui.Text("Currency");
        
        ImGui.SameLine(150);
        var filterHighLevel = filters.FilterHighLevelGear;
        if (ImGui.Checkbox("##HighLevel", ref filterHighLevel))
        {
            filters.FilterHighLevelGear = filterHighLevel;
            changed = true;
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip($"High Level Gear: i{filters.MaxGearItemLevel}+");
        ImGui.SameLine();
        ImGui.Text("High Lvl");
        
        ImGui.SameLine(300);
        var filterUnique = filters.FilterUniqueUntradeable;
        if (ImGui.Checkbox("##Unique", ref filterUnique))
        {
            filters.FilterUniqueUntradeable = filterUnique;
            changed = true;
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Unique & Untradeable: Cannot be reacquired");
        ImGui.SameLine();
        ImGui.Text("Unique");
        
        // Column 3
        var filterHQ = filters.FilterHQItems;
        if (ImGui.Checkbox("##HQ", ref filterHQ))
        {
            filters.FilterHQItems = filterHQ;
            changed = true;
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("High Quality: HQ items");
        ImGui.SameLine();
        ImGui.Text("HQ Items");
        
        ImGui.SameLine(150);
        var filterCollectable = filters.FilterCollectables;
        if (ImGui.Checkbox("##Collectables", ref filterCollectable))
        {
            filters.FilterCollectables = filterCollectable;
            changed = true;
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Collectables: Turn-in items");
        ImGui.SameLine();
        ImGui.Text("Collectables");
        
        ImGui.SameLine(300);
        var filterIndisposable = filters.FilterIndisposableItems;
        if (ImGui.Checkbox("##Indisposable", ref filterIndisposable))
        {
            filters.FilterIndisposableItems = filterIndisposable;
            changed = true;
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Indisposable: Cannot be discarded");
        ImGui.SameLine();
        ImGui.Text("Protected");
        
        ImGui.EndGroup();
        
        if (changed)
        {
            _plugin.Configuration.Save();
            RefreshInventory();
        }
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
    
    private void DrawAvailableItemsTab()
    {
        foreach (var category in _categories)
        {
            if (category.Items.Count == 0) continue;
            
            var isExpanded = ExpandedCategories.GetValueOrDefault(category.CategoryId, true);
            
            ImGui.PushID(category.Name);
            
            var headerText = $"{category.Name} ({category.Items.Count} items, {category.TotalQuantity} total)";
            if (Settings.ShowMarketPrices && category.TotalValue.HasValue)
            {
                headerText += $" - {category.TotalValue.Value:N0} gil";
            }
            
            if (ImGui.CollapsingHeader(headerText, isExpanded ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None))
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
        
        // Add category-specific selection controls
        DrawCategoryControls(category);
        
        if (ImGui.BeginTable($"ItemTable_{category.Name}", Settings.ShowMarketPrices ? 6 : 5, 
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 25); // Checkbox
            ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Qty", ImGuiTableColumnFlags.WidthFixed, 50);
            ImGui.TableSetupColumn("Location", ImGuiTableColumnFlags.WidthFixed, 120);
            if (Settings.ShowMarketPrices)
            {
                ImGui.TableSetupColumn("Price", ImGuiTableColumnFlags.WidthFixed, 100);
                ImGui.TableSetupColumn("Total", ImGuiTableColumnFlags.WidthFixed, 100);
            }
            else
            {
                ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, 100);
            }
            
            ImGui.TableHeadersRow();
            
            foreach (var item in category.Items)
            {
                DrawItemRow(item, category);
            }
            
            ImGui.EndTable();
        }
        
        ImGui.Unindent();
    }
    
    private void DrawCategoryControls(CategoryGroup category)
    {
        var discardableItems = category.Items.Where(i => i.SafetyAssessment?.IsSafeToDiscard == true).ToList();
        var selectedInCategory = category.Items.Count(i => _selectedItems.Contains(i.ItemId));
        var allSelectedInCategory = discardableItems.Count > 0 && discardableItems.All(i => _selectedItems.Contains(i.ItemId));
        
        // Show selection info
        ImGui.TextColored(ColorInfo, 
            $"Selected: {selectedInCategory}/{category.Items.Count} | Discardable: {discardableItems.Count}");
        
        if (discardableItems.Count > 0)
        {
            ImGui.SameLine();
            
            // Select/Deselect category button
            var buttonText = allSelectedInCategory ? $"Deselect All ({discardableItems.Count})" : $"Select All ({discardableItems.Count})";
            var buttonColor = allSelectedInCategory ? new Vector4(0.6f, 0.6f, 0.6f, 1) : new Vector4(0.2f, 0.7f, 0.2f, 1);
            
            ImGui.PushStyleColor(ImGuiCol.Button, buttonColor);
            if (ImGui.Button(buttonText))
            {
                if (allSelectedInCategory)
                {
                    // Deselect all items in this category
                    foreach (var item in discardableItems)
                    {
                        _selectedItems.Remove(item.ItemId);
                        item.IsSelected = false;
                    }
                }
                else
                {
                    // Select all discardable items in this category
                    foreach (var item in discardableItems)
                    {
                        _selectedItems.Add(item.ItemId);
                        item.IsSelected = true;
                    }
                }
            }
            ImGui.PopStyleColor();
            
            // Add warning for dangerous categories
            if (IsDangerousCategory(category))
            {
                ImGui.SameLine();
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.TextColored(new Vector4(0.8f, 0.5f, 0.2f, 1), FontAwesomeIcon.ExclamationTriangle.ToIconString());
                ImGui.PopFont();
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("This category may contain valuable items. Please review carefully!");
                }
            }
        }
        
        ImGui.Spacing();
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
        
        // Checkbox
        ImGui.TableNextColumn();
        var isSelected = _selectedItems.Contains(item.ItemId);
        if (ImGui.Checkbox($"##select_{item.GetUniqueKey()}", ref isSelected))
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
        
        // Always show icons using cached textures
        if (item.IconId > 0)
        {
            var icon = _iconCache.GetIcon(item.IconId);
            if (icon != null)
            {
                ImGui.Image(icon.ImGuiHandle, new Vector2(20, 20));
                ImGui.SameLine();
            }
            else
            {
                // Debug: Icon failed to load
                Plugin.Log.Debug($"Failed to load icon {item.IconId} for item {item.Name}");
            }
        }
        else
        {
            // Debug: No icon ID
            Plugin.Log.Debug($"No icon ID for item {item.Name}");
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
                ImGui.TextColored(ColorNotTradeable, priceText);
                
                // Only show fetch button if not already loading
                if (!_fetchingPrices.Contains(item.ItemId))
                {
                    ImGui.SameLine();
                    ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(2, 2));
                    if (ImGui.Button($"$##fetch_{item.GetUniqueKey()}", new Vector2(20, 20)))
                    {
                        _ = FetchMarketPrice(item);
                    }
                    ImGui.PopStyleVar();
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
        if (ImGui.BeginTable("FilteredItemsTable", Settings.ShowMarketPrices ? 5 : 4, 
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Qty", ImGuiTableColumnFlags.WidthFixed, 50);
            ImGui.TableSetupColumn("Location", ImGuiTableColumnFlags.WidthFixed, 120);
            if (Settings.ShowMarketPrices)
            {
                ImGui.TableSetupColumn("Price", ImGuiTableColumnFlags.WidthFixed, 100);
                ImGui.TableSetupColumn("Total", ImGuiTableColumnFlags.WidthFixed, 100);
            }
            else
            {
                ImGui.TableSetupColumn("Reason", ImGuiTableColumnFlags.WidthFixed, 150);
            }
            
            ImGui.TableHeadersRow();
            
            foreach (var item in items)
            {
                DrawFilteredItemRow(item);
            }
            
            ImGui.EndTable();
        }
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
                ImGui.Image(icon.ImGuiHandle, new Vector2(20, 20));
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
}
