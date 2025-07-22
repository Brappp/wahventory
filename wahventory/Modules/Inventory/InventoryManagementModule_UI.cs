using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;
using wahventory.External;
using wahventory.Helpers;
using wahventory.Models;

namespace wahventory.Modules.Inventory;

public partial class InventoryManagementModule
{
    private void DrawTopControls()
    {
        using var style = ImRaii.PushStyle(ImGuiStyleVar.FramePadding, new Vector2(6, 5))
                                .Push(ImGuiStyleVar.ItemSpacing, new Vector2(8, 4));
        using var color = ImRaii.PushColor(ImGuiCol.ChildBg, new Vector4(0.145f, 0.145f, 0.145f, 1f));
        
        using (var child = ImRaii.Child("TopBar", new Vector2(0, 40), true, ImGuiWindowFlags.NoScrollbar))
        {
            // Center content vertically with more space
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 2);
            
            using (var font = ImRaii.PushFont(UiBuilder.IconFont))
            {
                ImGui.Text(FontAwesomeIcon.Search.ToIconString());
            }
            
            ImGui.SameLine();
            ImGui.SetNextItemWidth(180f);
            if (ImGui.InputTextWithHint("##Search", "Search items...", ref _searchFilter, 100))
            {
                // Update categories immediately when search changes
                UpdateCategories();
            }
            
            // Show clear button if there's text in the search
            if (!string.IsNullOrWhiteSpace(_searchFilter))
            {
                ImGui.SameLine();
                using (var colors = ImRaii.PushColor(ImGuiCol.Button, new Vector4(0.3f, 0.3f, 0.3f, 0.3f))
                                          .Push(ImGuiCol.ButtonHovered, new Vector4(0.4f, 0.4f, 0.4f, 0.5f)))
                {
                    if (ImGui.SmallButton("×"))
                    {
                        _searchFilter = string.Empty;
                        UpdateCategories();
                    }
                }
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Clear search");
                }
            }
            
            // Remove the delayed update logic since we now update immediately
            
