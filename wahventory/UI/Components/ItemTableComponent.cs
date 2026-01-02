using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Bindings.ImGui;
using FFXIVClientStructs.FFXIV.Client.Game;
using wahventory.Models;
using wahventory.Services.Helpers;

namespace wahventory.UI.Components;

public class ItemTableComponent
{
    private readonly IconCache _iconCache;

    // Color constants
    private static readonly Vector4 ColorHQItem = new(0.6f, 0.8f, 1f, 1f);
    private static readonly Vector4 ColorError = new(0.8f, 0.2f, 0.2f, 1f);
    private static readonly Vector4 ColorNotTradeable = new(0.5f, 0.5f, 0.5f, 1f);
    private static readonly Vector4 ColorSubdued = new(0.6f, 0.6f, 0.6f, 1f);
    private static readonly Vector4 ColorPrice = new(1f, 0.8f, 0.2f, 1f);
    private static readonly Vector4 ColorWarning = new(0.9f, 0.5f, 0.1f, 1f);

    // Sort state
    private int _sortColumnIndex = -1;
    private bool _sortAscending = true;

    public ItemTableComponent(IconCache iconCache)
    {
        _iconCache = iconCache;
    }
    
    public void DrawTable(
        IEnumerable<InventoryItemInfo> items,
        ItemTableConfig config)
    {
        using var style = ImRaii.PushStyle(ImGuiStyleVar.CellPadding, new Vector2(4, 2))
                                .Push(ImGuiStyleVar.ItemSpacing, new Vector2(4, 2));

        var columnCount = CalculateColumnCount(config);
        var flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.NoPadOuterX | ImGuiTableFlags.Sortable;
        if (!config.NoBorders)
            flags |= ImGuiTableFlags.Borders;
        if (config.Scrollable)
            flags |= ImGuiTableFlags.ScrollY;

        bool anyItemHovered = false;

        using (var table = ImRaii.Table($"ItemTable_{config.TableId}", columnCount, flags))
        {
            if (table)
            {
                SetupColumns(config);
                if (config.ShowHeaders)
                    ImGui.TableHeadersRow();

                // Handle sorting
                var sortSpecs = ImGui.TableGetSortSpecs();
                if (sortSpecs.SpecsDirty)
                {
                    if (sortSpecs.SpecsCount > 0)
                    {
                        var spec = sortSpecs.Specs;
                        _sortColumnIndex = spec.ColumnIndex;
                        _sortAscending = spec.SortDirection == ImGuiSortDirection.Ascending;
                    }
                    sortSpecs.SpecsDirty = false;
                }

                // Sort items
                var sortedItems = SortItems(items.ToList(), config);

                foreach (var item in sortedItems)
                {
                    bool wasHovered = DrawItemRow(item, config);
                    if (wasHovered)
                    {
                        anyItemHovered = true;
                        config.OnItemHovered?.Invoke(item.ItemId);
                    }
                }
            }
        }

        if (!anyItemHovered)
        {
            config.OnNoItemHovered?.Invoke();
        }
    }

    private List<InventoryItemInfo> SortItems(List<InventoryItemInfo> items, ItemTableConfig config)
    {
        if (_sortColumnIndex < 0 || items.Count == 0)
            return items;

        // Map column index to actual column based on config
        var columnName = GetColumnNameByIndex(_sortColumnIndex, config);

        IOrderedEnumerable<InventoryItemInfo> sorted = columnName switch
        {
            "ID" => _sortAscending ? items.OrderBy(i => i.ItemId) : items.OrderByDescending(i => i.ItemId),
            "Item" => _sortAscending ? items.OrderBy(i => i.Name) : items.OrderByDescending(i => i.Name),
            "Qty" => _sortAscending ? items.OrderBy(i => i.Quantity) : items.OrderByDescending(i => i.Quantity),
            "iLvl" => _sortAscending ? items.OrderBy(i => i.ItemLevel) : items.OrderByDescending(i => i.ItemLevel),
            "Price" => _sortAscending ? items.OrderBy(i => i.MarketPrice ?? 0) : items.OrderByDescending(i => i.MarketPrice ?? 0),
            "Total" => _sortAscending ? items.OrderBy(i => (i.MarketPrice ?? 0) * i.Quantity) : items.OrderByDescending(i => (i.MarketPrice ?? 0) * i.Quantity),
            "Location" => _sortAscending ? items.OrderBy(i => i.Container.ToString()) : items.OrderByDescending(i => i.Container.ToString()),
            "Category" => _sortAscending ? items.OrderBy(i => i.CategoryName) : items.OrderByDescending(i => i.CategoryName),
            _ => items.OrderBy(i => i.Name)
        };

        return sorted.ToList();
    }

