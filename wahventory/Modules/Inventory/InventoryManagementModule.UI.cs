using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Bindings.ImGui;
using wahventory.Services.Tasks;
using wahventory.Services.External;
using wahventory.Models;
using wahventory.Core;
using FFXIVClientStructs.FFXIV.Client.Game;

namespace wahventory.Modules.Inventory;

/// <summary>
/// UI methods for the refactored InventoryManagementModule
/// </summary>
public partial class InventoryManagementModule
{
    private void DrawTopControls()
    {
        using var style = ImRaii.PushStyle(ImGuiStyleVar.FramePadding, new Vector2(6, 5))
                                .Push(ImGuiStyleVar.ItemSpacing, new Vector2(8, 4));
        using var color = ImRaii.PushColor(ImGuiCol.ChildBg, new Vector4(0.145f, 0.145f, 0.145f, 1f));
        
        using (var child = ImRaii.Child("TopBar", new Vector2(0, 40), true, ImGuiWindowFlags.NoScrollbar))
        {
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 2);
            
            // Search section
            using (var font = ImRaii.PushFont(UiBuilder.IconFont))
            {
                ImGui.Text(FontAwesomeIcon.Search.ToIconString());
            }
            
            ImGui.SameLine();
            ImGui.SetNextItemWidth(180f);
            if (ImGui.InputTextWithHint("##Search", "Search items...", ref _searchFilter, 100))
            {
                _taskCoordinator.InventoryTasks.EnqueueCategoryUpdate(_searchFilter);
            }
            
            if (!string.IsNullOrWhiteSpace(_searchFilter))
            {
                ImGui.SameLine();
                using (var colors = ImRaii.PushColor(ImGuiCol.Button, new Vector4(0.3f, 0.3f, 0.3f, 0.3f))
                                          .Push(ImGuiCol.ButtonHovered, new Vector4(0.4f, 0.4f, 0.4f, 0.5f)))
                {
                    if (ImGui.SmallButton("×"))
                    {
                        _searchFilter = string.Empty;
                        _taskCoordinator.InventoryTasks.RefreshInventoryAndCategories(_showArmory);
                    }
                }
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Clear search");
                }
            }
            
            ImGui.SameLine();
            
