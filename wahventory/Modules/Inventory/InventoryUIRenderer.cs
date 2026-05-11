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
using wahventory.Services.Helpers;
using wahventory.UI;
using wahventory.UI.Components;

namespace wahventory.Modules.Inventory;

internal sealed class InventoryUIRenderer
{
    private readonly InventoryManagementModule _module;
    private readonly ItemFilterService _filterService;
    private readonly PriceService _priceService;
    private readonly PassiveDiscardService _passiveDiscardService;
    private readonly InventoryListsWindow _listsWindow;
    private readonly InventorySettingsWindow _settingsWindow;

    private readonly FilterPanelComponent _filterPanel;
    private readonly ItemTableComponent _itemTable;

    public InventoryUIRenderer(
        InventoryManagementModule module,
        ItemFilterService filterService,
        ItemSearchService searchService,
        PriceService priceService,
        PassiveDiscardService passiveDiscardService,
        IconCache iconCache,
        InventoryListsWindow listsWindow,
        InventorySettingsWindow settingsWindow)
    {
        _module = module;
        _filterService = filterService;
        _priceService = priceService;
        _passiveDiscardService = passiveDiscardService;
        _listsWindow = listsWindow;
        _settingsWindow = settingsWindow;

        _filterPanel = new FilterPanelComponent();
        _filterPanel.OnFiltersChanged += () =>
        {
            _module.SaveConfig();
            _module.UpdateCategories();
        };

        _itemTable = new ItemTableComponent(iconCache);
    }

    public void Draw()
    {
        // Top: toolbar
        DrawToolbar();

        // Reserve space for the status bar at the bottom. Account for
        // ImGui's ItemSpacing.Y between the body and the status bar so the
        // parent window never has to scroll.
        var spacing = ImGui.GetStyle().ItemSpacing.Y;
        var statusBarHeight = 22f + spacing;
        var bodyHeight = ImGui.GetContentRegionAvail().Y - statusBarHeight;
        if (bodyHeight < 100) bodyHeight = 100;

        // Body: sidebar + main pane
        using (ImRaii.Child("Body", new Vector2(0, bodyHeight), false))
        {
            using (ImRaii.Child("Sidebar", new Vector2(210, 0), true))
            {
                DrawSidebar();
            }
            ImGui.SameLine();
            using (ImRaii.Child("MainPane", new Vector2(0, 0), false))
            {
                DrawMainPane();
            }
        }

        // Bottom: status bar
        DrawStatusBar();
    }

    // ─────────────────────────────────────────────────────────────
    // Toolbar
    // ─────────────────────────────────────────────────────────────

    private void DrawToolbar()
    {
        using var color = ImRaii.PushColor(ImGuiCol.ChildBg, new Vector4(0.145f, 0.145f, 0.145f, 1f));
        using var style = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(8, 4));

        using (ImRaii.Child("Toolbar", new Vector2(0, 38), true, ImGuiWindowFlags.NoScrollbar))
        {
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 2);

            // Search
            using (ImRaii.PushFont(UiBuilder.IconFont))
            {
                ImGui.Text(FontAwesomeIcon.Search.ToIconString());
            }
            ImGui.SameLine();
            ImGui.SetNextItemWidth(200);
            if (ImGui.InputTextWithHint("##Search", "Search items…", ref _module._searchFilter, 100))
            {
                _module.UpdateCategories();
            }
            if (!string.IsNullOrWhiteSpace(_module._searchFilter))
            {
                ImGui.SameLine();
                if (ImGui.SmallButton("×##ClearSearch"))
                {
                    _module._searchFilter = string.Empty;
                    _module.UpdateCategories();
                }
            }