    private string GetColumnNameByIndex(int index, ItemTableConfig config)
    {
        var columns = new List<string>();

        if (config.ShowCheckbox) columns.Add("");
        columns.Add("ID");
        columns.Add("Item");
        columns.Add("Qty");
        if (config.ShowItemLevel) columns.Add("iLvl");
        if (config.ShowLocation) columns.Add("Location");
        if (config.ShowCategory) columns.Add("Category");
        if (config.ShowMarketPrices)
        {
            columns.Add("Price");
            if (config.ShowTotalValue) columns.Add("Total");
        }
        else if (config.ShowStatus) columns.Add("Status");
        else if (config.ShowReason) columns.Add("Reason");
        else if (config.ShowActions) columns.Add("Actions");

        return index >= 0 && index < columns.Count ? columns[index] : "";
    }

    private int CalculateColumnCount(ItemTableConfig config)
    {
        int count = 3; // ID, Item, Qty are always shown

        if (config.ShowCheckbox) count++;
        if (config.ShowItemLevel) count++;
        if (config.ShowLocation) count++;
        if (config.ShowCategory) count++;

        // These are mutually exclusive last columns
        if (config.ShowMarketPrices)
        {
            count++; // Price column
            if (config.ShowTotalValue) count++; // Total column
        }
        else if (config.ShowStatus) count++;
        else if (config.ShowReason) count++;
        else if (config.ShowActions) count++;

        return count;
    }
    
    // Static column widths for consistency across all tables
    private static readonly float CheckboxWidth = 26f;
    private static readonly float IdWidth = 50f;
    private static readonly float QtyWidth = 35f;
    private static readonly float ILvlWidth = 40f;
    private static readonly float LocationWidth = 110f;
    private static readonly float CategoryWidth = 130f;
    private static readonly float PriceWidth = 75f;
    private static readonly float TotalWidth = 90f;
    private static readonly float StatusWidth = 100f;
    private static readonly float ReasonWidth = 130f;
    private static readonly float ActionsWidth = 60f;

    private void SetupColumns(ItemTableConfig config)
    {
        if (config.ShowCheckbox)
        {
            ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, CheckboxWidth);
        }