            // Refresh button
            using (var font = ImRaii.PushFont(UiBuilder.IconFont))
            {
                if (ImGui.Button(FontAwesomeIcon.Sync.ToIconString() + "##Refresh", new Vector2(28, 0)))
                {
                    _taskCoordinator.RefreshAll(_showArmory, _searchFilter);
                }
            }
            ImGui.SameLine();
            ImGui.Text("Refresh");
            
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.3f, 0.3f, 0.3f, 1f), "|");
            
            // Armory checkbox
            ImGui.SameLine();
            if (ImGui.Checkbox("Armory", ref _showArmory)) 
            {
                // Match original behavior exactly
                _taskCoordinator.InventoryTasks.RefreshInventoryAndCategories(_showArmory);
            }
            
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.3f, 0.3f, 0.3f, 1f), "|");
            
            // Show prices checkbox
            ImGui.SameLine();
            var showPrices = Settings.ShowMarketPrices;
            if (ImGui.Checkbox("Show Prices", ref showPrices))
            {
                Settings.ShowMarketPrices = showPrices;
                _plugin.ConfigManager.SaveConfiguration();
            }
            
            // World selection and total value
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
                                // Update the UniversalisClient in the TaskCoordinator
                                var newClient = new UniversalisClient(Plugin.Log, _selectedWorld);
                                _taskCoordinator.UpdateUniversalisClient(newClient);
                            }
                        }
                    }
                }
                
                // Total value display
                var windowWidth = ImGui.GetWindowContentRegionMax().X;
                var totalValue = _cachedCategories.Sum(c => c.TotalValue ?? 0);
                var totalText = $"Total: {totalValue:N0} gil";
                var totalTextWidth = ImGui.CalcTextSize(totalText).X;
                ImGui.SameLine(windowWidth - totalTextWidth);
                
                using (var font = ImRaii.PushFont(UiBuilder.IconFont))
                {
                    ImGui.TextColored(ColorWarning, FontAwesomeIcon.Coins.ToIconString());
                }
                ImGui.SameLine(0, 4);
                ImGui.TextColored(ColorPrice, $"{totalValue:N0} gil");
            }
        }
        
        ImGui.Spacing();
    }
    
    private void DrawFiltersAndSettings()
    {
        using var style = ImRaii.PushStyle(ImGuiStyleVar.FramePadding, new Vector2(6, 5));
        using var color = ImRaii.PushColor(ImGuiCol.ChildBg, new Vector4(0.145f, 0.145f, 0.145f, 1f));
        
        using (var child = ImRaii.Child("FiltersSection", new Vector2(0, 130), true))
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
        
        // First column
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
        
        // Second column
        var filterCrystals = filters.FilterCrystalsAndShards;
        if (DrawFilterItem("Crystals & Shards", ref filterCrystals, "Crafting materials", "?"))
        {
            filters.FilterCrystalsAndShards = filterCrystals;
            changed = true;
        }
        
        // High level gear filter with input
        var filterHighLevel = filters.FilterHighLevelGear;
        if (ImGui.Checkbox($"##FilterHighLevel", ref filterHighLevel))
        {
            filters.FilterHighLevelGear = filterHighLevel;
            changed = true;
        }
        
        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.Text("High Level Gear (");
        ImGui.SameLine(0, 0);
        ImGui.TextColored(ColorWarning, "i");
        ImGui.SameLine(0, 2);
        
        using (var styles = ImRaii.PushStyle(ImGuiStyleVar.FramePadding, new Vector2(4, 2))
                                  .Push(ImGuiStyleVar.FrameBorderSize, 0))
        using (var colors = ImRaii.PushColor(ImGuiCol.FrameBg, new Vector4(0.2f, 0.2f, 0.2f, 0.5f))
                                  .Push(ImGuiCol.FrameBgHovered, new Vector4(0.3f, 0.3f, 0.3f, 0.6f))
                                  .Push(ImGuiCol.Text, ColorWarning))
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
        ImGui.TextColored(ColorWarning, "+");
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
        
        var filterCollectable = filters.FilterCollectables;
        if (DrawFilterItem("Collectables", ref filterCollectable, "Turn-in items", "?"))
        {
            filters.FilterCollectables = filterCollectable;
            changed = true;
        }
        
        ImGui.NextColumn();
        
        // Third column
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
            _plugin.ConfigManager.SaveConfiguration();
            _taskCoordinator.InventoryTasks.RefreshInventoryAndCategories(_showArmory);
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
    
    private void DrawAvailableItemsTab(string tabName, float contentHeight)
    {
        using (var tabItem = ImRaii.TabItem(tabName))
        {
            if (tabItem)
            {
                using (var child = ImRaii.Child("AvailableContent", new Vector2(0, contentHeight), false))
                {
                    DrawAvailableItemsContent();
                }
            }
        }
    }
    
    private void DrawAvailableItemsContent()
    {
        var categories = _cachedCategories;
        
        if (!string.IsNullOrWhiteSpace(_searchFilter))
        {
            DrawSearchResultsView(categories);
            return;
        }
        
        foreach (var category in categories)
        {
            if (category.Items.Count == 0) continue;
            
            var isExpanded = ExpandedCategories.GetValueOrDefault(category.CategoryId, true);
            using var id = ImRaii.PushId($"Category_{category.CategoryId}");
            var nodeFlags = ImGuiTreeNodeFlags.AllowItemOverlap | ImGuiTreeNodeFlags.SpanAvailWidth;
            if (isExpanded) nodeFlags |= ImGuiTreeNodeFlags.DefaultOpen;
            
            using (var node = ImRaii.TreeNode($"{category.Name}###{category.CategoryId}_node", nodeFlags))
            {
                ImGui.SameLine();
                ImGui.TextColored(ColorInfo, $"({category.Items.Count} items, {category.TotalQuantity} total)");
                
                if (Settings.ShowMarketPrices && category.TotalValue.HasValue)
                {
                    ImGui.SameLine();
                    ImGui.TextColored(ColorPrice, $"{category.TotalValue.Value:N0} gil");
                }
                
                var selectAllWidth = 90f;
                var windowWidth = ImGui.GetWindowContentRegionMax().X;
                ImGui.SameLine(windowWidth - selectAllWidth - 10);
                DrawCategoryControls(category);
                
                if (node)
                {
                    ExpandedCategories[category.CategoryId] = true;
                    _expandedCategoriesChanged = true;
                    DrawCategoryItems(category);
                    
                    // Queue price fetching for visible items
                    if (Settings.ShowMarketPrices)
                    {
                        var tradableItems = category.Items.Where(i => i.CanBeTraded && !i.MarketPrice.HasValue).Take(2);
                        _taskCoordinator.PriceTasks.EnqueueBatchPriceFetch(tradableItems);
                    }
                }
                else
                {
                    ExpandedCategories[category.CategoryId] = false;
                    _expandedCategoriesChanged = true;
                }
            }
            
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
        
        // Draw search results table
        DrawItemsTable(allMatchingItems, "SearchResultsTable");
    }
    
    private void DrawCategoryItems(CategoryGroup category)
    {
        DrawItemsTable(category.Items, $"ItemTable_{category.CategoryId}");
    }
    
    private void DrawItemsTable(List<InventoryItemInfo> items, string tableId)
    {
        using var style = ImRaii.PushStyle(ImGuiStyleVar.CellPadding, new Vector2(4, 2))
                                .Push(ImGuiStyleVar.ItemSpacing, new Vector2(4, 2));
        
        using (var table = ImRaii.Table(tableId, Settings.ShowMarketPrices ? 8 : 7, 
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable))
        {
            if (table)
            {
                SetupItemTableColumns();
                ImGui.TableHeadersRow();
                
                foreach (var item in items)
                {
                    DrawItemRow(item);
                }
            }
        }
    }
    
    private void SetupItemTableColumns()
    {
        float checkboxWidth = 22;
        float idWidth = ImGui.CalcTextSize("99999").X + 8;
        float qtyWidth = ImGui.CalcTextSize("999").X + 8;
        float ilvlWidth = ImGui.CalcTextSize("999").X + 8;
        float locationWidth = ImGui.CalcTextSize("P.Saddlebag 9").X + 8;
        
        ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, checkboxWidth);
        ImGui.TableSetupColumn("ID", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoHide, idWidth);
        ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch | ImGuiTableColumnFlags.NoHide);
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
    }
    
    private void DrawItemRow(InventoryItemInfo item)
    {
        ImGui.TableNextRow();
        using var id = ImRaii.PushId(item.GetUniqueKey());
        
        bool isSelected;
        lock (_selectedItemsLock)
        {
            isSelected = _selectedItems.Contains(item.ItemId);
        }
        bool isBlacklisted = _taskCoordinator.BlacklistedItems.Contains(item.ItemId);
        
        // Row highlighting
        if (isBlacklisted)
        {
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(new Vector4(0.3f, 0.1f, 0.1f, 0.3f)));
        }
        else if (isSelected)
        {
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(new Vector4(0.3f, 0.5f, 0.7f, 0.3f)));
        }
        
        // Checkbox column
        ImGui.TableNextColumn();
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
        
        // Item name and icon column
        ImGui.TableNextColumn();
        var iconSize = new Vector2(20, 20);
        if (item.IconId > 0)
        {
            var icon = _iconCache.GetIcon(item.IconId);
            if (icon != null)
            {
                var startY = ImGui.GetCursorPosY();
                ImGui.SetCursorPosY(startY - 2);
                ImGui.Image(icon.Handle, iconSize);
                ImGui.SetCursorPosY(startY);
                ImGui.SameLine(0, 5);
            }
            else
            {
                ImGui.Dummy(iconSize);
                ImGui.SameLine(0, 5);
            }
        }
        else
        {
            ImGui.Dummy(iconSize);
            ImGui.SameLine(0, 5);
        }
        
        ImGui.Text(item.Name);
        
        if (item.IsHQ)
        {
            ImGui.SameLine();
            ImGui.TextColored(ColorHQItem, "[HQ]");
        }
        if (isBlacklisted)
        {
            ImGui.SameLine();
            ImGui.TextColored(ColorError, "[Blacklisted]");
        }
        if (!item.CanBeTraded)
        {
            ImGui.SameLine();
            ImGui.TextColored(ColorNotTradeable, "[Not Tradeable]");
        }
        
        // Quantity column
        ImGui.TableNextColumn();
        ImGui.Text(item.Quantity.ToString());
        
        // Item level column
        ImGui.TableNextColumn();
        if (item.ItemLevel > 0)
        {
            ImGui.Text(item.ItemLevel.ToString());
        }
        else
        {
            ImGui.TextColored(ColorSubdued, "-");
        }
        
        // Location column
        ImGui.TableNextColumn();
        ImGui.Text(GetLocationName(item.Container));
        
        // Price/Status columns
        if (Settings.ShowMarketPrices && item.CanBeTraded)
        {
            ImGui.TableNextColumn();
            DrawItemPrice(item);
            
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
            ImGui.TableNextColumn();
            DrawItemStatus(item);
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
            // Queue price fetch if not already fetching
            _taskCoordinator.PriceTasks.EnqueuePriceFetch(item.ItemId, item.IsHQ);
        }
    }
    
    private void DrawItemStatus(InventoryItemInfo item)
    {
        if (!item.CanBeDiscarded)
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
        else
        {
            ImGui.TextColored(ColorSubdued, "Normal");
        }
    }
    
    private void DrawCategoryControls(CategoryGroup category)
    {
        int selectedInCategory;
        bool allSelectableSelected;
        lock (_selectedItemsLock)
        {
            selectedInCategory = category.Items.Count(i => _selectedItems.Contains(i.ItemId));
            var selectableItems = category.Items.Where(i => !_taskCoordinator.BlacklistedItems.Contains(i.ItemId)).ToList();
            allSelectableSelected = selectableItems.Count > 0 && selectableItems.All(i => _selectedItems.Contains(i.ItemId));
        }
        
        var buttonText = allSelectableSelected ? "Deselect All" : "Select All";
        
        if (ImGui.SmallButton(buttonText))
        {
            if (allSelectableSelected)
            {
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
                lock (_selectedItemsLock)
                {
                    foreach (var item in category.Items)
                    {
                        if (_taskCoordinator.BlacklistedItems.Contains(item.ItemId))
                            continue;
                            
                        _selectedItems.Add(item.ItemId);
                        item.IsSelected = true;
                    }
                }
            }
        }
        
        if (ImGui.IsItemHovered())
        {
            var blacklistedCount = category.Items.Count(i => _taskCoordinator.BlacklistedItems.Contains(i.ItemId));
            if (blacklistedCount > 0)
            {
                ImGui.SetTooltip($"Blacklisted items ({blacklistedCount}) will not be selected");
            }
        }
    }
    
    private void DrawProtectedItemsTab(string tabName, float contentHeight, List<InventoryItemInfo> protectedItems)
    {
        using (var tabItem = ImRaii.TabItem(tabName))
        {
            if (tabItem)
            {
                using (var child = ImRaii.Child("ProtectedContent", new Vector2(0, contentHeight), false))
                {
                    ImGui.Text("Items Protected by Active Filters:");
                    ImGui.Spacing();
                    
                    if (!protectedItems.Any())
                    {
                        ImGui.TextColored(new Vector4(0.6f, 0.8f, 0.6f, 1), "No items are currently being filtered out.");
                        ImGui.Text("All items in your inventory are available for selection.");
                    }
                    else
                    {
                        DrawItemsTable(protectedItems, "ProtectedItemsTable");
                    }
                }
            }
        }
    }
    
    private void DrawBlacklistTab(string tabName, float contentHeight)
    {
        using (var tabItem = ImRaii.TabItem(tabName))
        {
            if (tabItem)
            {
                using (var child = ImRaii.Child("BlacklistContent", new Vector2(0, contentHeight), false))
                {
                    DrawBlacklistTabContent();
                }
            }
        }
    }
    
    private void DrawAutoDiscardTab(string tabName, float contentHeight)
    {
        using (var tabItem = ImRaii.TabItem(tabName))
        {
            if (tabItem)
            {
                using (var child = ImRaii.Child("AutoDiscardContent", new Vector2(0, contentHeight), false))
                {
                    DrawAutoDiscardTabContent();
                }
            }
        }
    }
    
    private void DrawBottomActionBar()
    {
        using var style = ImRaii.PushStyle(ImGuiStyleVar.FramePadding, new Vector2(8, 6));
        using var color = ImRaii.PushColor(ImGuiCol.ChildBg, new Vector4(0.165f, 0.165f, 0.165f, 1f));
        
        using (var child = ImRaii.Child("ActionBar", new Vector2(0, 42), true, ImGuiWindowFlags.NoScrollbar))
        {
            int selectedCount;
            lock (_selectedItemsLock)
            {
                selectedCount = _selectedItems.Count;
            }
            
            var clearButtonText = "Clear All";
            var discardButtonText = $"Discard ({selectedCount})###DiscardButton";
            var blacklistButtonText = $"Add to Blacklist ({selectedCount})###BlacklistButton";
            var autoDiscardButtonText = $"Add to Auto-Discard ({selectedCount})###AutoDiscardButton";
            var executeAutoDiscardText = "Execute Auto Discard";
            
            var buttonPadding = 20f;
            var clearButtonWidth = Math.Max(80f, ImGui.CalcTextSize(clearButtonText).X + buttonPadding);
            var discardButtonWidth = Math.Max(80f, ImGui.CalcTextSize(discardButtonText).X + buttonPadding);
            var blacklistButtonWidth = Math.Max(120f, ImGui.CalcTextSize(blacklistButtonText).X + buttonPadding);
            var autoDiscardButtonWidth = Math.Max(140f, ImGui.CalcTextSize(autoDiscardButtonText).X + buttonPadding);
            var executeAutoDiscardWidth = Math.Max(140f, ImGui.CalcTextSize(executeAutoDiscardText).X + buttonPadding);
            
            // Clear All button
            if (ImGui.Button(clearButtonText, new Vector2(clearButtonWidth, 0)))
            {
                lock (_selectedItemsLock)
                {
                    _selectedItems.Clear();
                }
                lock (_itemsLock)
                {
                    foreach (var item in _cachedItems)
                    {
                        item.IsSelected = false;
                    }
                }
            }
            
            ImGui.SameLine();
            
            // Discard button
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
            
            // Add to Blacklist button
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
            
            ImGui.SameLine();
            
            // Add to Auto-Discard button
            using (var btnColors = ImRaii.PushColor(ImGuiCol.Button, new Vector4(0.541f, 0.341f, 0.127f, 1f))
                                         .Push(ImGuiCol.ButtonHovered, new Vector4(0.641f, 0.441f, 0.227f, 1f)))
            {
                if (selectedCount > 0)
                {
                    if (ImGui.Button(autoDiscardButtonText, new Vector2(autoDiscardButtonWidth, 0)))
                    {
                        AddSelectedToAutoDiscard();
                    }
                }
                else
                {
                    using (var disabled = ImRaii.Disabled())
                    {
                        ImGui.Button(autoDiscardButtonText, new Vector2(autoDiscardButtonWidth, 0));
                    }
                }
            }
            
            ImGui.SameLine();
            
            // Execute Auto Discard button
            using (var btnColors = ImRaii.PushColor(ImGuiCol.Button, new Vector4(0.7f, 0.2f, 0.2f, 1f))
                                         .Push(ImGuiCol.ButtonHovered, new Vector4(0.8f, 0.3f, 0.3f, 1f)))
            {
                bool hasAutoDiscardItems = _taskCoordinator.AutoDiscardItems.Count > 0;
                
                if (hasAutoDiscardItems)
                {
                    if (ImGui.Button(executeAutoDiscardText, new Vector2(executeAutoDiscardWidth, 0)))
                    {
                        _taskCoordinator.ExecuteAutoDiscard();
                    }
                    
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip("Execute auto-discard for configured items");
                    }
                }
                else
                {
                    using (var disabled = ImRaii.Disabled())
                    {
                        ImGui.Button(executeAutoDiscardText, new Vector2(executeAutoDiscardWidth, 0));
                    }
                    
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip("No items configured for auto-discard");
                    }
                }
            }
        }
    }
    
    private void DrawDiscardConfirmation()
    {
        var windowSize = new Vector2(700, 500);
        ImGui.SetNextWindowSize(windowSize, ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.FirstUseEver, new Vector2(0.5f, 0.5f));
        
        using var styles = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(10, 10))
                                 .Push(ImGuiStyleVar.FramePadding, new Vector2(6, 5))
                                 .Push(ImGuiStyleVar.ItemSpacing, new Vector2(8, 6));
        
        bool isDiscarding = _taskCoordinator.IsDiscarding;
        ImGui.Begin("Confirm Discard##DiscardConfirmation", ref isDiscarding, ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse);
        
        if (!isDiscarding)
        {
            _taskCoordinator.DiscardTasks.EnqueueCancelDiscard();
        }
        
        // Warning header
        using (var color = ImRaii.PushColor(ImGuiCol.ChildBg, new Vector4(0.541f, 0.227f, 0.227f, 0.3f)))
        {
            using (var child = ImRaii.Child("WarningHeader", new Vector2(0, 36), true, ImGuiWindowFlags.NoScrollbar))
            {
                ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 5);
                using (var font = ImRaii.PushFont(UiBuilder.IconFont))
                {
                    ImGui.TextColored(ColorError, FontAwesomeIcon.ExclamationTriangle.ToIconString());
                }
                ImGui.SameLine();
                ImGui.Text("WARNING: This will permanently delete the following items!");
            }
        }
        
        ImGui.Spacing();
        
        // Summary section
        using (var color = ImRaii.PushColor(ImGuiCol.ChildBg, new Vector4(0.145f, 0.145f, 0.145f, 1f)))
        {
            using (var child = ImRaii.Child("SummarySection", new Vector2(0, 80), true))
            {
                DrawDiscardSummary();
            }
        }
        
        ImGui.Spacing();
        
        // Items to discard
        ImGui.Text("Items to discard:");
        var tableHeight = windowSize.Y - 280;
        using (var color = ImRaii.PushColor(ImGuiCol.ChildBg, new Vector4(0.145f, 0.145f, 0.145f, 1f)))
        {
            using (var child = ImRaii.Child("ItemTable", new Vector2(0, tableHeight), true))
            {
                DrawDiscardItemsTable();
            }
        }
        
        // Error display
        var error = _taskCoordinator.DiscardError;
        if (!string.IsNullOrEmpty(error))
        {
            ImGui.Spacing();
            using (var color = ImRaii.PushColor(ImGuiCol.ChildBg, new Vector4(0.541f, 0.227f, 0.227f, 0.3f)))
            {
                using (var child = ImRaii.Child("ErrorSection", new Vector2(0, 30), true, ImGuiWindowFlags.NoScrollbar))
                {
                    ImGui.TextColored(ColorError, error);
                }
            }
        }
        
        // Progress bar
        var progress = _taskCoordinator.DiscardProgress;
        var totalItems = _taskCoordinator.CurrentDiscardItems.Count;
        if (progress > 0 && totalItems > 0)
        {
            ImGui.Spacing();
            var progressPercent = (float)progress / totalItems;
            using (var color = ImRaii.PushColor(ImGuiCol.PlotHistogram, ColorSuccess))
            {
                ImGui.ProgressBar(progressPercent, new Vector2(-1, 25), $"Discarding... {progress}/{totalItems}");
            }
        }
        
        ImGui.Spacing();
        DrawDiscardButtons();
        ImGui.End();
    }
    
    private void DrawDiscardSummary()
    {
        var itemsToDiscard = _taskCoordinator.CurrentDiscardItems;
        var totalItems = itemsToDiscard.Count;
        var totalQuantity = itemsToDiscard.Sum(i => i.Quantity);
        var totalValue = itemsToDiscard.Where(i => i.MarketPrice.HasValue).Sum(i => i.MarketPrice!.Value * i.Quantity);
        var totalValueFormatted = totalValue > 0 ? $"{totalValue:N0} gil" : "Unknown";
        
        ImGui.Columns(3, "SummaryColumns", false);
        
        using (var font = ImRaii.PushFont(UiBuilder.IconFont))
        {
            ImGui.TextColored(ColorInfo, FontAwesomeIcon.List.ToIconString());
        }
        ImGui.SameLine();
        ImGui.Text("Total Items:");
        ImGui.TextColored(ColorWarning, $"{totalItems} unique items");
        
        ImGui.NextColumn();
        
        using (var font = ImRaii.PushFont(UiBuilder.IconFont))
        {
            ImGui.TextColored(ColorInfo, FontAwesomeIcon.LayerGroup.ToIconString());
        }
        ImGui.SameLine();
        ImGui.Text("Total Quantity:");
        ImGui.TextColored(ColorWarning, $"{totalQuantity} items");
        
        ImGui.NextColumn();
        
        using (var font = ImRaii.PushFont(UiBuilder.IconFont))
        {
            ImGui.TextColored(ColorPrice, FontAwesomeIcon.Coins.ToIconString());
        }
        ImGui.SameLine();
        ImGui.Text("Market Value:");
        if (totalValue > 0)
        {
            ImGui.TextColored(ColorPrice, totalValueFormatted);
        }
        else
        {
            ImGui.TextColored(ColorSubdued, totalValueFormatted);
        }
        
        ImGui.Columns(1);
    }
    
    private void DrawDiscardItemsTable()
    {
        var itemsToDiscard = _taskCoordinator.CurrentDiscardItems;
        using var style = ImRaii.PushStyle(ImGuiStyleVar.CellPadding, new Vector2(4, 4));
        
        using (var table = ImRaii.Table("DiscardTable", 5, 
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | 
            ImGuiTableFlags.Resizable))
        {
            if (table)
            {
                ImGui.TableSetupColumn("ID", ImGuiTableColumnFlags.WidthFixed, 60);
                ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Quantity", ImGuiTableColumnFlags.WidthFixed, 80);
                ImGui.TableSetupColumn("Location", ImGuiTableColumnFlags.WidthFixed, 120);
                ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthFixed, 100);
                ImGui.TableSetupScrollFreeze(0, 1);
                ImGui.TableHeadersRow();
                
                foreach (var item in itemsToDiscard)
                {
                    ImGui.TableNextRow();
                    
                    ImGui.TableNextColumn();
                    ImGui.Text(item.ItemId.ToString());
                    
                    ImGui.TableNextColumn();
                    if (item.IconId > 0)
                    {
                        var icon = _iconCache.GetIcon(item.IconId);
                        if (icon != null)
                        {
                            ImGui.Image(icon.Handle, new Vector2(20, 20));
                            ImGui.SameLine();
                        }
                    }
                    ImGui.Text(item.Name);
                    if (item.IsHQ)
                    {
                        ImGui.SameLine();
                        ImGui.TextColored(ColorHQItem, "[HQ]");
                    }
                    
                    ImGui.TableNextColumn();
                    ImGui.Text(item.Quantity.ToString());
                    
                    ImGui.TableNextColumn();
                    ImGui.Text(GetLocationName(item.Container));
                    
                    ImGui.TableNextColumn();
                    if (item.MarketPrice.HasValue && item.MarketPrice.Value > 0)
                    {
                        ImGui.TextColored(ColorPrice, $"{item.MarketPrice.Value * item.Quantity:N0} gil");
                    }
                    else
                    {
                        ImGui.TextColored(ColorSubdued, "N/A");
                    }
                }
            }
        }
    }
    
    private void DrawDiscardButtons()
    {
        var buttonWidth = 120f;
        var buttonHeight = 30f;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var totalWidth = buttonWidth * 2 + spacing;
        var availableWidth = ImGui.GetContentRegionAvail().X;
        var centerPos = (availableWidth - totalWidth) * 0.5f;
        
        if (centerPos > 0)
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + centerPos);
        
        var progress = _taskCoordinator.DiscardProgress;
        if (progress == 0)
        {
            using (var color = ImRaii.PushColor(ImGuiCol.Button, new Vector4(0.541f, 0.227f, 0.227f, 1f))
                                     .Push(ImGuiCol.ButtonHovered, new Vector4(0.641f, 0.327f, 0.327f, 1f)))
            {
                if (ImGui.Button("Start Discarding", new Vector2(buttonWidth, buttonHeight)))
                {
                    StartDiscarding();
                }
            }
            
            ImGui.SameLine();
            
            if (ImGui.Button("Cancel", new Vector2(buttonWidth, buttonHeight)))
            {
                _taskCoordinator.DiscardTasks.EnqueueCancelDiscard();
            }
        }
        else
        {
            using (var disabled = ImRaii.Disabled())
            {
                ImGui.Button("Discarding...", new Vector2(buttonWidth, buttonHeight));
            }
            
            ImGui.SameLine();
            
            using (var color = ImRaii.PushColor(ImGuiCol.Button, new Vector4(0.541f, 0.541f, 0.227f, 1f))
                                     .Push(ImGuiCol.ButtonHovered, new Vector4(0.641f, 0.641f, 0.327f, 1f)))
            {
                if (ImGui.Button("Cancel", new Vector2(buttonWidth, buttonHeight)))
                {
                    _taskCoordinator.DiscardTasks.EnqueueCancelDiscard();
                }
            }
        }
    }
    
    // Helper methods
    private void PrepareDiscard()
    {
        List<uint> selectedItemsCopy;
        lock (_selectedItemsLock)
        {
            selectedItemsCopy = new List<uint>(_selectedItems);
        }
        
        if (selectedItemsCopy.Any())
        {
            _taskCoordinator.ExecuteBulkDiscard(selectedItemsCopy);
        }
    }
    
    private void StartDiscarding()
    {
        // The TaskCoordinator handles the actual discarding logic
        // This just triggers the start if items are prepared
    }
    
    private void AddSelectedToBlacklist()
    {
        lock (_selectedItemsLock)
        {
            foreach (var itemId in _selectedItems)
            {
                _taskCoordinator.BlacklistedItems.Add(itemId);
            }
            _selectedItems.Clear();
        }
        
        SaveBlacklist();
        _taskCoordinator.RefreshAll(_showArmory, _searchFilter);
    }
    
    private void AddSelectedToAutoDiscard()
    {
        lock (_selectedItemsLock)
        {
            foreach (var itemId in _selectedItems)
            {
                _taskCoordinator.AutoDiscardItems.Add(itemId);
            }
            _selectedItems.Clear();
        }
        
        SaveAutoDiscard();
        _taskCoordinator.RefreshAll(_showArmory, _searchFilter);
    }
    
    private string GetLocationName(InventoryType container)
    {
        return container switch
        {
            InventoryType.Inventory1 => "Inventory (1)",
            InventoryType.Inventory2 => "Inventory (2)",
            InventoryType.Inventory3 => "Inventory (3)",
            InventoryType.Inventory4 => "Inventory (4)",
            InventoryType.ArmoryMainHand => "Armory (Main Hand)",
            InventoryType.ArmoryOffHand => "Armory (Off Hand)",
            InventoryType.ArmoryHead => "Armory (Head)",
            InventoryType.ArmoryBody => "Armory (Body)",
            InventoryType.ArmoryHands => "Armory (Hands)",
            InventoryType.ArmoryLegs => "Armory (Legs)",
            InventoryType.ArmoryFeets => "Armory (Feet)",
            InventoryType.ArmoryEar => "Armory (Earrings)",
            InventoryType.ArmoryNeck => "Armory (Necklace)",
            InventoryType.ArmoryWrist => "Armory (Bracelets)",
            InventoryType.ArmoryRings => "Armory (Rings)",
            _ => container.ToString()
        };
    }
    
}