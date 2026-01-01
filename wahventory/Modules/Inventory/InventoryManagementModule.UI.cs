using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Bindings.ImGui;
using wahventory.Core;
using wahventory.Models;
using wahventory.Services;
using wahventory.UI.Components;

namespace wahventory.Modules.Inventory;

public partial class InventoryManagementModule
{
    private FilterPanelComponent? _filterPanel;
    private ItemTableComponent? _itemTable;
    private SearchComponent? _blacklistSearch;
    private SearchComponent? _autoDiscardSearch;
    
    private static readonly Vector4 ColorPrice = new(1f, 0.8f, 0.2f, 1f);
    private static readonly Vector4 ColorWarning = new(0.9f, 0.5f, 0.1f, 1f);
    private static readonly Vector4 ColorInfo = new(0.7f, 0.7f, 0.7f, 1f);
    private static readonly Vector4 ColorSubdued = new(0.6f, 0.6f, 0.6f, 1f);
    private static readonly Vector4 ColorError = new(0.8f, 0.2f, 0.2f, 1f);
    private static readonly Vector4 ColorSuccess = new(0.2f, 0.8f, 0.2f, 1f);
    
    private void InitializeUIComponents()
    {
        _filterPanel = new FilterPanelComponent();
        _filterPanel.OnFiltersChanged += () =>
        {
            _plugin.ConfigManager.SaveConfiguration();
            UpdateCategories();
        };
        
        _itemTable = new ItemTableComponent(_iconCache);
        _blacklistSearch = new SearchComponent(_searchService, _iconCache);
        _blacklistSearch.OnItemSelected += (itemId) =>
        {
            if (!BlacklistedItems.Contains(itemId))
            {
                BlacklistedItems.Add(itemId);
                SaveBlacklist();
                RefreshInventory();
            }
        };
        
        _autoDiscardSearch = new SearchComponent(_searchService, _iconCache);
        _autoDiscardSearch.OnItemSelected += (itemId) =>
        {
            if (!AutoDiscardItems.Contains(itemId))
            {
                AutoDiscardItems.Add(itemId);
                SaveAutoDiscard();
                RefreshInventory();
            }
        };
    }
    
