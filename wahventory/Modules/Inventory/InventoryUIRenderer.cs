using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Bindings.ImGui;
using wahventory.Models;
using wahventory.Services;
using wahventory.Services.Helpers;
using wahventory.UI;
using wahventory.UI.Components;

namespace wahventory.Modules.Inventory;

internal sealed class InventoryUIRenderer
{
    private readonly InventoryManagementModule _module;
    private readonly ItemFilterService _filterService;
    private readonly ItemSearchService _searchService;
    private readonly PriceService _priceService;
    private readonly PassiveDiscardService _passiveDiscardService;

    private readonly FilterPanelComponent _filterPanel;
    private readonly ItemTableComponent _itemTable;
    private readonly SearchComponent _blacklistSearch;
    private readonly SearchComponent _autoDiscardSearch;

    public InventoryUIRenderer(
        InventoryManagementModule module,
        ItemFilterService filterService,
        ItemSearchService searchService,
        PriceService priceService,
        PassiveDiscardService passiveDiscardService,
        IconCache iconCache)
    {
        _module = module;
        _filterService = filterService;
        _searchService = searchService;
        _priceService = priceService;
        _passiveDiscardService = passiveDiscardService;

        _filterPanel = new FilterPanelComponent();
        _filterPanel.OnFiltersChanged += () =>
        {
            _module.SaveConfig();
            _module.UpdateCategories();
        };

        _itemTable = new ItemTableComponent(iconCache);
        _blacklistSearch = new SearchComponent(_searchService, iconCache);
        _blacklistSearch.OnItemSelected += (itemId) =>
        {
            if (!_module.BlacklistedItems.Contains(itemId))
            {
                _module.BlacklistedItems.Add(itemId);
                _module.SaveBlacklist();
                _module.RefreshInventory();
            }
        };

        _autoDiscardSearch = new SearchComponent(_searchService, iconCache);
        _autoDiscardSearch.OnItemSelected += (itemId) =>
        {
            if (!_module.AutoDiscardItems.Contains(itemId))
            {
                _module.AutoDiscardItems.Add(itemId);
                _module.SaveAutoDiscard();
                _module.RefreshInventory();
            }
        };
    }

    public void Draw()
    {
        DrawTopControls();
        DrawFiltersAndSettings();

        ImGui.Separator();
        var windowHeight = ImGui.GetWindowHeight();
        var currentY = ImGui.GetCursorPosY();
        var bottomBarHeight = 42f;
        var separatorHeight = ImGui.GetStyle().ItemSpacing.Y * 2 + 2;
        var tabBarHeight = ImGui.GetFrameHeight();
        var contentHeight = windowHeight - currentY - bottomBarHeight - separatorHeight - tabBarHeight - 10f;
        contentHeight = Math.Max(100f, contentHeight);

        using (var tabBar = ImRaii.TabBar("InventoryTabs"))
        {
            if (tabBar)
            {
                var filteredItems = _module.GetProtectedItems();
                var availableCount = _module._state.CategoryItemTotal;

                string availableTabText = $"Available Items ({availableCount})###AvailableTab";
                using (var tabItem = ImRaii.TabItem(availableTabText))
                {
                    if (tabItem)
                    {
                        using (var child = ImRaii.Child("AvailableContent", new Vector2(0, contentHeight), false))
                        {
                            DrawAvailableItemsTab();
                        }
                    }
                }

                string protectedTabText = $"Protected Items ({filteredItems.Count})###ProtectedTab";
                using (var tabItem = ImRaii.TabItem(protectedTabText))
                {
                    if (tabItem)
                    {
                        using (var child = ImRaii.Child("ProtectedContent", new Vector2(0, contentHeight), false))
                        {
                            DrawProtectedItemsTab(filteredItems);
                        }
                    }
                }

                string blacklistTabText = "Blacklist Management###BlacklistTab";
                using (var tabItem = ImRaii.TabItem(blacklistTabText))
                {
                    if (tabItem)
                    {
                        using (var child = ImRaii.Child("BlacklistContent", new Vector2(0, contentHeight), false))
                        {
                            DrawBlacklistTab();
                        }
                    }
                }

                string autoDiscardTabText = "Auto Discard###AutoDiscardTab";
                using (var tabItem = ImRaii.TabItem(autoDiscardTabText))
                {
                    if (tabItem)
                    {
                        using (var child = ImRaii.Child("AutoDiscardContent", new Vector2(0, contentHeight), false))
                        {
                            DrawAutoDiscardTab();
                        }
                    }
                }
            }
        }

        ImGui.Separator();
        DrawBottomActionBar();
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
            if (ImGui.InputTextWithHint("##Search", "Search items...", ref _module._searchFilter, 100))
            {
                _module.UpdateCategories();
            }

            if (!string.IsNullOrWhiteSpace(_module._searchFilter))
            {
                ImGui.SameLine();
                using (var colors = ImRaii.PushColor(ImGuiCol.Button, new Vector4(0.3f, 0.3f, 0.3f, 0.3f))
                                          .Push(ImGuiCol.ButtonHovered, new Vector4(0.4f, 0.4f, 0.4f, 0.5f)))
                {
                    if (ImGui.SmallButton("×"))
                    {
                        _module._searchFilter = string.Empty;
                        _module.UpdateCategories();
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
                    _module.RefreshInventory();
                }
            }
            ImGui.SameLine();
            ImGui.Text("Refresh");

            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.3f, 0.3f, 0.3f, 1f), "|");

