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
    private ItemTableComponent? _itemTable;
    private SearchComponent? _blacklistSearch;
    private SearchComponent? _autoDiscardSearch;

    // Sort state for available items table
    private int _availableSortColumn = -1;
    private bool _availableSortAscending = true;

    private static readonly Vector4 ColorPrice = new(1f, 0.8f, 0.2f, 1f);
    private static readonly Vector4 ColorWarning = new(0.9f, 0.5f, 0.1f, 1f);
    private static readonly Vector4 ColorInfo = new(0.7f, 0.7f, 0.7f, 1f);
    private static readonly Vector4 ColorSubdued = new(0.6f, 0.6f, 0.6f, 1f);
    private static readonly Vector4 ColorError = new(0.8f, 0.2f, 0.2f, 1f);
    private static readonly Vector4 ColorSuccess = new(0.2f, 0.8f, 0.2f, 1f);

    private void InitializeUIComponents()
    {
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
    
    private void DrawSidebar()
    {
        using var style = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(4, 6));

        // Source Section
        ImGui.TextColored(ColorInfo, "SOURCE");
        ImGui.Separator();
        if (ImGui.Checkbox("Include Armory", ref _showArmory))
        {
            RefreshInventory();
        }

        ImGui.Spacing();
        ImGui.Spacing();

        // Job Filter Section
        ImGui.TextColored(ColorInfo, "JOB FILTER");
        ImGui.Separator();
        ImGui.SetNextItemWidth(-1);
        var jobDisplayText = string.IsNullOrEmpty(_jobFilter) ? "All Jobs" : _jobFilter;
        using (var combo = ImRaii.Combo("##JobFilter", jobDisplayText))
        {
            if (combo)
            {
                foreach (var job in JobAbbreviations)
                {
                    var displayName = string.IsNullOrEmpty(job) ? "All Jobs" : job;
                    if (ImGui.Selectable(displayName, job == _jobFilter))
                    {
                        _jobFilter = job;
                        UpdateCategories();
                    }
                }
            }
        }

        ImGui.Spacing();
        ImGui.Spacing();

        // Safety Filters Section
        ImGui.TextColored(ColorInfo, "SAFETY FILTERS");
        ImGui.Separator();
        DrawSidebarSafetyFilters();

        ImGui.Spacing();
        ImGui.Spacing();

        // Market Section
        ImGui.TextColored(ColorInfo, "MARKET");
        ImGui.Separator();
        var showPrices = Settings.ShowMarketPrices;
        if (ImGui.Checkbox("Show Prices", ref showPrices))
        {
            Settings.ShowMarketPrices = showPrices;
            _plugin.ConfigManager.SaveConfiguration();
        }

        if (Settings.ShowMarketPrices)
        {
            ImGui.Text("World:");
            ImGui.SetNextItemWidth(-1);
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

            ImGui.Spacing();

            // Total value
            long totalValue;
            lock (_stateLock)
            {
                totalValue = _categories.Sum(c => c.TotalValue ?? 0);
            }

            using (var font = ImRaii.PushFont(UiBuilder.IconFont))
            {
                ImGui.TextColored(ColorWarning, FontAwesomeIcon.Coins.ToIconString());
            }
            ImGui.SameLine(0, 4);
            ImGui.TextColored(ColorPrice, $"{totalValue:N0}");

            ImGui.Spacing();
            ImGui.TextColored(ColorSubdued, "via Universalis");
        }
    }

    private void DrawSidebarSafetyFilters()
    {
        var filters = Settings.SafetyFilters;
        bool changed = false;

        // Hardcoded filters (always on) - shown as disabled checkboxes
        ImGui.TextColored(ColorSubdued, "Always Protected:");
        using (var disabled = ImRaii.Disabled())
        {
            bool alwaysOn = true;
            ImGui.Checkbox("Ultimate Tokens", ref alwaysOn);
            ImGui.Checkbox("Currency", ref alwaysOn);
            ImGui.Checkbox("Crystals/Shards", ref alwaysOn);
        }

        ImGui.Spacing();
        ImGui.TextColored(ColorSubdued, "Toggleable:");

        var filterGearset = filters.FilterGearsetItems;
        if (ImGui.Checkbox("Gearset Items", ref filterGearset))
        {
            filters.FilterGearsetItems = filterGearset;
            changed = true;
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Items currently equipped in any gearset");

        var filterIndisposable = filters.FilterIndisposableItems;
        if (ImGui.Checkbox("Protected Items", ref filterIndisposable))
        {
            filters.FilterIndisposableItems = filterIndisposable;
            changed = true;
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Items flagged by the game as indisposable");

        var filterHighLevel = filters.FilterHighLevelGear;
        if (ImGui.Checkbox("High iLvl Gear", ref filterHighLevel))
        {
            filters.FilterHighLevelGear = filterHighLevel;
            changed = true;
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Gear at or above the specified item level");

        if (filters.FilterHighLevelGear)
        {
            ImGui.SameLine();
            ImGui.SetNextItemWidth(45);
            int maxLevel = (int)filters.MaxGearItemLevel;
            if (ImGui.InputInt("##MaxiLvl", ref maxLevel, 0, 0))
            {
                filters.MaxGearItemLevel = (uint)Math.Max(1, Math.Min(999, maxLevel));
                changed = true;
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Minimum item level to protect");
        }

        var filterUnique = filters.FilterUniqueUntradeable;
        if (ImGui.Checkbox("Unique & Untrade", ref filterUnique))
        {
            filters.FilterUniqueUntradeable = filterUnique;
            changed = true;
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Items that are both unique AND untradeable");

        var filterHQ = filters.FilterHQItems;
        if (ImGui.Checkbox("HQ Items", ref filterHQ))
        {
            filters.FilterHQItems = filterHQ;
            changed = true;
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("High quality crafted items");

        var filterCollectables = filters.FilterCollectables;
        if (ImGui.Checkbox("Collectables", ref filterCollectables))
        {
            filters.FilterCollectables = filterCollectables;
            changed = true;
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Items marked as collectable for turn-ins");

        if (changed)
        {
            _plugin.ConfigManager.SaveConfiguration();
            UpdateCategories();
        }
    }

    private void DrawSearchBar()
    {
        // Calculate position for right-aligned search
        var searchWidth = 160f;
        var totalWidth = searchWidth + 4f;
        var availWidth = ImGui.GetContentRegionAvail().X;

        ImGui.SameLine(availWidth - totalWidth);

        // Search input with integrated refresh
        ImGui.SetNextItemWidth(searchWidth);
        if (ImGui.InputTextWithHint("##Search", "Search...", ref _searchFilter, 100))
        {
            UpdateCategories();
        }

        // Refresh on right-click of search box
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
        {
            RefreshInventory();
        }
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Right-click to refresh");
        }
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

        DrawUnifiedItemTable(categoriesCopy);
    }

    private void DrawUnifiedItemTable(List<CategoryGroup> categories)
    {
        using var style = ImRaii.PushStyle(ImGuiStyleVar.CellPadding, new Vector2(4, 3))
                                .Push(ImGuiStyleVar.ItemSpacing, new Vector2(4, 2));

        // Column count: Checkbox, ID, Item, Qty, iLvl + (Price, Total) or (Location)
        int columnCount = Settings.ShowMarketPrices ? 7 : 6;

        var flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit
                  | ImGuiTableFlags.ScrollY | ImGuiTableFlags.NoPadOuterX | ImGuiTableFlags.Sortable;

        using var table = ImRaii.Table("UnifiedItemTable", columnCount, flags);
        if (!table) return;

        // Setup columns - checkbox not sortable, others are
        ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize | ImGuiTableColumnFlags.NoSort, 26f);
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

        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableHeadersRow();

        // Handle sorting
        var sortSpecs = ImGui.TableGetSortSpecs();
        if (sortSpecs.SpecsDirty)
        {
            if (sortSpecs.SpecsCount > 0)
            {
                var spec = sortSpecs.Specs;
                _availableSortColumn = spec.ColumnIndex;
                _availableSortAscending = spec.SortDirection == ImGuiSortDirection.Ascending;
            }
            else
            {
                _availableSortColumn = -1;
            }
            sortSpecs.SpecsDirty = false;
        }

        // Show category groups, sort items within each category if sorting is active
        foreach (var category in categories)
        {
            if (category.Items.Count == 0) continue;

            DrawCategoryHeaderRow(category, columnCount);

            var isExpanded = ExpandedCategories.GetValueOrDefault(category.CategoryId, true);
            if (isExpanded)
            {
                var itemsToShow = _availableSortColumn > 0
                    ? SortAvailableItems(category.Items.ToList())
                    : category.Items;

                foreach (var item in itemsToShow)
                {
                    DrawItemRow(item);
                }
            }
        }
    }

    private List<InventoryItemInfo> SortAvailableItems(List<InventoryItemInfo> items)
    {
        if (_availableSortColumn < 0 || items.Count == 0)
            return items;

        // Column indices: 0=checkbox, 1=ID, 2=Item, 3=Qty, 4=iLvl, 5=Price/Location, 6=Total
        IOrderedEnumerable<InventoryItemInfo> sorted = _availableSortColumn switch
        {
            1 => _availableSortAscending ? items.OrderBy(i => i.ItemId) : items.OrderByDescending(i => i.ItemId),
            2 => _availableSortAscending ? items.OrderBy(i => i.Name) : items.OrderByDescending(i => i.Name),
            3 => _availableSortAscending ? items.OrderBy(i => i.Quantity) : items.OrderByDescending(i => i.Quantity),
            4 => _availableSortAscending ? items.OrderBy(i => i.ItemLevel) : items.OrderByDescending(i => i.ItemLevel),
            5 => Settings.ShowMarketPrices
                ? (_availableSortAscending ? items.OrderBy(i => i.MarketPrice ?? 0) : items.OrderByDescending(i => i.MarketPrice ?? 0))
                : (_availableSortAscending ? items.OrderBy(i => i.Container.ToString()) : items.OrderByDescending(i => i.Container.ToString())),
            6 => _availableSortAscending ? items.OrderBy(i => (i.MarketPrice ?? 0) * i.Quantity) : items.OrderByDescending(i => (i.MarketPrice ?? 0) * i.Quantity),
            _ => items.OrderBy(i => i.Name)
        };

        return sorted.ToList();
    }

    private void DrawCategoryHeaderRow(CategoryGroup category, int columnCount)
    {
        var isExpanded = ExpandedCategories.GetValueOrDefault(category.CategoryId, true);

        ImGui.TableNextRow();
        ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(new Vector4(0.2f, 0.2f, 0.25f, 1f)));

        // First column - expand/collapse button
        ImGui.TableNextColumn();
        using (var font = ImRaii.PushFont(UiBuilder.IconFont))
        {
            var icon = isExpanded ? FontAwesomeIcon.ChevronDown : FontAwesomeIcon.ChevronRight;
            if (ImGui.SmallButton($"{icon.ToIconString()}##expand_{category.CategoryId}"))
            {
                ExpandedCategories[category.CategoryId] = !isExpanded;
                _expandedCategoriesChanged = true;
            }
        }

        // Second column - skip (ID column)
        ImGui.TableNextColumn();

        // Third column - category name and info (Item column - stretches)
        ImGui.TableNextColumn();
        ImGui.TextColored(new Vector4(0.9f, 0.9f, 0.9f, 1f), category.Name);
        ImGui.SameLine();
        ImGui.TextColored(ColorInfo, $"({category.Items.Count} items, {category.TotalQuantity} total)");

        if (Settings.ShowMarketPrices && category.TotalValue.HasValue)
        {
            ImGui.SameLine();
            ImGui.TextColored(ColorPrice, $"{category.TotalValue.Value:N0} gil");
        }

        // Remaining columns - empty
        ImGui.TableNextColumn(); // Qty
        ImGui.TableNextColumn(); // iLvl

        if (Settings.ShowMarketPrices)
        {
            ImGui.TableNextColumn(); // Price
            ImGui.TableNextColumn(); // Total
        }
        else
        {
            ImGui.TableNextColumn(); // Location
        }
    }

    private void DrawItemRow(InventoryItemInfo item)
    {
        ImGui.TableNextRow();
        using var id = ImRaii.PushId(item.GetUniqueKey());

        // Row background color
        bool isBlacklisted = BlacklistedItems.Contains(item.ItemId);
        bool isSelected = _selectedItems.Contains(item.ItemId);

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
                bool dummy = false;
                ImGui.Checkbox($"##check", ref dummy);
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("This item is blacklisted");
            }
        }
        else
        {
            bool selected = isSelected;
            if (ImGui.Checkbox($"##check", ref selected))
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
            }
        }

        // ID column
        ImGui.TableNextColumn();
        ImGui.TextColored(ColorSubdued, item.ItemId.ToString());

        // Item column
        ImGui.TableNextColumn();
        DrawItemNameCell(item);

        // Qty column
        ImGui.TableNextColumn();
        ImGui.Text(item.Quantity.ToString());

        // iLvl column
        ImGui.TableNextColumn();
        if (item.ItemLevel > 0)
            ImGui.Text(item.ItemLevel.ToString());
        else
            ImGui.TextColored(ColorSubdued, "-");

        // Price/Location columns
        if (Settings.ShowMarketPrices)
        {
            // Price column
            ImGui.TableNextColumn();
            DrawPriceCell(item);

            // Total column
            ImGui.TableNextColumn();
            DrawTotalCell(item);
        }
        else
        {
            // Location column
            ImGui.TableNextColumn();
            ImGui.Text(GetLocationName(item.Container));
        }
    }

    private void DrawItemNameCell(InventoryItemInfo item)
    {
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

        // Highlight search term if provided
        if (!string.IsNullOrWhiteSpace(_searchFilter) && !string.IsNullOrEmpty(item.Name))
        {
            var matchIndex = item.Name.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase);
            if (matchIndex >= 0)
            {
                if (matchIndex > 0)
                {
                    ImGui.Text(item.Name.Substring(0, matchIndex));
                    ImGui.SameLine(0, 0);
                }

                ImGui.TextColored(ColorWarning, item.Name.Substring(matchIndex, _searchFilter.Length));
                ImGui.SameLine(0, 0);

                if (matchIndex + _searchFilter.Length < item.Name.Length)
                {
                    ImGui.Text(item.Name.Substring(matchIndex + _searchFilter.Length));
                }
            }
            else
            {
                ImGui.Text(item.Name);
            }
        }
        else
        {
            ImGui.Text(item.Name ?? string.Empty);
        }

        // Item tags
        if (item.IsHQ)
        {
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.6f, 0.8f, 1f, 1f), "[HQ]");
        }

        if (BlacklistedItems.Contains(item.ItemId))
        {
            ImGui.SameLine();
            ImGui.TextColored(ColorError, "[Blacklisted]");
        }

        if (!item.CanBeTraded)
        {
            ImGui.SameLine();
            ImGui.TextColored(ColorSubdued, "[Untradeable]");
        }
    }

    private void DrawPriceCell(InventoryItemInfo item)
    {
        if (!item.CanBeTraded)
        {
            ImGui.TextColored(ColorSubdued, "-");
        }
        else if (item.MarketPrice.HasValue)
        {
            if (item.MarketPrice.Value > 0)
            {
                ImGui.TextColored(ColorPrice, $"{item.MarketPrice.Value:N0}");
            }
            else
            {
                ImGui.TextColored(ColorSubdued, "N/A");
            }
        }
        else
        {
            ImGui.TextColored(ColorSubdued, "...");
            if (!_priceService.IsFetchingPrice(item.ItemId))
            {
                _ = _priceService.FetchPrice(item).ContinueWith(task =>
                {
                    if (task.IsCompletedSuccessfully && task.Result.HasValue)
                    {
                        lock (_stateLock)
                        {
                            item.MarketPrice = task.Result.Value;
                            item.MarketPriceFetchTime = DateTime.Now;
                        }
                    }
                });
            }
        }
    }

    private void DrawTotalCell(InventoryItemInfo item)
    {
        if (!item.CanBeTraded || !item.MarketPrice.HasValue || item.MarketPrice.Value <= 0)
        {
            ImGui.TextColored(ColorSubdued, "-");
        }
        else
        {
            var total = item.MarketPrice.Value * item.Quantity;
            ImGui.Text($"{total:N0}");
        }
    }

    private string GetLocationName(FFXIVClientStructs.FFXIV.Client.Game.InventoryType container)
    {
        return container switch
        {
            FFXIVClientStructs.FFXIV.Client.Game.InventoryType.Inventory1 => "Inv 1",
            FFXIVClientStructs.FFXIV.Client.Game.InventoryType.Inventory2 => "Inv 2",
            FFXIVClientStructs.FFXIV.Client.Game.InventoryType.Inventory3 => "Inv 3",
            FFXIVClientStructs.FFXIV.Client.Game.InventoryType.Inventory4 => "Inv 4",
            FFXIVClientStructs.FFXIV.Client.Game.InventoryType.ArmoryMainHand => "Main Hand",
            FFXIVClientStructs.FFXIV.Client.Game.InventoryType.ArmoryOffHand => "Off Hand",
            FFXIVClientStructs.FFXIV.Client.Game.InventoryType.ArmoryHead => "Head",
            FFXIVClientStructs.FFXIV.Client.Game.InventoryType.ArmoryBody => "Body",
            FFXIVClientStructs.FFXIV.Client.Game.InventoryType.ArmoryHands => "Hands",
            FFXIVClientStructs.FFXIV.Client.Game.InventoryType.ArmoryLegs => "Legs",
            FFXIVClientStructs.FFXIV.Client.Game.InventoryType.ArmoryFeets => "Feet",
            FFXIVClientStructs.FFXIV.Client.Game.InventoryType.ArmoryEar => "Earrings",
            FFXIVClientStructs.FFXIV.Client.Game.InventoryType.ArmoryNeck => "Necklace",
            FFXIVClientStructs.FFXIV.Client.Game.InventoryType.ArmoryWrist => "Bracelets",
            FFXIVClientStructs.FFXIV.Client.Game.InventoryType.ArmoryRings => "Rings",
            _ => container.ToString()
        };
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
    
    private IEnumerable<uint> GetBlacklistItemsToShow() => FilterItemIdsBySearch(BlacklistedItems);

    private IEnumerable<uint> FilterItemIdsBySearch(HashSet<uint> itemIds)
    {
        if (string.IsNullOrWhiteSpace(_searchFilter))
            return itemIds;

        var filteredIds = new List<uint>();
        foreach (var itemId in itemIds)
        {
            var itemName = GetItemName(itemId);
            if (itemName.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase) ||
                itemId.ToString().Contains(_searchFilter))
            {
                filteredIds.Add(itemId);
            }
        }
        return filteredIds;
    }

    private string GetItemName(uint itemId)
    {
        lock (_stateLock)
        {
            var itemInfo = _allItems.FirstOrDefault(i => i.ItemId == itemId);
            if (itemInfo != null)
                return itemInfo.Name;
        }

        var info = _searchService.GetItemInfo(itemId);
        return info?.Name ?? $"Unknown Item ({itemId})";
    }

    private InventoryItemInfo? GetOrCreateItemInfo(uint itemId)
    {
        lock (_stateLock)
        {
            var existing = _allItems.FirstOrDefault(i => i.ItemId == itemId);
            if (existing != null)
                return existing;
        }

        var info = _searchService.GetItemInfo(itemId);
        if (!info.HasValue)
            return null;

        return new InventoryItemInfo
        {
            ItemId = itemId,
            Name = info.Value.Name,
            IconId = info.Value.IconId,
            CategoryName = info.Value.CategoryName,
            ItemLevel = (uint)info.Value.ItemLevel
        };
    }

    private List<InventoryItemInfo> ResolveItemInfos(IEnumerable<uint> itemIds)
    {
        return itemIds
            .Select(GetOrCreateItemInfo)
            .Where(i => i != null)
            .Cast<InventoryItemInfo>()
            .ToList();
    }

    private void DrawBlacklistTable(IEnumerable<uint> itemIds)
    {
        if (_itemTable == null)
            InitializeUIComponents();

        var items = ResolveItemInfos(itemIds);

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
    
    private IEnumerable<uint> GetAutoDiscardItemsToShow() => FilterItemIdsBySearch(AutoDiscardItems);

    private void DrawAutoDiscardTable(IEnumerable<uint> itemIds)
    {
        if (_itemTable == null)
            InitializeUIComponents();

        var items = ResolveItemInfos(itemIds);

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
        int selectedCount;
        lock (_stateLock)
        {
            selectedCount = _selectedItems.Count;
        }

        var hasSelection = selectedCount > 0;
        var hasAutoDiscardItems = AutoDiscardItems.Count > 0;

        ImGui.Spacing();

        // Selection count on left
        if (hasSelection)
        {
            ImGui.TextColored(ColorInfo, $"{selectedCount} selected");
        }
        else
        {
            ImGui.TextColored(ColorSubdued, "No items selected");
        }

        ImGui.SameLine();
        ImGui.TextColored(ColorSubdued, "|");
        ImGui.SameLine();

        // Discard button
        using (var disabled = ImRaii.Disabled(!hasSelection))
        {
            if (ImGui.SmallButton("Discard"))
            {
                List<uint> selectedItemIds;
                lock (_stateLock)
                {
                    selectedItemIds = _selectedItems.ToList();
                    foreach (var item in _originalItems.Where(i => selectedItemIds.Contains(i.ItemId)))
                    {
                        _priceService.UpdateItemPrice(item);
                    }
                }
                DiscardService.PrepareDiscard(selectedItemIds, _originalItems, BlacklistedItems);
            }
        }

        ImGui.SameLine();

        // Add to Blacklist button
        using (var disabled = ImRaii.Disabled(!hasSelection))
        {
            if (ImGui.SmallButton("Add to Blacklist"))
            {
                AddSelectedToBlacklist();
            }
        }

        ImGui.SameLine();

        // Add to Auto-Discard button
        using (var disabled = ImRaii.Disabled(!hasSelection))
        {
            if (ImGui.SmallButton("Add to Auto-Discard"))
            {
                AddSelectedToAutoDiscard();
            }
        }

        ImGui.SameLine();

        // Clear button
        using (var disabled = ImRaii.Disabled(!hasSelection))
        {
            if (ImGui.SmallButton("Clear"))
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
        }

        // Run Auto-Discard on right side
        var runText = hasAutoDiscardItems ? $"Run Auto-Discard ({AutoDiscardItems.Count})" : "Run Auto-Discard";
        var textWidth = ImGui.CalcTextSize(runText).X + 16;
        var availWidth = ImGui.GetContentRegionAvail().X;
        ImGui.SameLine(ImGui.GetCursorPosX() + availWidth - textWidth);

        using (var disabled = ImRaii.Disabled(!hasAutoDiscardItems))
        {
            if (ImGui.SmallButton(runText))
            {
                ExecuteAutoDiscard();
            }
        }
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            ImGui.SetTooltip(hasAutoDiscardItems
                ? "Discard all items in your auto-discard list"
                : "No items configured for auto-discard");
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