    private void DrawTopControls()
    {
        using var style = ImRaii.PushStyle(ImGuiStyleVar.FramePadding, new Vector2(6, 5))
                                .Push(ImGuiStyleVar.ItemSpacing, new Vector2(8, 4));
        using var color = ImRaii.PushColor(ImGuiCol.ChildBg, new Vector4(0.145f, 0.145f, 0.145f, 1f));
        
        using (var child = ImRaii.Child("TopBar", new Vector2(0, 40), true, ImGuiWindowFlags.NoScrollbar))
        {
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 2);
            
            using (var font = ImRaii.PushFont(UiBuilder.IconFont))
            {
                ImGui.Text(FontAwesomeIcon.Search.ToIconString());
            }
            
            ImGui.SameLine();
            ImGui.SetNextItemWidth(180f);
            if (ImGui.InputTextWithHint("##Search", "Search items...", ref _searchFilter, 100))
            {
                UpdateCategories();
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
                        UpdateCategories();
                    }
                }
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip("Clear search");
                }
            }
            
            ImGui.SameLine();
            using (var font = ImRaii.PushFont(UiBuilder.IconFont))
            {
                if (ImGui.Button(FontAwesomeIcon.Sync.ToIconString() + "##Refresh", new Vector2(28, 0)))
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
                _plugin.ConfigManager.SaveConfiguration();
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
                                _priceService.UpdateWorld(_selectedWorld);
                                _priceService.ClearCache();
                                
                                lock (_stateLock)
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
            
            var windowWidth = ImGui.GetWindowContentRegionMax().X;
            long totalValue;
            lock (_stateLock)
            {
                totalValue = _categories.Sum(c => c.TotalValue ?? 0);
            }
            
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
        
        ImGui.Spacing();
    }
    
    private void DrawFiltersAndSettings()
    {
        if (_filterPanel == null)
            InitializeUIComponents();
        
        _filterPanel?.Draw(Settings);
    }
    
    private void DrawAvailableItemsTab()
    {
        List<CategoryGroup> categoriesCopy;
        lock (_stateLock)
        {
            categoriesCopy = new List<CategoryGroup>(_categories);
        }

        if (!string.IsNullOrWhiteSpace(_searchFilter))
        {
            DrawSearchResultsView(categoriesCopy);
            return;
        }

        // Draw a persistent header row
        DrawTableHeader();

        foreach (var category in categoriesCopy)
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
                    DrawCategoryItems(category, showHeaders: false);
                }
                else
                {
                    ExpandedCategories[category.CategoryId] = false;
                    _expandedCategoriesChanged = true;
                }

                ImGui.Spacing();
            }
        }
    }

    private void DrawTableHeader()
    {
        using var style = ImRaii.PushStyle(ImGuiStyleVar.CellPadding, new Vector2(4, 2))
                                .Push(ImGuiStyleVar.ItemSpacing, new Vector2(4, 2));

        // Column count: Checkbox, ID, Item, Qty, iLvl + (Price, Total) or (Location)
        int columnCount = Settings.ShowMarketPrices ? 7 : 6;

        using (var table = ImRaii.Table("AvailableItemsHeader", columnCount,
            ImGuiTableFlags.Borders | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.NoPadOuterX))
        {
            if (table)
            {
                // Setup columns with same widths as ItemTableComponent
                ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, 26f);
                ImGui.TableSetupColumn("ID", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, 50f);
                ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch | ImGuiTableColumnFlags.NoHide);
                ImGui.TableSetupColumn("Qty", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, 35f);
                ImGui.TableSetupColumn("iLvl", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, 40f);

                if (Settings.ShowMarketPrices)
                {
                    ImGui.TableSetupColumn("Price", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, 75f);
                    ImGui.TableSetupColumn("Total", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, 90f);
                }
                else
                {
                    ImGui.TableSetupColumn("Location", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, 110f);
                }

                ImGui.TableHeadersRow();
            }
        }
    }
    
    private void DrawCategoryItems(CategoryGroup category, bool showHeaders = true)
    {
        if (_itemTable == null)
            InitializeUIComponents();

        var config = new ItemTableConfig
        {
            TableId = $"CategoryTable_{category.CategoryId}",
            ShowCheckbox = true,
            ShowItemLevel = true,
            ShowLocation = !Settings.ShowMarketPrices,
            ShowMarketPrices = Settings.ShowMarketPrices,
            ShowTotalValue = Settings.ShowMarketPrices,
            ShowHeaders = showHeaders,
            SearchFilter = _searchFilter,
            IsItemSelected = (item) =>
            {
                lock (_stateLock)
                {
                    return _selectedItems.Contains(item.ItemId);
                }
            },
            IsItemBlacklisted = (item) => BlacklistedItems.Contains(item.ItemId),
            OnItemSelectionChanged = (item, selected) =>
            {
                lock (_stateLock)
                {
                    if (selected)
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
            },
            IsFetchingPrice = (itemId) => _priceService.IsFetchingPrice(itemId),
            OnPriceFetchRequested = (item) => _ = _priceService.FetchPrice(item).ContinueWith(task =>
            {
                if (task.IsCompletedSuccessfully && task.Result.HasValue)
                {
                    lock (_stateLock)
                    {
                        item.MarketPrice = task.Result.Value;
                        item.MarketPriceFetchTime = DateTime.Now;
                    }
                }
            })
        };
        
        _itemTable?.DrawTable(category.Items, config);
    }
    
    private void DrawCategoryControls(CategoryGroup category)
    {
        int selectedInCategory;
        bool allSelectableSelected;
        lock (_stateLock)
        {
            selectedInCategory = category.Items.Count(i => _selectedItems.Contains(i.ItemId));
            var selectableItems = category.Items.Where(i => !BlacklistedItems.Contains(i.ItemId)).ToList();
            allSelectableSelected = selectableItems.Count > 0 && selectableItems.All(i => _selectedItems.Contains(i.ItemId));
        }
        
        var buttonText = allSelectableSelected ? "Deselect All" : "Select All";
        
        if (ImGui.SmallButton(buttonText))
        {
            lock (_stateLock)
            {
                if (allSelectableSelected)
                {
                    foreach (var item in category.Items)
                    {
                        _selectedItems.Remove(item.ItemId);
                        item.IsSelected = false;
                    }
                }
                else
                {
                    foreach (var item in category.Items)
                    {
                        if (BlacklistedItems.Contains(item.ItemId))
                            continue;
                        
                        _selectedItems.Add(item.ItemId);
                        item.IsSelected = true;
                    }
                }
            }
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
        
        if (_itemTable == null)
            InitializeUIComponents();
        
        var config = new ItemTableConfig
        {
            TableId = "SearchResultsTable",
            ShowCheckbox = true,
            ShowItemLevel = true,
            ShowLocation = true,
            ShowCategory = true,
            ShowMarketPrices = Settings.ShowMarketPrices,
            Scrollable = true,
            SearchFilter = _searchFilter,
            IsItemSelected = (item) =>
            {
                lock (_stateLock)
                {
                    return _selectedItems.Contains(item.ItemId);
                }
            },
            IsItemBlacklisted = (item) => BlacklistedItems.Contains(item.ItemId),
            OnItemSelectionChanged = (item, selected) =>
            {
                lock (_stateLock)
                {
                    if (selected)
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
            },
            IsFetchingPrice = (itemId) => _priceService.IsFetchingPrice(itemId),
            OnPriceFetchRequested = (item) => _ = _priceService.FetchPrice(item).ContinueWith(task =>
            {
                if (task.IsCompletedSuccessfully && task.Result.HasValue)
                {
                    lock (_stateLock)
                    {
                        item.MarketPrice = task.Result.Value;
                        item.MarketPriceFetchTime = DateTime.Now;
                    }
                }
            })
        };
        
        _itemTable?.DrawTable(allMatchingItems, config);
    }
    
    private void DrawProtectedItemsTab(List<InventoryItemInfo> protectedItems)
    {
        ImGui.Text("Items Protected by Active Filters:");
        ImGui.Spacing();
        
        if (!protectedItems.Any())
        {
            ImGui.TextColored(new Vector4(0.6f, 0.8f, 0.6f, 1), "No items are currently being filtered out.");
            ImGui.Text("All items in your inventory are available for selection.");
            return;
        }
        
        var filteredCategories = protectedItems
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
            using var id = ImRaii.PushId($"FilteredCategory_{category.CategoryId}");
            
            var categoryHeaderText = $"{category.CategoryName} ({category.Items.Count} protected)";
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
                        
                        if (_itemTable == null)
                            InitializeUIComponents();
                        
                        var config = new ItemTableConfig
                        {
                            TableId = $"ProtectedTable_{category.CategoryId}",
                            ShowItemLevel = true,
                            ShowLocation = true,
                            ShowMarketPrices = Settings.ShowMarketPrices,
                            ShowTotalValue = Settings.ShowMarketPrices,
                            ShowReason = true,
                            GetFilterReason = (item) => FilterService.GetFilterReason(item, Settings.SafetyFilters)
                        };
                        
                        _itemTable?.DrawTable(category.Items, config);
                    }
                    else
                    {
                        ExpandedCategories[category.CategoryId] = false;
                        _expandedCategoriesChanged = true;
                    }
                }
            }
            
            ImGui.Spacing();
        }
    }
    
    private void DrawBlacklistTab()
    {
        ImGui.TextWrapped("Manage your custom blacklist. Items added here will never be selected for discard.");
        ImGui.TextWrapped("This is in addition to the built-in safety lists shown in the Protected Items tab.");
        ImGui.Spacing();
        
        if (_blacklistSearch == null)
            InitializeUIComponents();
        
        _blacklistSearch?.Draw("Add New Item to Blacklist", BlacklistedItems);
        
        ImGui.Separator();
        ImGui.Spacing();
        
        ImGui.Text($"Your Custom Blacklist ({BlacklistedItems.Count} items)");
        ImGui.Spacing();
        
        if (!BlacklistedItems.Any())
        {
            ImGui.TextColored(ColorSubdued, "No custom blacklisted items.");
            ImGui.TextColored(ColorInfo, "Add items using the controls above or select items in the Available Items tab and click 'Add to Blacklist'.");
            return;
        }
        
        var itemsToShow = GetBlacklistItemsToShow();
        DrawBlacklistTable(itemsToShow);
        
        ImGui.Spacing();
        if (ImGui.Button("Clear All Blacklisted Items"))
        {
            ImGui.OpenPopup("ClearBlacklistConfirm");
        }
        
        using (var popup = ImRaii.PopupModal("ClearBlacklistConfirm"))
        {
            if (popup)
            {
                ImGui.Text("Are you sure you want to clear all custom blacklisted items?");
                ImGui.Text($"This will remove {BlacklistedItems.Count} items from your blacklist.");
                ImGui.Spacing();
                
                if (ImGui.Button("Yes, Clear All", new Vector2(120, 0)))
                {
                    BlacklistedItems.Clear();
                    SaveBlacklist();
                    RefreshInventory();
                    ImGui.CloseCurrentPopup();
                }
                
                ImGui.SameLine();
                if (ImGui.Button("Cancel", new Vector2(120, 0)))
                {
                    ImGui.CloseCurrentPopup();
                }
            }
        }
    }
    
    private IEnumerable<uint> GetBlacklistItemsToShow()
    {
        var itemsToShow = BlacklistedItems.AsEnumerable();
        
        if (!string.IsNullOrWhiteSpace(_searchFilter))
        {
            var filteredIds = new List<uint>();
            foreach (var itemId in BlacklistedItems)
            {
                string itemName = null;
                lock (_stateLock)
                {
                    var itemInfo = _allItems.FirstOrDefault(i => i.ItemId == itemId);
                    itemName = itemInfo?.Name;
                }
                
                if (string.IsNullOrEmpty(itemName))
                {
                    var itemInfo = _searchService.GetItemInfo(itemId);
                    itemName = itemInfo?.Name ?? $"Unknown Item ({itemId})";
                }
                
                if (itemName.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase) ||
                    itemId.ToString().Contains(_searchFilter))
                {
                    filteredIds.Add(itemId);
                }
            }
            itemsToShow = filteredIds;
        }
        
        return itemsToShow;
    }
    
    private void DrawBlacklistTable(IEnumerable<uint> itemIds)
    {
        if (_itemTable == null)
            InitializeUIComponents();
        
        var items = itemIds.Select(itemId =>
        {
            InventoryItemInfo? itemInfo = null;
            lock (_stateLock)
            {
                itemInfo = _allItems.FirstOrDefault(i => i.ItemId == itemId);
            }
            
            if (itemInfo == null)
            {
                var info = _searchService.GetItemInfo(itemId);
                if (info.HasValue)
                {
                    itemInfo = new InventoryItemInfo
                    {
                        ItemId = itemId,
                        Name = info.Value.Name,
                        IconId = info.Value.IconId,
                        CategoryName = info.Value.CategoryName,
                        ItemLevel = (uint)info.Value.ItemLevel
                    };
                }
            }
            
            return itemInfo;
        }).Where(i => i != null).Cast<InventoryItemInfo>().ToList();
        
        var config = new ItemTableConfig
        {
            TableId = "BlacklistTable",
            ShowItemLevel = true,
            ShowCategory = true,
            ShowActions = true,
            OnRemoveItem = (item) =>
            {
                BlacklistedItems.Remove(item.ItemId);
                SaveBlacklist();
                RefreshInventory();
            }
        };
        
        _itemTable?.DrawTable(items, config);
    }
    
    private void DrawAutoDiscardTab()
    {
        var passiveDiscardOpen = ImGui.CollapsingHeader("Passive Discard Settings", ImGuiTreeNodeFlags.DefaultOpen);
        if (passiveDiscardOpen)
        {
            ImGui.Indent();
            DrawPassiveDiscardSettings();
            ImGui.Unindent();
            ImGui.Spacing();
        }
        
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.TextWrapped("Manage your auto-discard list. Items added here will be automatically discarded when using the /wahventory auto command.");
        ImGui.TextWrapped("WARNING: This is a powerful feature. Only add items you are absolutely certain you want to discard automatically!");
        ImGui.Spacing();
        
        if (_autoDiscardSearch == null)
            InitializeUIComponents();
        
        _autoDiscardSearch?.Draw("Add New Item to Auto-Discard", AutoDiscardItems);
        
        ImGui.Separator();
        ImGui.Spacing();
        
        ImGui.Text($"Your Auto-Discard List ({AutoDiscardItems.Count} items)");
        ImGui.Spacing();
        
        if (!AutoDiscardItems.Any())
        {
            ImGui.TextColored(ColorSubdued, "No auto-discard items configured.");
            ImGui.TextColored(ColorInfo, "Add items using the controls above or select items in the Available Items tab and click 'Add to Auto-Discard'.");
            return;
        }
        
        var itemsToShow = GetAutoDiscardItemsToShow();
        DrawAutoDiscardTable(itemsToShow);
        
        ImGui.Spacing();
        if (ImGui.Button("Clear All Auto-Discard Items"))
        {
            ImGui.OpenPopup("ClearAutoDiscardConfirm");
        }
        
        using (var popup = ImRaii.PopupModal("ClearAutoDiscardConfirm"))
        {
            if (popup)
            {
                ImGui.Text("Are you sure you want to clear all auto-discard items?");
                ImGui.Text($"This will remove {AutoDiscardItems.Count} items from your auto-discard list.");
                ImGui.Spacing();
                
                if (ImGui.Button("Yes, Clear All", new Vector2(120, 0)))
                {
                    AutoDiscardItems.Clear();
                    SaveAutoDiscard();
                    RefreshInventory();
                    ImGui.CloseCurrentPopup();
                }
                
                ImGui.SameLine();
                if (ImGui.Button("Cancel", new Vector2(120, 0)))
                {
                    ImGui.CloseCurrentPopup();
                }
            }
        }
    }
    
    private IEnumerable<uint> GetAutoDiscardItemsToShow()
    {
        var itemsToShow = AutoDiscardItems.AsEnumerable();
        
        if (!string.IsNullOrWhiteSpace(_searchFilter))
        {
            var filteredIds = new List<uint>();
            foreach (var itemId in AutoDiscardItems)
            {
                string itemName = null;
                lock (_stateLock)
                {
                    var itemInfo = _allItems.FirstOrDefault(i => i.ItemId == itemId);
                    itemName = itemInfo?.Name;
                }
                
                if (string.IsNullOrEmpty(itemName))
                {
                    var itemInfo = _searchService.GetItemInfo(itemId);
                    itemName = itemInfo?.Name ?? $"Unknown Item ({itemId})";
                }
                
                if (itemName.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase) ||
                    itemId.ToString().Contains(_searchFilter))
                {
                    filteredIds.Add(itemId);
                }
            }
            itemsToShow = filteredIds;
        }
        
        return itemsToShow;
    }
    
    private void DrawAutoDiscardTable(IEnumerable<uint> itemIds)
    {
        if (_itemTable == null)
            InitializeUIComponents();
        
        var items = itemIds.Select(itemId =>
        {
            InventoryItemInfo? itemInfo = null;
            lock (_stateLock)
            {
                itemInfo = _allItems.FirstOrDefault(i => i.ItemId == itemId);
            }
            
            if (itemInfo == null)
            {
                var info = _searchService.GetItemInfo(itemId);
                if (info.HasValue)
                {
                    itemInfo = new InventoryItemInfo
                    {
                        ItemId = itemId,
                        Name = info.Value.Name,
                        IconId = info.Value.IconId,
                        CategoryName = info.Value.CategoryName,
                        ItemLevel = (uint)info.Value.ItemLevel
                    };
                }
            }
            
            return itemInfo;
        }).Where(i => i != null).Cast<InventoryItemInfo>().ToList();
        
        var config = new ItemTableConfig
        {
            TableId = "AutoDiscardTable",
            ShowItemLevel = true,
            ShowCategory = true,
            ShowActions = true,
            OnRemoveItem = (item) =>
            {
                AutoDiscardItems.Remove(item.ItemId);
                SaveAutoDiscard();
                RefreshInventory();
            },
            DrawItemTags = (item) =>
            {
                lock (_stateLock)
                {
                    if (_allItems.Any(i => i.ItemId == item.ItemId))
                    {
                        ImGui.SameLine();
                        ImGui.TextColored(ColorWarning, "[In Inventory]");
                    }
                }
            }
        };
        
        _itemTable?.DrawTable(items, config);
    }
    
    private void DrawPassiveDiscardSettings()
    {
        using (var font = ImRaii.PushFont(UiBuilder.IconFont))
        {
            ImGui.Text(FontAwesomeIcon.Robot.ToIconString());
        }
        ImGui.SameLine();
        ImGui.Text("Passive Discard Settings");
        
        ImGui.TextWrapped("Passive discard will automatically discard items from your auto-discard list when you are idle.");
        ImGui.Spacing();
        
        var enabled = Settings.PassiveDiscard.Enabled;
        if (ImGui.Checkbox("Enable Passive Discard", ref enabled))
        {
            Settings.PassiveDiscard.Enabled = enabled;
            _plugin.ConfigManager.SaveConfiguration();
        }
        
        using (var disabled = ImRaii.Disabled(!Settings.PassiveDiscard.Enabled))
        {
            ImGui.Spacing();
            ImGui.Text("Idle Time Required:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(100);
            var idleTime = Settings.PassiveDiscard.IdleTimeSeconds;
            if (ImGui.InputInt("##IdleTime", ref idleTime, 5, 10))
            {
                Settings.PassiveDiscard.IdleTimeSeconds = Math.Max(10, Math.Min(300, idleTime));
                _plugin.ConfigManager.SaveConfiguration();
            }
            ImGui.SameLine();
            ImGui.Text("seconds");
            
            ImGui.Spacing();
            ImGui.Text("Zone Restrictions:");
            ImGui.TextWrapped("Passive discard only works in safe zones: Cities, Housing Areas, Inn Rooms, Barracks, Gold Saucer, and other non-combat areas.");
            ImGui.Spacing();
            ImGui.Text("Status:");
            ImGui.SameLine();
            
            var status = _passiveDiscardService.GetStatus(AutoDiscardItems, _originalItems, BlacklistedItems);
            DrawPassiveDiscardStatus(status);
        }
    }
    
    private void DrawPassiveDiscardStatus(PassiveDiscardStatus status)
    {
        switch (status.State)
        {
            case PassiveDiscardState.Disabled:
                ImGui.TextColored(ColorSubdued, "Disabled");
                break;
            case PassiveDiscardState.NoItems:
                ImGui.TextColored(ColorSubdued, "No items to discard");
                break;
            case PassiveDiscardState.PlayerBusy:
                ImGui.TextColored(ColorWarning, "Player Busy");
                break;
            case PassiveDiscardState.NotInAllowedZone:
                ImGui.TextColored(ColorWarning, "Not in Allowed Zone");
                break;
            case PassiveDiscardState.WaitingForIdle:
                ImGui.TextColored(ColorInfo, $"Waiting for idle ({status.IdleSeconds}/{status.RequiredIdleSeconds}s)");
                break;
            case PassiveDiscardState.Cooldown:
                ImGui.TextColored(ColorSubdued, $"Cooldown ({status.CooldownSecondsRemaining}s remaining)");
                break;
            case PassiveDiscardState.Ready:
                ImGui.TextColored(ColorSuccess, "Ready to execute auto-discard");
                break;
        }
    }
    
    private void DrawBottomActionBar()
    {
        using var style = ImRaii.PushStyle(ImGuiStyleVar.FramePadding, new Vector2(8, 6));
        using var color = ImRaii.PushColor(ImGuiCol.ChildBg, new Vector4(0.165f, 0.165f, 0.165f, 1f));
        
        using (var child = ImRaii.Child("ActionBar", new Vector2(0, 42), true, ImGuiWindowFlags.NoScrollbar))
        {
            int selectedCount;
            lock (_stateLock)
            {
                selectedCount = _selectedItems.Count;
            }
            
            var clearButtonText = "Clear All";
            var discardButtonText = $"Discard ({selectedCount})";
            var blacklistButtonText = $"Add to Blacklist ({selectedCount})";
            var autoDiscardButtonText = $"Add to Auto-Discard ({selectedCount})";
            var executeAutoDiscardText = "Execute Auto Discard";
            
            var buttonPadding = 20f;
            var clearButtonWidth = Math.Max(80f, ImGui.CalcTextSize(clearButtonText).X + buttonPadding);
            var discardButtonWidth = Math.Max(80f, ImGui.CalcTextSize(discardButtonText).X + buttonPadding);
            var blacklistButtonWidth = Math.Max(120f, ImGui.CalcTextSize(blacklistButtonText).X + buttonPadding);
            var autoDiscardButtonWidth = Math.Max(140f, ImGui.CalcTextSize(autoDiscardButtonText).X + buttonPadding);
            var executeAutoDiscardWidth = Math.Max(140f, ImGui.CalcTextSize(executeAutoDiscardText).X + buttonPadding);
            
            if (ImGui.Button(clearButtonText, new Vector2(clearButtonWidth, 0)))
            {
                lock (_stateLock)
                {
                    _selectedItems.Clear();
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
                        List<uint> selectedItemIds;
                        lock (_stateLock)
                        {
                            selectedItemIds = _selectedItems.ToList();

                            // Update prices from cache before preparing discard
                            foreach (var item in _originalItems.Where(i => selectedItemIds.Contains(i.ItemId)))
                            {
                                _priceService.UpdateItemPrice(item);
                            }
                        }
                        DiscardService.PrepareDiscard(selectedItemIds, _originalItems, BlacklistedItems);
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
            
            ImGui.SameLine();
            
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
            
            using (var btnColors = ImRaii.PushColor(ImGuiCol.Button, new Vector4(0.7f, 0.2f, 0.2f, 1f))
                                         .Push(ImGuiCol.ButtonHovered, new Vector4(0.8f, 0.3f, 0.3f, 1f)))
            {
                bool hasAutoDiscardItems = AutoDiscardItems.Count > 0;
                
                if (hasAutoDiscardItems)
                {
                    if (ImGui.Button(executeAutoDiscardText, new Vector2(executeAutoDiscardWidth, 0)))
                    {
                        ExecuteAutoDiscard();
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
    
    private void AddSelectedToBlacklist()
    {
        lock (_stateLock)
        {
            foreach (var itemId in _selectedItems)
            {
                if (!BlacklistedItems.Contains(itemId))
                {
                    BlacklistedItems.Add(itemId);
                }
            }
            
            _selectedItems.Clear();
            foreach (var item in _allItems)
            {
                item.IsSelected = false;
            }
        }
        
        SaveBlacklist();
        RefreshInventory();
    }
    
    private void AddSelectedToAutoDiscard()
    {
        lock (_stateLock)
        {
            foreach (var itemId in _selectedItems)
            {
                if (!AutoDiscardItems.Contains(itemId))
                {
                    AutoDiscardItems.Add(itemId);
                }
            }
            
            _selectedItems.Clear();
            foreach (var item in _allItems)
            {
                item.IsSelected = false;
            }
        }
        
        SaveAutoDiscard();
        RefreshInventory();
    }
}