        ImGui.TableSetupColumn("ID", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, IdWidth);
        ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch | ImGuiTableColumnFlags.NoHide);
        ImGui.TableSetupColumn("Qty", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, QtyWidth);

        if (config.ShowItemLevel)
        {
            ImGui.TableSetupColumn("iLvl", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, ILvlWidth);
        }

        if (config.ShowLocation)
        {
            ImGui.TableSetupColumn("Location", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, LocationWidth);
        }

        if (config.ShowCategory)
        {
            ImGui.TableSetupColumn("Category", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, CategoryWidth);
        }

        if (config.ShowMarketPrices)
        {
            ImGui.TableSetupColumn("Price", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, PriceWidth);

            if (config.ShowTotalValue)
            {
                ImGui.TableSetupColumn("Total", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, TotalWidth);
            }
        }
        else if (config.ShowStatus)
        {
            ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, StatusWidth);
        }
        else if (config.ShowReason)
        {
            ImGui.TableSetupColumn("Reason", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, ReasonWidth);
        }
        else if (config.ShowActions)
        {
            ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, ActionsWidth);
        }

        if (config.Scrollable)
        {
            ImGui.TableSetupScrollFreeze(0, 1);
        }
    }
    
    private bool DrawItemRow(InventoryItemInfo item, ItemTableConfig config)
    {
        ImGui.TableNextRow();
        using var id = ImRaii.PushId(item.GetUniqueKey());

        // Store row position for hover detection
        var rowMinY = ImGui.GetCursorScreenPos().Y;

        // Row background color
        if (config.IsItemBlacklisted != null && config.IsItemBlacklisted(item))
        {
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(new Vector4(0.3f, 0.1f, 0.1f, 0.3f)));
        }
        else if (config.IsItemSelected != null && config.IsItemSelected(item))
        {
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(new Vector4(0.3f, 0.5f, 0.7f, 0.3f)));
        }
        
        // Checkbox column
        if (config.ShowCheckbox)
        {
            ImGui.TableNextColumn();
            DrawCheckbox(item, config);
        }
        
        // ID column
        ImGui.TableNextColumn();
        ImGui.TextColored(ColorSubdued, item.ItemId.ToString());
        
        // Item column
        ImGui.TableNextColumn();
        DrawItemName(item, config);
        
        // Quantity column
        ImGui.TableNextColumn();
        ImGui.Text(item.Quantity.ToString());
        
        // Item Level column
        if (config.ShowItemLevel)
        {
            ImGui.TableNextColumn();
            if (item.ItemLevel > 0)
            {
                ImGui.Text(item.ItemLevel.ToString());
            }
            else
            {
                ImGui.TextColored(ColorSubdued, "-");
            }
        }
        
        // Location column
        if (config.ShowLocation)
        {
            ImGui.TableNextColumn();
            ImGui.Text(GetLocationName(item.Container));
        }
        
        // Category column
        if (config.ShowCategory)
        {
            ImGui.TableNextColumn();
            ImGui.TextColored(ColorSubdued, item.CategoryName);
        }
        
        // Market Price columns
        if (config.ShowMarketPrices)
        {
            ImGui.TableNextColumn();
            DrawItemPrice(item, config);
            
            if (config.ShowTotalValue)
            {
                ImGui.TableNextColumn();
                DrawTotalValue(item);
            }
        }
        // Status/Reason/Actions column
        else if (config.ShowStatus)
        {
            ImGui.TableNextColumn();
            DrawItemStatus(item, config);
        }
        else if (config.ShowReason && config.GetFilterReason != null)
        {
            ImGui.TableNextColumn();
            ImGui.TextColored(ColorWarning, config.GetFilterReason(item));
        }
        else if (config.ShowActions && config.OnRemoveItem != null)
        {
            ImGui.TableNextColumn();
            if (ImGui.SmallButton($"Remove##{item.ItemId}"))
            {
                config.OnRemoveItem(item);
            }
        }

        // Detect row hover using the table row rect
        var rowMaxY = ImGui.GetCursorScreenPos().Y;
        var mousePos = ImGui.GetMousePos();
        var tableMinX = ImGui.GetWindowPos().X;
        var tableMaxX = tableMinX + ImGui.GetWindowWidth();
        bool isRowHovered = mousePos.Y >= rowMinY && mousePos.Y < rowMaxY &&
                            mousePos.X >= tableMinX && mousePos.X <= tableMaxX &&
                            ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem);

        return isRowHovered;
    }

    private void DrawCheckbox(InventoryItemInfo item, ItemTableConfig config)
    {
        if (config.IsItemBlacklisted != null && config.IsItemBlacklisted(item))
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
        else if (config.OnItemSelectionChanged != null)
        {
            bool isSelected = config.IsItemSelected != null && config.IsItemSelected(item);
            if (ImGui.Checkbox($"##check_{item.GetUniqueKey()}", ref isSelected))
            {
                config.OnItemSelectionChanged(item, isSelected);
            }
        }
    }
    
    private void DrawItemName(InventoryItemInfo item, ItemTableConfig config)
    {
        var startPos = ImGui.GetCursorScreenPos();

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
        if (!string.IsNullOrWhiteSpace(config.SearchFilter) && !string.IsNullOrEmpty(item.Name))
        {
            var matchIndex = item.Name.IndexOf(config.SearchFilter, StringComparison.OrdinalIgnoreCase);
            if (matchIndex >= 0)
            {
                if (matchIndex > 0)
                {
                    ImGui.Text(item.Name.Substring(0, matchIndex));
                    ImGui.SameLine(0, 0);
                }

                ImGui.TextColored(ColorWarning, item.Name.Substring(matchIndex, config.SearchFilter.Length));
                ImGui.SameLine(0, 0);

                if (matchIndex + config.SearchFilter.Length < item.Name.Length)
                {
                    ImGui.Text(item.Name.Substring(matchIndex + config.SearchFilter.Length));
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
            ImGui.TextColored(ColorHQItem, "[HQ]");
        }

        if (config.IsItemBlacklisted != null && config.IsItemBlacklisted(item))
        {
            ImGui.SameLine();
            ImGui.TextColored(ColorError, "[Blacklisted]");
        }

        if (!item.CanBeTraded)
        {
            ImGui.SameLine();
            ImGui.TextColored(ColorNotTradeable, "[Not Tradeable]");
        }

        if (config.DrawItemTags != null)
        {
            config.DrawItemTags(item);
        }

        // Tooltip for gear items showing job info - check hover on entire cell area
        if (item.EquipSlotCategory > 0 && !string.IsNullOrEmpty(item.ClassJobCategoryName))
        {
            var endPos = ImGui.GetCursorScreenPos();
            var cellRect = new Vector2(ImGui.GetColumnWidth(), endPos.Y - startPos.Y + ImGui.GetTextLineHeight());
            var mousePos = ImGui.GetMousePos();
            if (mousePos.X >= startPos.X && mousePos.X <= startPos.X + cellRect.X &&
                mousePos.Y >= startPos.Y && mousePos.Y <= startPos.Y + cellRect.Y)
            {
                ImGui.SetTooltip($"Jobs: {item.ClassJobCategoryName}");
            }
        }
    }
    
    private void DrawItemPrice(InventoryItemInfo item, ItemTableConfig config)
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
            if (config.OnPriceFetchRequested != null && 
                (config.IsFetchingPrice == null || !config.IsFetchingPrice(item.ItemId)))
            {
                config.OnPriceFetchRequested(item);
            }
        }
    }
    
    private void DrawTotalValue(InventoryItemInfo item)
    {
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
    
    private void DrawItemStatus(InventoryItemInfo item, ItemTableConfig config)
    {
        if (config.ShowMarketPrices && item.MarketPrice.HasValue && item.CanBeTraded && 
            (config.IsItemBlacklisted == null || !config.IsItemBlacklisted(item)))
        {
            ImGui.TextColored(ColorPrice, $"{item.MarketPrice.Value:N0}");
        }
        else if (!item.CanBeDiscarded)
        {
            ImGui.TextColored(ColorError, "Not Discardable");
        }
        else if (item.IsCollectable)
        {
            ImGui.TextColored(ColorWarning, "Collectable");
        }
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

public class ItemTableConfig
{
    public string TableId { get; set; } = "ItemTable";
    public bool ShowCheckbox { get; set; } = false;
    public bool ShowItemLevel { get; set; } = true;
    public bool ShowLocation { get; set; } = true;
    public bool ShowCategory { get; set; } = false;
    public bool ShowMarketPrices { get; set; } = false;
    public bool ShowTotalValue { get; set; } = false;
    public bool ShowStatus { get; set; } = false;
    public bool ShowReason { get; set; } = false;
    public bool ShowActions { get; set; } = false;
    public bool Scrollable { get; set; } = false;
    public bool ShowHeaders { get; set; } = true;
    public bool NoBorders { get; set; } = false;
    public string SearchFilter { get; set; } = string.Empty;
    
    public Func<InventoryItemInfo, bool>? IsItemSelected { get; set; }
    public Func<InventoryItemInfo, bool>? IsItemBlacklisted { get; set; }
    public Action<InventoryItemInfo, bool>? OnItemSelectionChanged { get; set; }
    public Action<InventoryItemInfo>? OnRemoveItem { get; set; }
    public Func<InventoryItemInfo, string>? GetFilterReason { get; set; }
    public Action<InventoryItemInfo>? DrawItemTags { get; set; }
    public Func<uint, bool>? IsFetchingPrice { get; set; }
    public Action<InventoryItemInfo>? OnPriceFetchRequested { get; set; }
    public Action<uint>? OnItemHovered { get; set; }
    public Action? OnNoItemHovered { get; set; }
}