            ImGui.SameLine();
            if (ImGui.Checkbox("Armory", ref _module._showArmory))
            {
                _module.RefreshInventory();
            }

            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.3f, 0.3f, 0.3f, 1f), "|");

            ImGui.SameLine();
            var settings = _module.Settings;
            var showPrices = settings.ShowMarketPrices;
            if (ImGui.Checkbox("Show Prices", ref showPrices))
            {
                settings.ShowMarketPrices = showPrices;
                _module.SaveConfig();
            }

            if (settings.ShowMarketPrices)
            {
                ImGui.SameLine();
                ImGui.Text("World:");
                ImGui.SameLine();
                ImGui.SetNextItemWidth(100);
                using (var combo = ImRaii.Combo("##World", _module._selectedWorld))
                {
                    if (combo)
                    {
                        foreach (var world in _module._availableWorlds)
                        {
                            bool isSelected = world == _module._selectedWorld;
                            if (ImGui.Selectable(world, isSelected))
                            {
                                _module._selectedWorld = world;
                                _priceService.UpdateWorld(_module._selectedWorld);
                                _priceService.ClearCache();
                                _module._state.ClearAllPrices();
                            }
                        }
                    }
                }
            }

            var windowWidth = ImGui.GetWindowContentRegionMax().X;
            var totalValue = _module._state.TotalCategoryValue;

            var totalText = $"Total: {totalValue:N0} gil";
            var totalTextWidth = ImGui.CalcTextSize(totalText).X;
            ImGui.SameLine(windowWidth - totalTextWidth);

            using (var font = ImRaii.PushFont(UiBuilder.IconFont))
            {
                ImGui.TextColored(Theme.ColorWarning, FontAwesomeIcon.Coins.ToIconString());
            }
            ImGui.SameLine(0, 4);
            ImGui.TextColored(Theme.ColorPrice, $"{totalValue:N0} gil");
        }

        ImGui.Spacing();
    }

    private void DrawFiltersAndSettings()
    {
        _filterPanel.Draw(_module.Settings);
    }

    private void DrawAvailableItemsTab()
    {
        var categoriesCopy = _module._state.SnapshotCategories();

        if (!string.IsNullOrWhiteSpace(_module._searchFilter))
        {
            DrawSearchResultsView(categoriesCopy);
            return;
        }

        var settings = _module.Settings;
        var expanded = _module.ExpandedCategories;
        foreach (var category in categoriesCopy)
        {
            if (category.Items.Count == 0) continue;

            var isExpanded = expanded.GetValueOrDefault(category.CategoryId, true);
            using var id = ImRaii.PushId($"Category_{category.CategoryId}");
            var nodeFlags = ImGuiTreeNodeFlags.AllowItemOverlap | ImGuiTreeNodeFlags.SpanAvailWidth;
            if (isExpanded) nodeFlags |= ImGuiTreeNodeFlags.DefaultOpen;

            using (var node = ImRaii.TreeNode($"{category.Name}###{category.CategoryId}_node", nodeFlags))
            {
                ImGui.SameLine();
                ImGui.TextColored(Theme.ColorInfo, $"({category.Items.Count} items, {category.TotalQuantity} total)");

                if (settings.ShowMarketPrices && category.TotalValue.HasValue)
                {
                    ImGui.SameLine();
                    ImGui.TextColored(Theme.ColorPrice, $"{category.TotalValue.Value:N0} gil");
                }

                var selectAllWidth = 90f;
                var windowWidth = ImGui.GetWindowContentRegionMax().X;
                ImGui.SameLine(windowWidth - selectAllWidth - 10);
                DrawCategoryControls(category);

                if (node)
                {
                    expanded[category.CategoryId] = true;
                    _module.MarkExpansionChanged();
                    DrawCategoryItems(category);
                }
                else
                {
                    expanded[category.CategoryId] = false;
                    _module.MarkExpansionChanged();
                }

                ImGui.Spacing();
            }
        }
    }

    private void DrawCategoryItems(CategoryGroup category)
    {
        var settings = _module.Settings;
        var config = new ItemTableConfig
        {
            TableId = $"CategoryTable_{category.CategoryId}",
            ShowCheckbox = true,
            ShowItemLevel = true,
            ShowLocation = true,
            ShowMarketPrices = settings.ShowMarketPrices,
            ShowTotalValue = settings.ShowMarketPrices,
            SearchFilter = _module._searchFilter,
            IsItemSelected = (item) => _module._state.IsSelected(item.ItemId),
            IsItemBlacklisted = (item) => _module.BlacklistedItems.Contains(item.ItemId),
            OnItemSelectionChanged = (item, selected) =>
            {
                if (selected) _module._state.Select(item);
                else _module._state.Deselect(item);
            },
            IsFetchingPrice = (itemId) => _priceService.IsFetchingPrice(itemId),
            OnPriceFetchRequested = (item) => _ = _priceService.FetchPrice(item).ContinueWith(task =>
            {
                if (task.IsCompletedSuccessfully && task.Result.HasValue)
                {
                    _module._state.SetItemPrice(item, task.Result.Value, DateTime.Now);
                }
            })
        };

        _itemTable.DrawTable(category.Items, config);
    }

    private void DrawCategoryControls(CategoryGroup category)
    {
        var allSelectableSelected = _module._state.AreAllSelectableSelected(category, _module.BlacklistedItems);
        var buttonText = allSelectableSelected ? "Deselect All" : "Select All";

        if (ImGui.SmallButton(buttonText))
        {
            if (allSelectableSelected)
                _module._state.DeselectAllInCategory(category);
            else
                _module._state.SelectAllSelectableInCategory(category, _module.BlacklistedItems);
        }
    }

    private void DrawSearchResultsView(List<CategoryGroup> categories)
    {
        ImGui.Text("Search Results for: ");
        ImGui.SameLine();
        ImGui.TextColored(Theme.ColorInfo, $"\"{_module._searchFilter}\"");
        ImGui.Separator();
        ImGui.Spacing();

        var allMatchingItems = new List<InventoryItemInfo>();
        foreach (var category in categories)
        {
            allMatchingItems.AddRange(category.Items);
        }

        if (!allMatchingItems.Any())
        {
            ImGui.TextColored(Theme.ColorSubdued, "No items found in available inventory.");
            ImGui.Spacing();
            ImGui.Text("Items might be:");
            ImGui.BulletText("Protected by active filters (check Protected Items tab)");
            ImGui.BulletText("In your blacklist (check Blacklist Management tab)");
            ImGui.BulletText("Not matching your search term");
            return;
        }

        var settings = _module.Settings;
        var config = new ItemTableConfig
        {
            TableId = "SearchResultsTable",
            ShowCheckbox = true,
            ShowItemLevel = true,
            ShowLocation = true,
            ShowCategory = true,
            ShowMarketPrices = settings.ShowMarketPrices,
            Scrollable = true,
            SearchFilter = _module._searchFilter,
            IsItemSelected = (item) => _module._state.IsSelected(item.ItemId),
            IsItemBlacklisted = (item) => _module.BlacklistedItems.Contains(item.ItemId),
            OnItemSelectionChanged = (item, selected) =>
            {
                if (selected) _module._state.Select(item);
                else _module._state.Deselect(item);
            },
            IsFetchingPrice = (itemId) => _priceService.IsFetchingPrice(itemId),
            OnPriceFetchRequested = (item) => _ = _priceService.FetchPrice(item).ContinueWith(task =>
            {
                if (task.IsCompletedSuccessfully && task.Result.HasValue)
                {
                    _module._state.SetItemPrice(item, task.Result.Value, DateTime.Now);
                }
            })
        };

        _itemTable.DrawTable(allMatchingItems, config);
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

        var settings = _module.Settings;
        var expanded = _module.ExpandedCategories;
        foreach (var category in filteredCategories)
        {
            var isExpanded = expanded.GetValueOrDefault(category.CategoryId, true);
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
                        expanded[category.CategoryId] = true;
                        _module.MarkExpansionChanged();

                        var config = new ItemTableConfig
                        {
                            TableId = $"ProtectedTable_{category.CategoryId}",
                            ShowItemLevel = true,
                            ShowLocation = true,
                            ShowMarketPrices = settings.ShowMarketPrices,
                            ShowTotalValue = settings.ShowMarketPrices,
                            ShowReason = true,
                            GetFilterReason = (item) => _filterService.GetFilterReason(item, settings.SafetyFilters)
                        };

                        _itemTable.DrawTable(category.Items, config);
                    }
                    else
                    {
                        expanded[category.CategoryId] = false;
                        _module.MarkExpansionChanged();
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

        _blacklistSearch.Draw("Add New Item to Blacklist", _module.BlacklistedItems);

        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Text($"Your Custom Blacklist ({_module.BlacklistedItems.Count} items)");
        ImGui.Spacing();

        if (!_module.BlacklistedItems.Any())
        {
            ImGui.TextColored(Theme.ColorSubdued, "No custom blacklisted items.");
            ImGui.TextColored(Theme.ColorInfo, "Add items using the controls above or select items in the Available Items tab and click 'Add to Blacklist'.");
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
                ImGui.Text($"This will remove {_module.BlacklistedItems.Count} items from your blacklist.");
                ImGui.Spacing();

                if (ImGui.Button("Yes, Clear All", new Vector2(120, 0)))
                {
                    _module.BlacklistedItems.Clear();
                    _module.SaveBlacklist();
                    _module.RefreshInventory();
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
        var itemsToShow = _module.BlacklistedItems.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(_module._searchFilter))
        {
            var filteredIds = new List<uint>();
            foreach (var itemId in _module.BlacklistedItems)
            {
                var itemInfo = _module._state.FindAllItem(itemId);
                string itemName = itemInfo?.Name;

                if (string.IsNullOrEmpty(itemName))
                {
                    var info = _searchService.GetItemInfo(itemId);
                    itemName = info?.Name ?? $"Unknown Item ({itemId})";
                }

                if (itemName.Contains(_module._searchFilter, StringComparison.OrdinalIgnoreCase) ||
                    itemId.ToString().Contains(_module._searchFilter))
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
        var items = itemIds.Select(itemId =>
        {
            var itemInfo = _module._state.FindAllItem(itemId);

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
                _module.BlacklistedItems.Remove(item.ItemId);
                _module.SaveBlacklist();
                _module.RefreshInventory();
            }
        };

        _itemTable.DrawTable(items, config);
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

        _autoDiscardSearch.Draw("Add New Item to Auto-Discard", _module.AutoDiscardItems);

        ImGui.Separator();
        ImGui.Spacing();

        ImGui.Text($"Your Auto-Discard List ({_module.AutoDiscardItems.Count} items)");
        ImGui.Spacing();

        if (!_module.AutoDiscardItems.Any())
        {
            ImGui.TextColored(Theme.ColorSubdued, "No auto-discard items configured.");
            ImGui.TextColored(Theme.ColorInfo, "Add items using the controls above or select items in the Available Items tab and click 'Add to Auto-Discard'.");
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
                ImGui.Text($"This will remove {_module.AutoDiscardItems.Count} items from your auto-discard list.");
                ImGui.Spacing();

                if (ImGui.Button("Yes, Clear All", new Vector2(120, 0)))
                {
                    _module.AutoDiscardItems.Clear();
                    _module.SaveAutoDiscard();
                    _module.RefreshInventory();
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
        var itemsToShow = _module.AutoDiscardItems.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(_module._searchFilter))
        {
            var filteredIds = new List<uint>();
            foreach (var itemId in _module.AutoDiscardItems)
            {
                var itemInfo = _module._state.FindAllItem(itemId);
                string itemName = itemInfo?.Name;

                if (string.IsNullOrEmpty(itemName))
                {
                    var info = _searchService.GetItemInfo(itemId);
                    itemName = info?.Name ?? $"Unknown Item ({itemId})";
                }

                if (itemName.Contains(_module._searchFilter, StringComparison.OrdinalIgnoreCase) ||
                    itemId.ToString().Contains(_module._searchFilter))
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
        var items = itemIds.Select(itemId =>
        {
            var itemInfo = _module._state.FindAllItem(itemId);

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
                _module.AutoDiscardItems.Remove(item.ItemId);
                _module.SaveAutoDiscard();
                _module.RefreshInventory();
            },
            DrawItemTags = (item) =>
            {
                if (_module._state.ContainsAllItem(item.ItemId))
                {
                    ImGui.SameLine();
                    ImGui.TextColored(Theme.ColorWarning, "[In Inventory]");
                }
            }
        };

        _itemTable.DrawTable(items, config);
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

        var settings = _module.Settings;
        var enabled = settings.PassiveDiscard.Enabled;
        if (ImGui.Checkbox("Enable Passive Discard", ref enabled))
        {
            settings.PassiveDiscard.Enabled = enabled;
            _module.SaveConfig();
        }

        using (var disabled = ImRaii.Disabled(!settings.PassiveDiscard.Enabled))
        {
            ImGui.Spacing();
            ImGui.Text("Idle Time Required:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(100);
            var idleTime = settings.PassiveDiscard.IdleTimeSeconds;
            if (ImGui.InputInt("##IdleTime", ref idleTime, 5, 10))
            {
                settings.PassiveDiscard.IdleTimeSeconds = Math.Max(10, Math.Min(300, idleTime));
                _module.SaveConfig();
            }
            ImGui.SameLine();
            ImGui.Text("seconds");

            ImGui.Spacing();
            ImGui.Text("Zone Restrictions:");
            ImGui.TextWrapped("Passive discard only works in safe zones: Cities, Housing Areas, Inn Rooms, Barracks, Gold Saucer, and other non-combat areas.");
            ImGui.Spacing();
            ImGui.Text("Status:");
            ImGui.SameLine();

            var status = _passiveDiscardService.GetStatus(_module.AutoDiscardItems, _module._state.SnapshotOriginalItems(), _module.BlacklistedItems);
            DrawPassiveDiscardStatus(status);
        }
    }

    private void DrawPassiveDiscardStatus(PassiveDiscardStatus status)
    {
        switch (status.State)
        {
            case PassiveDiscardState.Disabled:
                ImGui.TextColored(Theme.ColorSubdued, "Disabled");
                break;
            case PassiveDiscardState.NoItems:
                ImGui.TextColored(Theme.ColorSubdued, "No items to discard");
                break;
            case PassiveDiscardState.PlayerBusy:
                ImGui.TextColored(Theme.ColorWarning, "Player Busy");
                break;
            case PassiveDiscardState.NotInAllowedZone:
                ImGui.TextColored(Theme.ColorWarning, "Not in Allowed Zone");
                break;
            case PassiveDiscardState.WaitingForIdle:
                ImGui.TextColored(Theme.ColorInfo, $"Waiting for idle ({status.IdleSeconds}/{status.RequiredIdleSeconds}s)");
                break;
            case PassiveDiscardState.Cooldown:
                ImGui.TextColored(Theme.ColorSubdued, $"Cooldown ({status.CooldownSecondsRemaining}s remaining)");
                break;
            case PassiveDiscardState.Ready:
                ImGui.TextColored(Theme.ColorSuccess, "Ready to execute auto-discard");
                break;
        }
    }

    private void DrawBottomActionBar()
    {
        using var style = ImRaii.PushStyle(ImGuiStyleVar.FramePadding, new Vector2(8, 6));
        using var color = ImRaii.PushColor(ImGuiCol.ChildBg, new Vector4(0.165f, 0.165f, 0.165f, 1f));

        using (var child = ImRaii.Child("ActionBar", new Vector2(0, 42), true, ImGuiWindowFlags.NoScrollbar))
        {
            var selectedCount = _module._state.SelectedCount;

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
                _module._state.ClearSelectionAndReset();
            }

            ImGui.SameLine();

            using (var btnColors = ImRaii.PushColor(ImGuiCol.Button, new Vector4(0.541f, 0.227f, 0.227f, 1f))
                                         .Push(ImGuiCol.ButtonHovered, new Vector4(0.641f, 0.327f, 0.327f, 1f)))
            {
                if (selectedCount > 0)
                {
                    if (ImGui.Button(discardButtonText, new Vector2(discardButtonWidth, 0)))
                    {
                        var selectedItemIds = _module._state.SnapshotSelectedIds();
                        _module.DiscardService.PrepareDiscard(selectedItemIds, _module._state.SnapshotOriginalItems(), _module.BlacklistedItems);
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
                        _module.AddSelectedToBlacklist();
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
                        _module.AddSelectedToAutoDiscard();
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
                bool hasAutoDiscardItems = _module.AutoDiscardItems.Count > 0;

                if (hasAutoDiscardItems)
                {
                    if (ImGui.Button(executeAutoDiscardText, new Vector2(executeAutoDiscardWidth, 0)))
                    {
                        _module.ExecuteAutoDiscard();
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
}