            ImGui.SameLine();
            using (ImRaii.PushFont(UiBuilder.IconFont))
            {
                if (ImGui.Button($"{FontAwesomeIcon.Sync.ToIconString()}##Refresh", new Vector2(30, 0)))
                {
                    _module.RefreshInventory();
                }
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Refresh inventory");

            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.3f, 0.3f, 0.3f, 1f), "|");
            ImGui.SameLine();

            // World combo (only when prices on)
            if (_module.Settings.ShowMarketPrices)
            {
                ImGui.TextColored(Theme.ColorSubdued, "World:");
                ImGui.SameLine();
                ImGui.SetNextItemWidth(110);
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
                ImGui.SameLine();
                ImGui.TextColored(new Vector4(0.3f, 0.3f, 0.3f, 1f), "|");
                ImGui.SameLine();
            }

            // Lists button
            using (ImRaii.PushFont(UiBuilder.IconFont))
            {
                if (ImGui.Button($"{FontAwesomeIcon.ClipboardList.ToIconString()}##Lists", new Vector2(30, 0)))
                {
                    _listsWindow.Toggle();
                }
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Manage blacklist and auto-discard list");

            ImGui.SameLine();

            // Settings button
            using (ImRaii.PushFont(UiBuilder.IconFont))
            {
                if (ImGui.Button($"{FontAwesomeIcon.Cog.ToIconString()}##Settings", new Vector2(30, 0)))
                {
                    _settingsWindow.Toggle();
                }
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Settings (passive discard, prices)");

            // Total gil — right-aligned
            var totalValue = _module._state.TotalCategoryValue;
            var totalText = $"{totalValue:N0} gil";
            var textWidth = ImGui.CalcTextSize(totalText).X;
            var iconWidth = 22f;
            ImGui.SameLine(ImGui.GetWindowContentRegionMax().X - textWidth - iconWidth - 6);
            using (ImRaii.PushFont(UiBuilder.IconFont))
            {
                ImGui.TextColored(Theme.ColorWarning, FontAwesomeIcon.Coins.ToIconString());
            }
            ImGui.SameLine(0, 4);
            ImGui.TextColored(Theme.ColorPrice, totalText);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Sidebar
    // ─────────────────────────────────────────────────────────────

    private void DrawSidebar()
    {
        var settings = _module.Settings;
        bool changed = false;

        using (ImRaii.PushColor(ImGuiCol.Text, Theme.ColorBlue))
        {
            ImGui.Text("DISPLAY");
        }
        ImGui.Separator();
        ImGui.Spacing();

        if (ImGui.Checkbox("Include armory", ref _module._showArmory))
        {
            _module.RefreshInventory();
        }
        var showPrices = settings.ShowMarketPrices;
        if (ImGui.Checkbox("Show market prices", ref showPrices))
        {
            settings.ShowMarketPrices = showPrices;
            changed = true;
        }

        ImGui.Spacing();
        ImGui.Spacing();

        // Filters — handled by FilterPanelComponent.DrawSidebar
        var counts = _filterService.CountHiddenPerFilter(
            _module._state.SnapshotOriginalItems(),
            settings.SafetyFilters);
        _filterPanel.DrawSidebar(settings, counts);

        // Footer: Reset / All on
        var bottomY = ImGui.GetWindowHeight() - 32;
        if (ImGui.GetCursorPosY() < bottomY) ImGui.SetCursorPosY(bottomY);
        ImGui.Separator();
        var halfW = (ImGui.GetContentRegionAvail().X - 6) / 2;
        if (ImGui.Button("Reset", new Vector2(halfW, 0)))
        {
            settings.SafetyFilters = new SafetyFilters();
            changed = true;
            _module.UpdateCategories();
        }
        ImGui.SameLine();
        if (ImGui.Button("All on", new Vector2(halfW, 0)))
        {
            var f = settings.SafetyFilters;
            f.FilterCurrencyItems = true;
            f.FilterCrystalsAndShards = true;
            f.FilterGearsetItems = true;
            f.FilterIndisposableItems = true;
            f.FilterUltimateTokens = true;
            f.FilterHQItems = true;
            f.FilterCollectables = true;
            f.FilterUniqueUntradeable = true;
            f.FilterHighLevelGear = true;
            f.FilterSpiritbondedItems = true;
            changed = true;
            _module.UpdateCategories();
        }

        if (changed)
        {
            _module.SaveConfig();
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Main pane
    // ─────────────────────────────────────────────────────────────

    private void DrawMainPane()
    {
        // Selection bar — only when items are selected
        if (_module._state.SelectedCount > 0)
        {
            DrawSelectionBar();
        }

        // Scrollable categories
        using (ImRaii.Child("Categories", new Vector2(0, 0), false))
        {
            DrawCategoriesAndItems();
        }
    }

    private void DrawSelectionBar()
    {
        using var color = ImRaii.PushColor(ImGuiCol.ChildBg, new Vector4(0.165f, 0.165f, 0.165f, 1f));

        using (ImRaii.Child("SelectionBar", new Vector2(0, 36), true, ImGuiWindowFlags.NoScrollbar))
        {
            var count = _module._state.SelectedCount;
            using (ImRaii.PushColor(ImGuiCol.Text, Theme.ColorBlue))
            {
                ImGui.AlignTextToFramePadding();
                ImGui.Text($"{count} selected");
            }
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(0.3f, 0.3f, 0.3f, 1f), "|");
            ImGui.SameLine();

            var selectedValue = _module._state.SumValueForSelected();
            ImGui.TextColored(Theme.ColorPrice, $"{selectedValue:N0} gil");

            // Right-aligned action buttons. Labels lead with the verb so it's
            // clear these add to lists vs. trigger the discard.
            const float clearW = 60, blW = 130, adW = 150, discW = 90, gap = 4;
            var totalBtnWidth = clearW + blW + adW + discW + gap * 3 + 6;
            ImGui.SameLine(ImGui.GetWindowContentRegionMax().X - totalBtnWidth);

            using (ImRaii.PushColor(ImGuiCol.Button, new Vector4(0, 0, 0, 0))
                                  .Push(ImGuiCol.ButtonHovered, new Vector4(0.3f, 0.3f, 0.3f, 0.4f))
                                  .Push(ImGuiCol.Text, Theme.ColorSubdued))
            {
                if (ImGui.Button("Clear", new Vector2(clearW, 0))) _module._state.ClearSelectionAndReset();
            }
            ImGui.SameLine();
            using (ImRaii.PushColor(ImGuiCol.Button, new Vector4(0.227f, 0.227f, 0.541f, 1f))
                                  .Push(ImGuiCol.ButtonHovered, new Vector4(0.327f, 0.327f, 0.641f, 1f)))
            {
                if (ImGui.Button("Add to blacklist", new Vector2(blW, 0))) _module.AddSelectedToBlacklist();
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Add selected items to the blacklist (they'll never be discarded).");
            ImGui.SameLine();
            using (ImRaii.PushColor(ImGuiCol.Button, new Vector4(0.541f, 0.341f, 0.127f, 1f))
                                  .Push(ImGuiCol.ButtonHovered, new Vector4(0.641f, 0.441f, 0.227f, 1f)))
            {
                if (ImGui.Button("Add to auto-discard", new Vector2(adW, 0))) _module.AddSelectedToAutoDiscard();
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Add selected items to the auto-discard list (discarded by /wahventory auto and passive discard).");
            ImGui.SameLine();
            using (ImRaii.PushColor(ImGuiCol.Button, new Vector4(0.541f, 0.227f, 0.227f, 1f))
                                  .Push(ImGuiCol.ButtonHovered, new Vector4(0.641f, 0.327f, 0.327f, 1f)))
            {
                if (ImGui.Button("Discard", new Vector2(discW, 0)))
                {
                    var ids = _module._state.SnapshotSelectedIds();
                    _module.DiscardService.PrepareDiscard(ids, _module._state.SnapshotOriginalItems(), _module.BlacklistedItems);
                }
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Discard the selected items now (with confirmation).");
        }
    }

    private void DrawCategoriesAndItems()
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
        _itemTable.DrawTable(category.Items, BuildItemTableConfig($"CategoryTable_{category.CategoryId}", scrollable: false, showCategory: false));
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
        ImGui.Text("Search results for:");
        ImGui.SameLine();
        ImGui.TextColored(Theme.ColorInfo, $"\"{_module._searchFilter}\"");
        ImGui.Spacing();

        var allMatchingItems = new List<InventoryItemInfo>();
        foreach (var category in categories)
        {
            allMatchingItems.AddRange(category.Items);
        }

        if (!allMatchingItems.Any())
        {
            ImGui.TextColored(Theme.ColorSubdued, "No items found in your inventory.");
            ImGui.Spacing();
            ImGui.TextColored(Theme.ColorSubdued, "Items might be hidden by filters in the sidebar, or in your blacklist (open Lists with the toolbar 📋).");
            return;
        }

        _itemTable.DrawTable(allMatchingItems, BuildItemTableConfig("SearchResultsTable", scrollable: true, showCategory: true));
    }

    private ItemTableConfig BuildItemTableConfig(string tableId, bool scrollable, bool showCategory)
    {
        var settings = _module.Settings;
        return new ItemTableConfig
        {
            TableId = tableId,
            ShowCheckbox = true,
            ShowItemLevel = true,
            ShowLocation = true,
            ShowCategory = showCategory,
            ShowMarketPrices = settings.ShowMarketPrices,
            ShowTotalValue = settings.ShowMarketPrices && !showCategory,
            Scrollable = scrollable,
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
            }),
            OnDrawContextMenu = DrawItemContextMenu,
        };
    }

    private void DrawItemContextMenu(InventoryItemInfo item)
    {
        ImGui.TextColored(Theme.ColorInfo, item.Name);
        ImGui.Separator();

        var isSelected = _module._state.IsSelected(item.ItemId);
        if (ImGui.MenuItem(isSelected ? "Deselect" : "Select"))
        {
            if (isSelected) _module._state.Deselect(item);
            else _module._state.Select(item);
        }

        ImGui.Separator();

        var isBlacklisted = _module.BlacklistedItems.Contains(item.ItemId);
        if (ImGui.MenuItem(isBlacklisted ? "Remove from blacklist" : "Add to blacklist"))
        {
            if (isBlacklisted)
            {
                _module.BlacklistedItems.Remove(item.ItemId);
            }
            else
            {
                _module.BlacklistedItems.Add(item.ItemId);
            }
            _module.SaveBlacklist();
            _module.RefreshInventory();
        }

        var isAutoDiscard = _module.AutoDiscardItems.Contains(item.ItemId);
        if (ImGui.MenuItem(isAutoDiscard ? "Remove from auto-discard" : "Add to auto-discard"))
        {
            if (isAutoDiscard)
            {
                _module.AutoDiscardItems.Remove(item.ItemId);
            }
            else
            {
                _module.AutoDiscardItems.Add(item.ItemId);
            }
            _module.SaveAutoDiscard();
            _module.RefreshInventory();
        }

        ImGui.Separator();

        using (ImRaii.PushColor(ImGuiCol.Text, Theme.ColorError))
        {
            if (ImGui.MenuItem($"Discard {item.Quantity}×"))
            {
                _module.DiscardService.PrepareDiscard(
                    new List<uint> { item.ItemId },
                    _module._state.SnapshotOriginalItems(),
                    _module.BlacklistedItems);
            }
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Status bar
    // ─────────────────────────────────────────────────────────────

    private void DrawStatusBar()
    {
        using var color = ImRaii.PushColor(ImGuiCol.ChildBg, new Vector4(0.1f, 0.1f, 0.1f, 1f));
        using (ImRaii.Child("StatusBar", new Vector2(0, 22), false, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 1);

            var status = _passiveDiscardService.GetStatus(
                _module.AutoDiscardItems,
                _module._state.SnapshotOriginalItems(),
                _module.BlacklistedItems);

            (Vector4 dotColor, string label) statusInfo = status.State switch
            {
                PassiveDiscardState.Disabled         => (Theme.ColorSubdued, "Passive auto-discard off"),
                PassiveDiscardState.NoItems          => (Theme.ColorSubdued, "Passive armed · no items pending"),
                PassiveDiscardState.PlayerBusy       => (Theme.ColorWarning, "Passive armed · player busy"),
                PassiveDiscardState.NotInAllowedZone => (Theme.ColorWarning, "Passive armed · not in safe zone"),
                PassiveDiscardState.WaitingForIdle   => (Theme.ColorInfo,    $"Passive armed · idle {status.IdleSeconds}/{status.RequiredIdleSeconds}s"),
                PassiveDiscardState.Cooldown         => (Theme.ColorSubdued, $"Passive cooldown · {status.CooldownSecondsRemaining}s"),
                PassiveDiscardState.Ready            => (Theme.ColorSuccess, "Passive ready to fire"),
                _                                    => (Theme.ColorSubdued, "—"),
            };

            ImGui.TextColored(statusInfo.dotColor, "●");
            ImGui.SameLine();
            using (ImRaii.PushColor(ImGuiCol.Text, Theme.ColorSubdued))
            {
                ImGui.Text(statusInfo.label);
            }

            // Right-side: blacklist + auto-discard counts
            var rightText = $"Blacklist {_module.BlacklistedItems.Count} · Auto-discard {_module.AutoDiscardItems.Count}";
            var rightWidth = ImGui.CalcTextSize(rightText).X;
            ImGui.SameLine(ImGui.GetWindowContentRegionMax().X - rightWidth - 4);
            ImGui.TextColored(Theme.ColorSubdued, rightText);
        }
    }
}