            ImGui.SameLine();
            using (var font = ImRaii.PushFont(UiBuilder.IconFont))
            {
                if (ImGui.Button(FontAwesomeIcon.Sync.ToIconString() + "##Refresh", new Vector2(28, 0))) // Fixed width for icon button
                {
                    RefreshInventory();
                }
            }
            ImGui.SameLine();
            ImGui.Text("Refresh");
            
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.3f, 0.3f, 0.3f, 1f), "|");
            
            ImGui.SameLine();
            if (ImGui.Checkbox("Armory", ref _showArmory)) 
            {
                RefreshInventory();
            }
            
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.3f, 0.3f, 0.3f, 1f), "|");
            
            ImGui.SameLine();
            var showPrices = Settings.ShowMarketPrices;
            if (ImGui.Checkbox("Show Prices", ref showPrices))
            {
                Settings.ShowMarketPrices = showPrices;
                _plugin.Configuration.Save();
            }
            
            if (Settings.ShowMarketPrices)
            {
                ImGui.SameLine();
                ImGui.Text("World:");
                ImGui.SameLine();
                ImGui.SetNextItemWidth(100);
                using (var combo = ImRaii.Combo("##World", _selectedWorld))
                {
                    if (combo)
                    {
                        foreach (var world in _availableWorlds)
                        {
                            bool isSelected = world == _selectedWorld;
                            if (ImGui.Selectable(world, isSelected))
                            {
                                _selectedWorld = world;
                                _universalisClient.Dispose();
                                _universalisClient = new UniversalisClient(Plugin.Log, _selectedWorld);
                                lock (_priceCacheLock)
                                {
                                    _priceCache.Clear();
                                }
                                lock (_itemsLock)
                                {
                                    foreach (var item in _allItems)
                                    {
                                        item.MarketPrice = null;
                                        item.MarketPriceFetchTime = null;
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        
        // Add spacing after the top bar
        ImGui.Spacing();
    }
    
    private void DrawFiltersAndSettings()
    {
        using var style = ImRaii.PushStyle(ImGuiStyleVar.FramePadding, new Vector2(6, 5));
        using var color = ImRaii.PushColor(ImGuiCol.ChildBg, new Vector4(0.145f, 0.145f, 0.145f, 1f));
        
        using (var child = ImRaii.Child("FiltersSection", new Vector2(0, 130), true)) // Increased to 130 to prevent scroll bar
        {
            using (var font = ImRaii.PushFont(UiBuilder.IconFont))
            {
                ImGui.TextColored(ColorBlue, FontAwesomeIcon.Shield.ToIconString());
            }
            ImGui.SameLine();
            ImGui.Text("Safety Filters");
            ImGui.SameLine();
            
            var activeCount = CountActiveFilters();
            ImGui.TextColored(ColorInfo, $"({activeCount}/9 active)");
            
            ImGui.Spacing();
            
            DrawFilterGrid();
            
            // Removed bottom spacing to reduce padding
        }
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
        if (ImGui.Checkbox($"##FilterHighLevel", ref filterHighLevel))
        {
            filters.FilterHighLevelGear = filterHighLevel;
            changed = true;
        }
        
        ImGui.SameLine();
        ImGui.AlignTextToFramePadding(); // Align text to input fields
        ImGui.Text("High Level Gear (");
        ImGui.SameLine(0, 0);
        ImGui.TextColored(ColorWarning, "i");
        ImGui.SameLine(0, 2);
        
        // Inline editable item level - fix vertical alignment
        using (var styles = ImRaii.PushStyle(ImGuiStyleVar.FramePadding, new Vector2(4, 2))
                                  .Push(ImGuiStyleVar.FrameBorderSize, 0))
        using (var colors = ImRaii.PushColor(ImGuiCol.FrameBg, new Vector4(0.2f, 0.2f, 0.2f, 0.5f))
                                  .Push(ImGuiCol.FrameBgHovered, new Vector4(0.3f, 0.3f, 0.3f, 0.6f))
                                  .Push(ImGuiCol.Text, ColorWarning))
        {
            ImGui.SetNextItemWidth(40); // Slightly narrower
            
            int maxLevel = (int)filters.MaxGearItemLevel;
            if (ImGui.InputInt("##MaxGearItemLevel", ref maxLevel, 0, 0, ImGuiInputTextFlags.CharsDecimal))
            {
                filters.MaxGearItemLevel = (uint)Math.Max(1, Math.Min(999, maxLevel));
                changed = true;
            }
        }
        
        ImGui.SameLine(0, 2);
        ImGui.TextColored(ColorWarning, "+");
        ImGui.SameLine(0, 0);
        ImGui.Text(")");
        
        // Help icon
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
        
        using (var group = ImRaii.Group())
        {
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

    
    private void DrawAvailableItemsTab()
    {
        List<CategoryGroup> categoriesCopy;
        lock (_categoriesLock)
        {
            categoriesCopy = new List<CategoryGroup>(_categories);
        }
        
        // When searching, show all results in a single view
        if (!string.IsNullOrWhiteSpace(_searchFilter))
        {
            DrawSearchResultsView(categoriesCopy);
            return;
        }
        
        // Normal category view when not searching
        foreach (var category in categoriesCopy)
        {
            if (category.Items.Count == 0) continue;
            
            var isExpanded = ExpandedCategories.GetValueOrDefault(category.CategoryId, true);
            
            // Use a unique ID based on category ID to ensure proper state tracking
            using var id = ImRaii.PushId($"Category_{category.CategoryId}");
            
            // Create a custom header with integrated controls
            var nodeFlags = ImGuiTreeNodeFlags.AllowItemOverlap | ImGuiTreeNodeFlags.SpanAvailWidth;
            if (isExpanded) nodeFlags |= ImGuiTreeNodeFlags.DefaultOpen;
            
            using (var node = ImRaii.TreeNode($"{category.Name}###{category.CategoryId}_node", nodeFlags))
            {
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
                
                if (node)
                {
                    ExpandedCategories[category.CategoryId] = true;
                    _expandedCategoriesChanged = true;
                    DrawCategoryItems(category);
                    
                    // Immediately fetch prices for visible tradable items in this category
                    if (Settings.ShowMarketPrices)
                    {
                        lock (_fetchingPricesLock)
                        {
                            var tradableItems = category.Items.Where(i => i.CanBeTraded && !i.MarketPrice.HasValue && !_fetchingPrices.Contains(i.ItemId)).Take(2);
                            foreach (var item in tradableItems)
                            {
                                _ = FetchMarketPrice(item);
                            }
                        }
                    }
                }
                else
                {
                    ExpandedCategories[category.CategoryId] = false;
                    _expandedCategoriesChanged = true;
                }
            }
            
            // Add small spacing between categories to prevent stretching
            ImGui.Spacing();
        }
    }
    
    private void DrawSearchResultsView(List<CategoryGroup> categories)
    {
        ImGui.Text("Search Results for: ");
        ImGui.SameLine();
        ImGui.TextColored(ColorInfo, $"\"{_searchFilter}\"");
        ImGui.Separator();
        ImGui.Spacing();
        
        // Collect all matching items from all categories
        var allMatchingItems = new List<InventoryItemInfo>();
        foreach (var category in categories)
        {
            allMatchingItems.AddRange(category.Items);
        }
        
        if (!allMatchingItems.Any())
        {
            ImGui.TextColored(ColorSubdued, "No items found in available inventory.");
            ImGui.Spacing();
            ImGui.Text("Items might be:");
            ImGui.BulletText("Protected by active filters (check Protected Items tab)");
            ImGui.BulletText("In your blacklist (check Blacklist Management tab)");
            ImGui.BulletText("Not matching your search term");
            return;
        }
        
        ImGui.Text($"Found {allMatchingItems.Count} items:");
        ImGui.Spacing();
        
        // Show all results in a single table for easy viewing
        using var style = ImRaii.PushStyle(ImGuiStyleVar.CellPadding, new Vector2(4, 2))
                                .Push(ImGuiStyleVar.ItemSpacing, new Vector2(4, 2));
        
        using (var table = ImRaii.Table("SearchResultsTable", Settings.ShowMarketPrices ? 8 : 7, 
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY))
        {
            if (table)
            {
                // Setup columns similar to normal item table
                float checkboxWidth = 22;
                float idWidth = ImGui.CalcTextSize("99999").X + 8;
                float qtyWidth = ImGui.CalcTextSize("999").X + 8;
                float ilvlWidth = ImGui.CalcTextSize("999").X + 8;
                float locationWidth = ImGui.CalcTextSize("P.Saddlebag 9").X + 8;
                float categoryWidth = ImGui.CalcTextSize("Seasonal Miscellany").X + 8;
                
                ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, checkboxWidth);
                ImGui.TableSetupColumn("ID", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoHide, idWidth);
                ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch | ImGuiTableColumnFlags.NoHide);
                ImGui.TableSetupColumn("Qty", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoHide, qtyWidth);
                ImGui.TableSetupColumn("iLvl", ImGuiTableColumnFlags.WidthFixed, ilvlWidth);
                ImGui.TableSetupColumn("Location", ImGuiTableColumnFlags.WidthFixed, locationWidth);
                ImGui.TableSetupColumn("Category", ImGuiTableColumnFlags.WidthFixed, categoryWidth);
                if (Settings.ShowMarketPrices)
                {
                    float priceWidth = ImGui.CalcTextSize("999,999g").X + 8;
                    ImGui.TableSetupColumn("Price", ImGuiTableColumnFlags.WidthFixed, priceWidth);
                }
                
                ImGui.TableSetupScrollFreeze(0, 1); // Freeze header row
                ImGui.TableHeadersRow();
                
                // Sort items by category then name for better organization
                var sortedItems = allMatchingItems
                    .OrderBy(i => i.CategoryName)
                    .ThenBy(i => i.Name)
                    .ToList();
                
                foreach (var item in sortedItems)
                {
                    ImGui.TableNextRow();
                    
                    // Checkbox column
                    ImGui.TableNextColumn();
                    bool isBlacklisted = Settings.BlacklistedItems.Contains(item.ItemId);
                    bool isSelected;
                    lock (_selectedItemsLock)
                    {
                        isSelected = _selectedItems.Contains(item.ItemId);
                    }
                    
                    if (isBlacklisted)
                    {
                        using (var disabled = ImRaii.Disabled())
                        {
                            bool blacklistedCheck = false;
                            ImGui.Checkbox($"##check_{item.GetUniqueKey()}", ref blacklistedCheck);
                        }
                        
                        if (ImGui.IsItemHovered())
                        {
                            ImGui.SetTooltip("This item is blacklisted and cannot be selected");
                        }
                    }
                    else
                    {
                        if (ImGui.Checkbox($"##check_{item.GetUniqueKey()}", ref isSelected))
                        {
                            lock (_selectedItemsLock)
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
                        }
                    }
                    
                    // ID column
                    ImGui.TableNextColumn();
                    ImGui.TextColored(ColorSubdued, item.ItemId.ToString());
                    
                    // Item name with icon
                    ImGui.TableNextColumn();
                    if (item.IconId > 0)
                    {
                        var icon = _iconCache.GetIcon(item.IconId);
                        if (icon != null)
                        {
                            var startY = ImGui.GetCursorPosY();
                            ImGui.SetCursorPosY(startY + 2);
                            ImGui.Image(icon.ImGuiHandle, new Vector2(20, 20));
                            ImGui.SetCursorPosY(startY);
                            ImGui.SameLine(0, 5);
                        }
                    }
                    
                    // Highlight the matching part of the name
                    var itemName = item.Name;
                    var matchIndex = itemName.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase);
                    if (matchIndex >= 0)
                    {
                        if (matchIndex > 0)
                        {
                            ImGui.Text(itemName.Substring(0, matchIndex));
                            ImGui.SameLine(0, 0);
                        }
                        
                        ImGui.TextColored(ColorWarning, itemName.Substring(matchIndex, _searchFilter.Length));
                        ImGui.SameLine(0, 0);
                        
                        if (matchIndex + _searchFilter.Length < itemName.Length)
                        {
                            ImGui.Text(itemName.Substring(matchIndex + _searchFilter.Length));
                        }
                    }
                    else
                    {
                        ImGui.Text(itemName);
                    }
                    
                    if (item.IsHQ)
                    {
                        ImGui.SameLine();
                        ImGui.TextColored(ColorHQItem, "[HQ]");
                    }
                    
                    DrawItemSafetyFlags(item);
                    DrawItemFilterTags(item);
                    
                    // Quantity
                    ImGui.TableNextColumn();
                    ImGui.Text(item.Quantity.ToString());
                    
                    // Item Level
                    ImGui.TableNextColumn();
                    if (item.ItemLevel > 0)
                    {
                        ImGui.Text(item.ItemLevel.ToString());
                    }
                    else
                    {
                        ImGui.TextColored(ColorSubdued, "-");
                    }
                    
                    // Location
                    ImGui.TableNextColumn();
                    ImGui.Text(GetLocationName(item.Container));
                    
                    // Category
                    ImGui.TableNextColumn();
                    ImGui.TextColored(ColorSubdued, item.CategoryName);
                    
                    // Price
                    if (Settings.ShowMarketPrices)
                    {
                        ImGui.TableNextColumn();
                        DrawItemPrice(item);
                    }
                }
            }
        }
    }
    
    private void DrawItemPrice(InventoryItemInfo item)
    {
        if (!item.CanBeTraded)
        {
            ImGui.TextColored(ColorSubdued, "Untradable");
        }
        else if (item.MarketPrice.HasValue)
        {
            if (item.MarketPrice.Value > 0)
            {
                ImGui.TextColored(ColorPrice, $"{item.MarketPrice.Value:N0}g");
            }
            else
            {
                ImGui.TextColored(ColorSubdued, "No data");
            }
        }
        else
        {
            ImGui.TextColored(ColorSubdued, "Loading...");
            // Fetch price if not already fetching
            if (!IsFetchingPrice(item.ItemId))
            {
                _ = FetchMarketPrice(item);
            }
        }
    }
    
    private void DrawCategoryItems(CategoryGroup category)
    {
        // Make table more compact
        using var style = ImRaii.PushStyle(ImGuiStyleVar.CellPadding, new Vector2(4, 2))
                                .Push(ImGuiStyleVar.ItemSpacing, new Vector2(4, 2));
        
        // Remove ScrollY and SizingStretchProp to let table size to content
        using (var table = ImRaii.Table($"ItemTable_{category.CategoryId}", Settings.ShowMarketPrices ? 8 : 7, 
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable))
        {
            if (table)
            {
                // Calculate dynamic widths based on content
                float checkboxWidth = 22;
                float idWidth = ImGui.CalcTextSize("99999").X + 8;
                float qtyWidth = ImGui.CalcTextSize("999").X + 8;
                float ilvlWidth = ImGui.CalcTextSize("999").X + 8;
                float locationWidth = ImGui.CalcTextSize("P.Saddlebag 9").X + 8;
                
                ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, checkboxWidth);
                ImGui.TableSetupColumn("ID", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoHide, idWidth);
                ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch | ImGuiTableColumnFlags.NoHide); // Back to stretch
                ImGui.TableSetupColumn("Qty", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoHide, qtyWidth);
                ImGui.TableSetupColumn("iLvl", ImGuiTableColumnFlags.WidthFixed, ilvlWidth);
                ImGui.TableSetupColumn("Location", ImGuiTableColumnFlags.WidthFixed, locationWidth);
                if (Settings.ShowMarketPrices)
                {
                    float priceWidth = ImGui.CalcTextSize("999,999g").X + 8;
                    float totalWidth = ImGui.CalcTextSize("9,999,999g").X + 8;
                    ImGui.TableSetupColumn("Price", ImGuiTableColumnFlags.WidthFixed, priceWidth);
                    ImGui.TableSetupColumn("Total", ImGuiTableColumnFlags.WidthFixed, totalWidth);
                }
                else
                {
                    float statusWidth = ImGui.CalcTextSize("Not Discardable").X + 8;
                    ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, statusWidth);
                }
                
                ImGui.TableHeadersRow();
                
                foreach (var item in category.Items)
                {
                    DrawItemRow(item, category);
                }
            }
        }
    }
    
    private void DrawCategoryControls(CategoryGroup category)
    {
        int selectedInCategory;
        bool allSelectableSelected;
        lock (_selectedItemsLock)
        {
            selectedInCategory = category.Items.Count(i => _selectedItems.Contains(i.ItemId));
            // Check if all non-blacklisted items are selected
            var selectableItems = category.Items.Where(i => !Settings.BlacklistedItems.Contains(i.ItemId)).ToList();
            allSelectableSelected = selectableItems.Count > 0 && selectableItems.All(i => _selectedItems.Contains(i.ItemId));
        }
        
        // Show selection info inline with category header
        var buttonText = allSelectableSelected ? "Deselect All" : "Select All";
        
        if (ImGui.SmallButton(buttonText))
        {
            if (allSelectableSelected)
            {
                // Deselect all items in this category
                lock (_selectedItemsLock)
                {
                    foreach (var item in category.Items)
                    {
                        _selectedItems.Remove(item.ItemId);
                        item.IsSelected = false;
                    }
                }
            }
            else
            {
                // Select all non-blacklisted items in this category
                lock (_selectedItemsLock)
                {
                    foreach (var item in category.Items)
                    {
                        // Skip blacklisted items
                        if (Settings.BlacklistedItems.Contains(item.ItemId))
                            continue;
                            
                        _selectedItems.Add(item.ItemId);
                        item.IsSelected = true;
                    }
                }
            }
        }
        
        if (ImGui.IsItemHovered())
        {
            var blacklistedCount = category.Items.Count(i => Settings.BlacklistedItems.Contains(i.ItemId));
            if (blacklistedCount > 0)
            {
                ImGui.SetTooltip($"Blacklisted items ({blacklistedCount}) will not be selected");
            }
        }
        
        // Add warning for dangerous categories
        if (IsDangerousCategory(category))
        {
            ImGui.SameLine();
            using (var font = ImRaii.PushFont(UiBuilder.IconFont))
            {
                ImGui.TextColored(ColorWarning, FontAwesomeIcon.ExclamationTriangle.ToIconString());
            }
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
        using var id = ImRaii.PushId(item.GetUniqueKey());
        
        // Check if this row is selected
        bool isSelected;
        lock (_selectedItemsLock)
        {
            isSelected = _selectedItems.Contains(item.ItemId);
        }
        
        // Check if item is blacklisted
        bool isBlacklisted = Settings.BlacklistedItems.Contains(item.ItemId);
        
        // Apply selection or blacklist background using table row bg
        if (isBlacklisted)
        {
            // Dark red tint for blacklisted items
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(new Vector4(0.3f, 0.1f, 0.1f, 0.3f)));
        }
        else if (isSelected)
        {
            // Blue tint for selected items
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(new Vector4(0.3f, 0.5f, 0.7f, 0.3f)));
        }
        
        // Checkbox column
        ImGui.TableNextColumn();
        
        if (isBlacklisted)
        {
            // Show disabled checkbox for blacklisted items
            using (var disabled = ImRaii.Disabled())
            {
                bool blacklistedCheck = false;
                ImGui.Checkbox($"##check_{item.GetUniqueKey()}", ref blacklistedCheck);
            }
            
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("This item is blacklisted and cannot be selected");
            }
        }
        else
        {
            if (ImGui.Checkbox($"##check_{item.GetUniqueKey()}", ref isSelected))
            {
                lock (_selectedItemsLock)
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
            }
        }
        
        // Item ID column
        ImGui.TableNextColumn();
        ImGui.TextColored(ColorSubdued, item.ItemId.ToString());
        
        // Item name with icon
        ImGui.TableNextColumn();
        
        // Item icon and name aligned properly
        var iconSize = new Vector2(20, 20);
        if (item.IconId > 0)
        {
            var icon = _iconCache.GetIcon(item.IconId);
            if (icon != null)
            {
                // Lower the icon to align with text baseline
                var startY = ImGui.GetCursorPosY();
                ImGui.SetCursorPosY(startY - 2);  // Lower the icon by 2 pixels
                ImGui.Image(icon.ImGuiHandle, iconSize);
                ImGui.SetCursorPosY(startY);
                ImGui.SameLine(0, 5);
            }
            else
            {
                // Reserve space for missing icon
                ImGui.Dummy(iconSize);
                ImGui.SameLine(0, 5);
            }
        }
        else
        {
            // Reserve space for missing icon
            ImGui.Dummy(iconSize);
            ImGui.SameLine(0, 5);
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
        
        // Item Level
        ImGui.TableNextColumn();
        if (item.ItemLevel > 0)
        {
            ImGui.Text(item.ItemLevel.ToString());
        }
        else
        {
            ImGui.TextColored(ColorSubdued, "-");
        }
        
        // Location
        ImGui.TableNextColumn();
        ImGui.Text(GetLocationName(item.Container));
        
        if (Settings.ShowMarketPrices && item.CanBeTraded)
        {
            // Unit price
            ImGui.TableNextColumn();
            
            bool isFetching;
            lock (_fetchingPricesLock)
            {
                isFetching = _fetchingPrices.Contains(item.ItemId);
            }
            
            if (isFetching)
            {
                // Show loading spinner
                using (var font = ImRaii.PushFont(UiBuilder.IconFont))
                {
                    var time = ImGui.GetTime();
                    var spinnerIcon = time % 1.0 < 0.5 ? FontAwesomeIcon.CircleNotch : FontAwesomeIcon.Circle;
                    ImGui.TextColored(ColorLoading, spinnerIcon.ToIconString());
                }
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
                    using (var style = ImRaii.PushStyle(ImGuiStyleVar.FramePadding, new Vector2(2, 0)))
                    using (var font = ImRaii.PushFont(UiBuilder.IconFont))
                    {
                        if (ImGui.SmallButton(FontAwesomeIcon.DollarSign.ToIconString() + $"##fetch_{item.GetUniqueKey()}"))
                        {
                            _ = FetchMarketPrice(item);
                        }
                    }
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
            
            if (isBlacklisted)
            {
                ImGui.TextColored(ColorError, "Blacklisted");
            }
            else if (!item.CanBeTraded)
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
        }
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
            
            using (var color = ImRaii.PushColor(ImGuiCol.Text, tagColor))
            {
                var shortName = GetShortFilterName(filterName);
                ImGui.Text($"[{shortName}]");
                
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(isActive ? 
                        $"HIDDEN by '{filterName}' filter" :
                        $"Would be hidden by '{filterName}' filter (disabled)");
                }
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
        
        using (var color = ImRaii.PushColor(ImGuiCol.Text, flagColor))
        {
            var flagText = string.Join(", ", additionalFlags);
            ImGui.Text($"[{flagText}]");
            
            if (ImGui.IsItemHovered())
            {
                using (var tooltip = ImRaii.Tooltip())
                {
                    ImGui.Text("Additional Safety Info:");
                    foreach (var flag in additionalFlags)
                    {
                        ImGui.BulletText(flag);
                    }
                }
            }
        }
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
        
        if (item.IsUnique && item.IsUntradable)
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
        // Section 1: Items filtered by current filter settings
        ImGui.Text("Items Protected by Active Filters:");
        ImGui.Spacing();
        
        if (!filteredItems.Any())
        {
            ImGui.TextColored(new Vector4(0.6f, 0.8f, 0.6f, 1), "No items are currently being filtered out.");
            ImGui.Text("All items in your inventory are available for selection.");
        }
        else
        {
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
                
                // Use unique ID based on category ID and filtered prefix
                using var id = ImRaii.PushId($"FilteredCategory_{category.CategoryId}");
                
                var categoryHeaderText = $"{category.CategoryName} ({category.Items.Count} protected)";
                
                // Use TreeNodeEx for consistent behavior with Available Items - remove Framed
                var nodeFlags = ImGuiTreeNodeFlags.AllowItemOverlap | ImGuiTreeNodeFlags.SpanAvailWidth;
                if (isExpanded) nodeFlags |= ImGuiTreeNodeFlags.DefaultOpen;
                
                using (var color = ImRaii.PushColor(ImGuiCol.Text, new Vector4(0.8f, 0.6f, 0.6f, 1)))
                {
                    using (var node = ImRaii.TreeNode($"{categoryHeaderText}###{category.CategoryId}_filtered_node", nodeFlags))
                    {
                        if (node)
                        {
                            ExpandedCategories[category.CategoryId] = true;
                            _expandedCategoriesChanged = true;
                            
                            DrawFilteredItemsTable(category.Items);
                        }
                        else
                        {
                            ExpandedCategories[category.CategoryId] = false;
                            _expandedCategoriesChanged = true;
                        }
                    }
                }
                
                // Add small spacing between categories to prevent stretching
                ImGui.Spacing();
            }
        }
        
        // Section 2: Built-in protected lists
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        
        DrawBuiltInProtectedLists();
    }
    
    private void DrawBuiltInProtectedLists()
    {
        ImGui.Text("Built-in Protected Lists (Always Active):");
        ImGui.Spacing();
        
        // Ultimate tokens and special items
        var ultimateNodeFlags = ImGuiTreeNodeFlags.AllowItemOverlap | ImGuiTreeNodeFlags.SpanAvailWidth;
        using (var id = ImRaii.PushId("UltimateTokensProtected"))
        {
            using (var node = ImRaii.TreeNode($"Ultimate Tokens & Special Items ({InventoryHelpers.HardcodedBlacklist.Count} items)##UltimateTokensProtectedHeader", ultimateNodeFlags))
            {
                if (node)
                {
                    DrawBuiltInItemsTable(InventoryHelpers.HardcodedBlacklist, "HardcodedListProtected");
                }
            }
        }
        
        ImGui.Spacing();
        
        // Currency items
        var currencyNodeFlags = ImGuiTreeNodeFlags.AllowItemOverlap | ImGuiTreeNodeFlags.SpanAvailWidth;
        using (var id = ImRaii.PushId("CurrencyItemsProtected"))
        {
            using (var node = ImRaii.TreeNode($"Currency Items (IDs 1-99)##CurrencyItemsProtectedHeader", currencyNodeFlags))
            {
                if (node)
                {
                    ImGui.TextWrapped("All items with IDs from 1 to 99 are protected as currency items.");
                    ImGui.Spacing();
                    
                    // Show a sample of currency items
                    var currencyItems = new HashSet<uint>();
                    for (uint i = 1; i <= 20; i++) // Just show first 20 as example
                    {
                        currencyItems.Add(i);
                    }
                    DrawBuiltInItemsTable(currencyItems, "CurrencyListProtected");
                    ImGui.Text("... and 79 more currency items");
                }
            }
        }
        
        ImGui.Spacing();
    }
    
    private void DrawBuiltInItemsTable(IEnumerable<uint> itemIds, string tableId)
    {
        using var style = ImRaii.PushStyle(ImGuiStyleVar.CellPadding, new Vector2(4, 2))
                                .Push(ImGuiStyleVar.ItemSpacing, new Vector2(4, 2));
        
        using (var table = ImRaii.Table($"BuiltInTable_{tableId}", 4, 
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable))
        {
            if (table)
            {
                // Use dynamic widths based on content
                float idWidth = ImGui.CalcTextSize("99999").X + 8;
                float ilvlWidth = ImGui.CalcTextSize("999").X + 8;
                float categoryWidth = ImGui.CalcTextSize("Seasonal Miscellany").X + 8;
                
                ImGui.TableSetupColumn("ID", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoHide, idWidth);
                ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch | ImGuiTableColumnFlags.NoHide);
                ImGui.TableSetupColumn("iLvl", ImGuiTableColumnFlags.WidthFixed, ilvlWidth);
                ImGui.TableSetupColumn("Category", ImGuiTableColumnFlags.WidthFixed, categoryWidth);
                ImGui.TableHeadersRow();
                
                foreach (var itemId in itemIds)
                {
                    // Try to find item info from inventory first
                    InventoryItemInfo itemInfo = null;
                    lock (_itemsLock)
                    {
                        itemInfo = _allItems.FirstOrDefault(i => i.ItemId == itemId);
                    }
                    string itemName = itemInfo?.Name;
                    string categoryName = itemInfo?.CategoryName ?? "Unknown";
                    ushort iconId = itemInfo?.IconId ?? 0;
                    int itemLevel = (int)(itemInfo?.ItemLevel ?? 0);
                    
                    // If not in inventory, try to get from game data
                    if (string.IsNullOrEmpty(itemName))
                    {
                        try
                        {
                            var itemSheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>();
                            if (itemSheet != null)
                            {
                                var gameItem = itemSheet.GetRowOrDefault(itemId);
                                if (gameItem != null && gameItem.Value.RowId != 0)
                                {
                                    itemName = gameItem.Value.Name.ExtractText();
                                    iconId = gameItem.Value.Icon;
                                    categoryName = GetItemCategoryName(gameItem.Value.ItemUICategory.RowId);
                                    itemLevel = (int)gameItem.Value.LevelItem.RowId;
                                }
                            }
                        }
                        catch { }
                    }
                    
                    // Fallback to hardcoded names for known items
                    if (string.IsNullOrEmpty(itemName))
                    {
                        itemName = GetItemNameFromBlacklist(itemId);
                    }
                    
                    ImGui.TableNextRow();
                    
                    // ID column
                    ImGui.TableNextColumn();
                    ImGui.TextColored(ColorSubdued, itemId.ToString());
                    
                    // Name column with icon
                    ImGui.TableNextColumn();
                    if (iconId > 0)
                    {
                        var icon = _iconCache.GetIcon(iconId);
                        if (icon != null)
                        {
                            // Lower the icon to align with text baseline
                            var startY = ImGui.GetCursorPosY();
                            ImGui.SetCursorPosY(startY - 2);
                            ImGui.Image(icon.ImGuiHandle, new Vector2(20, 20));
                            ImGui.SetCursorPosY(startY);
                            ImGui.SameLine(0, 5);
                        }
                        else
                        {
                            ImGui.Dummy(new Vector2(20, 20));
                            ImGui.SameLine(0, 5);
                        }
                    }
                    else
                    {
                        ImGui.Dummy(new Vector2(20, 20));
                        ImGui.SameLine(0, 5);
                    }
                    ImGui.Text(itemName);
                    
                    // Item Level column
                    ImGui.TableNextColumn();
                    if (itemLevel > 0)
                    {
                        ImGui.Text(itemLevel.ToString());
                    }
                    else
                    {
                        ImGui.TextColored(ColorSubdued, "-");
                    }
                    
                    // Category column
                    ImGui.TableNextColumn();
                    ImGui.Text(categoryName);
                }
            }
        }
    }
    
    private string GetItemNameFromBlacklist(uint itemId)
    {
        // Helper to get item names from the comments in InventoryHelpers.cs
        return itemId switch
        {
            16039 => "Ala Mhigan Earrings",
            24589 => "Aetheryte Earrings",
            33648 => "Menphina's Earrings",
            41081 => "Azeyma's Earrings",
            _ => $"Unknown Item ({itemId})"
        };
    }
    
    private void DrawFilteredItemsTable(List<InventoryItemInfo> items)
    {
        using var style = ImRaii.PushStyle(ImGuiStyleVar.CellPadding, new Vector2(4, 2))
                                .Push(ImGuiStyleVar.ItemSpacing, new Vector2(4, 2));
        
        // Remove unnecessary table flags that cause extra space
        using (var table = ImRaii.Table("FilteredItemsTable", Settings.ShowMarketPrices ? 7 : 6, 
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable))
        {
            if (table)
            {
                // Calculate dynamic widths based on content
                float idWidth = ImGui.CalcTextSize("99999").X + 8;
                float qtyWidth = ImGui.CalcTextSize("999").X + 8;
                float ilvlWidth = ImGui.CalcTextSize("999").X + 8;
                float locationWidth = ImGui.CalcTextSize("P.Saddlebag 9").X + 8;
                
                ImGui.TableSetupColumn("ID", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoHide, idWidth);
                ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch | ImGuiTableColumnFlags.NoHide); // Back to stretch
                ImGui.TableSetupColumn("Qty", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoHide, qtyWidth);
                ImGui.TableSetupColumn("iLvl", ImGuiTableColumnFlags.WidthFixed, ilvlWidth);
                ImGui.TableSetupColumn("Location", ImGuiTableColumnFlags.WidthFixed, locationWidth);
                if (Settings.ShowMarketPrices)
                {
                    float priceWidth = ImGui.CalcTextSize("999,999g").X + 8;
                    float totalWidth = ImGui.CalcTextSize("9,999,999g").X + 8;
                    ImGui.TableSetupColumn("Price", ImGuiTableColumnFlags.WidthFixed, priceWidth);
                    ImGui.TableSetupColumn("Total", ImGuiTableColumnFlags.WidthFixed, totalWidth);
                }
                else
                {
                    float reasonWidth = ImGui.CalcTextSize("Unique & Untradeable").X + 8;
                    ImGui.TableSetupColumn("Reason", ImGuiTableColumnFlags.WidthFixed, reasonWidth);
                }
                
                ImGui.TableHeadersRow();
                
                foreach (var item in items)
                {
                    DrawFilteredItemRow(item);
                }
            }
        }
    }
    
    private void DrawFilteredItemRow(InventoryItemInfo item)
    {
        ImGui.TableNextRow();
        using var id = ImRaii.PushId(item.GetUniqueKey());
        
        // ID column
        ImGui.TableNextColumn();
        ImGui.TextColored(ColorSubdued, item.ItemId.ToString());
        
        // Item name with icon
        ImGui.TableNextColumn();
        
        // Icon - same styling as available items
        var iconSize = new Vector2(20, 20);
        if (item.IconId > 0)
        {
            var icon = _iconCache.GetIcon(item.IconId);
            if (icon != null)
            {
                // Lower the icon to align with text baseline
                var startY = ImGui.GetCursorPosY();
                ImGui.SetCursorPosY(startY - 2);  // Lower the icon by 2 pixels
                ImGui.Image(icon.ImGuiHandle, iconSize);
                ImGui.SetCursorPosY(startY);
                ImGui.SameLine(0, 5);
            }
            else
            {
                // Reserve space for missing icon
                ImGui.Dummy(iconSize);
                ImGui.SameLine(0, 5);
            }
        }
        else
        {
            // Reserve space for missing icon  
            ImGui.Dummy(iconSize);
            ImGui.SameLine(0, 5);
        }
        
        ImGui.Text(item.Name);
        
        // Add filter tags right after item name - same as available items
        DrawItemFilterTags(item);
        
        if (item.IsHQ)
        {
            ImGui.SameLine();
            ImGui.TextColored(ColorHQItem, "[HQ]");
        }
        
        // Quantity
        ImGui.TableNextColumn();
        ImGui.Text(item.Quantity.ToString());
        
        // Item Level
        ImGui.TableNextColumn();
        if (item.ItemLevel > 0)
        {
            ImGui.Text(item.ItemLevel.ToString());
        }
        else
        {
            ImGui.TextColored(ColorSubdued, "-");
        }
        
        // Location
        ImGui.TableNextColumn();
        ImGui.Text(GetLocationName(item.Container));
        
        if (Settings.ShowMarketPrices)
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
                ImGui.TextColored(ColorSubdued, priceText);
            }
            
            // Total value
            ImGui.TableNextColumn();
            if (item.MarketPrice.HasValue && item.MarketPrice.Value > 0)
            {
                var total = item.MarketPrice.Value * item.Quantity;
                ImGui.Text($"{total:N0}g");
            }
            else
            {
                ImGui.TextColored(ColorSubdued, "---");
            }
        }
        else
        {
            // Reason
            ImGui.TableNextColumn();
            ImGui.TextColored(ColorWarning, GetFilterReason(item));
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
        if (filters.FilterUniqueUntradeable && item.IsUnique && item.IsUntradable)
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
        // NOTE: This method should be called inside a lock(_itemsLock)
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
            else if (filters.FilterUniqueUntradeable && item.IsUnique && item.IsUntradable)
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
        using var style = ImRaii.PushStyle(ImGuiStyleVar.FramePadding, new Vector2(8, 6));
        using var color = ImRaii.PushColor(ImGuiCol.ChildBg, new Vector4(0.165f, 0.165f, 0.165f, 1f));
        
        using (var child = ImRaii.Child("ActionBar", new Vector2(0, 42), true, ImGuiWindowFlags.NoScrollbar))
        {
            // Left side - Action buttons
            int selectedCount;
            lock (_selectedItemsLock)
            {
                selectedCount = _selectedItems.Count;
            }
            
            // Calculate dynamic button widths based on text
            var clearButtonText = "Clear All";
            var discardButtonText = $"Discard ({selectedCount})";
            var blacklistButtonText = $"Add to Blacklist ({selectedCount})";
            
            // Calculate minimum widths based on text size with padding
            var buttonPadding = 20f; // Extra padding for button aesthetics
            var clearButtonWidth = Math.Max(80f, ImGui.CalcTextSize(clearButtonText).X + buttonPadding);
            var discardButtonWidth = Math.Max(80f, ImGui.CalcTextSize(discardButtonText).X + buttonPadding);
            var blacklistButtonWidth = Math.Max(120f, ImGui.CalcTextSize(blacklistButtonText).X + buttonPadding);
            
            if (ImGui.Button(clearButtonText, new Vector2(clearButtonWidth, 0)))
            {
                lock (_selectedItemsLock)
                {
                    _selectedItems.Clear();
                }
                lock (_itemsLock)
                {
                    foreach (var item in _allItems)
                    {
                        item.IsSelected = false;
                    }
                }
            }
            
            ImGui.SameLine();
            
            using (var btnColors = ImRaii.PushColor(ImGuiCol.Button, new Vector4(0.541f, 0.227f, 0.227f, 1f))
                                         .Push(ImGuiCol.ButtonHovered, new Vector4(0.641f, 0.327f, 0.327f, 1f)))
            {
                if (selectedCount > 0)
                {
                    if (ImGui.Button(discardButtonText, new Vector2(discardButtonWidth, 0)))
                    {
                        PrepareDiscard();
                    }
                }
                else
                {
                    using (var disabled = ImRaii.Disabled())
                    {
                        ImGui.Button(discardButtonText, new Vector2(discardButtonWidth, 0));
                    }
                }
            }
            
            ImGui.SameLine();
            
            using (var btnColors = ImRaii.PushColor(ImGuiCol.Button, new Vector4(0.227f, 0.227f, 0.541f, 1f))
                                         .Push(ImGuiCol.ButtonHovered, new Vector4(0.327f, 0.327f, 0.641f, 1f)))
            {
                if (selectedCount > 0)
                {
                    if (ImGui.Button(blacklistButtonText, new Vector2(blacklistButtonWidth, 0)))
                    {
                        AddSelectedToBlacklist();
                    }
                }
                else
                {
                    using (var disabled = ImRaii.Disabled())
                    {
                        ImGui.Button(blacklistButtonText, new Vector2(blacklistButtonWidth, 0));
                    }
                }
            }
            
            // Visual separator
            ImGui.SameLine();
            ImGui.Text(" ");
            
            // Center/Right side - Statistics
            long totalValue;
            int availableItems;
            lock (_categoriesLock)
            {
                totalValue = _categories.Sum(c => c.TotalValue ?? 0);
                availableItems = _categories.Sum(c => c.Items.Count);
            }
            int protectedItems;
            lock (_itemsLock)
            {
                protectedItems = GetFilteredOutItems().Count;
            }
            
            // Right-align statistics with tighter spacing
            var windowWidth = ImGui.GetWindowContentRegionMax().X;
            
            // Calculate positions for each stat group
            var protectedPos = windowWidth - 100;
            var availablePos = protectedPos - 100;
            var totalValuePos = availablePos - 130;
            
            ImGui.SameLine(totalValuePos);
            ImGui.Text("Total:");
            ImGui.SameLine();
            ImGui.TextColored(ColorPrice, $"{totalValue:N0} gil");
            
            ImGui.SameLine(availablePos);
            ImGui.Text("Available:");
            ImGui.SameLine();
            ImGui.TextColored(ColorSuccess, availableItems.ToString());
            
            ImGui.SameLine(protectedPos);
            ImGui.Text("Protected:");
            ImGui.SameLine();
            ImGui.TextColored(ColorWarning, protectedItems.ToString());
        }
    }
    
    private void AddSelectedToBlacklist()
    {
        List<uint> itemsToAdd;
        lock (_selectedItemsLock)
        {
            itemsToAdd = new List<uint>(_selectedItems);
        }
        
        int addedCount = 0;
        foreach (var itemId in itemsToAdd)
        {
            if (!Settings.BlacklistedItems.Contains(itemId))
            {
                Settings.BlacklistedItems.Add(itemId);
                addedCount++;
            }
        }
        
        if (addedCount > 0)
        {
            _plugin.Configuration.Save();
            
            // Clear selections
            lock (_selectedItemsLock)
            {
                _selectedItems.Clear();
            }
            lock (_itemsLock)
            {
                foreach (var item in _allItems)
                {
                    item.IsSelected = false;
                }
            }
            
            RefreshInventory();
            Plugin.ChatGui.Print($"Added {addedCount} items to blacklist.");
        }
        else
        {
            Plugin.ChatGui.PrintError("All selected items are already blacklisted.");
        }
    }
}
