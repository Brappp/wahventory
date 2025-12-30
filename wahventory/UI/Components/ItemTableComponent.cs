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

        var columnCount = config.ShowMarketPrices ? 8 : 7;
        var flags = ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable;
        if (config.Scrollable)
            flags |= ImGuiTableFlags.ScrollY;

        bool anyItemHovered = false;

        using (var table = ImRaii.Table($"ItemTable_{config.TableId}", columnCount, flags))
        {
            if (table)
            {
                SetupColumns(config);
                ImGui.TableHeadersRow();

                foreach (var item in items)
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
    
    private void SetupColumns(ItemTableConfig config)
    {
        float checkboxWidth = 22;
        float idWidth = ImGui.CalcTextSize("99999").X + 8;
        float qtyWidth = ImGui.CalcTextSize("999").X + 8;
        float ilvlWidth = ImGui.CalcTextSize("999").X + 8;
        float locationWidth = ImGui.CalcTextSize("P.Saddlebag 9").X + 8;
        float categoryWidth = ImGui.CalcTextSize("Seasonal Miscellany").X + 8;
        
        if (config.ShowCheckbox)
        {
            ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, checkboxWidth);
        }
        
        ImGui.TableSetupColumn("ID", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoHide, idWidth);
        ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch | ImGuiTableColumnFlags.NoHide);
        ImGui.TableSetupColumn("Qty", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoHide, qtyWidth);
        
        if (config.ShowItemLevel)
        {
            ImGui.TableSetupColumn("iLvl", ImGuiTableColumnFlags.WidthFixed, ilvlWidth);
        }
        
        if (config.ShowLocation)
        {
            ImGui.TableSetupColumn("Location", ImGuiTableColumnFlags.WidthFixed, locationWidth);
        }
        
        if (config.ShowCategory)
        {
            ImGui.TableSetupColumn("Category", ImGuiTableColumnFlags.WidthFixed, categoryWidth);
        }
        
        if (config.ShowMarketPrices)
        {
            float priceWidth = ImGui.CalcTextSize("999,999g").X + 8;
            ImGui.TableSetupColumn("Price", ImGuiTableColumnFlags.WidthFixed, priceWidth);
            
            if (config.ShowTotalValue)
            {
                float totalWidth = ImGui.CalcTextSize("9,999,999g").X + 8;
                ImGui.TableSetupColumn("Total", ImGuiTableColumnFlags.WidthFixed, totalWidth);
            }
        }
        else if (config.ShowStatus)
        {
            float statusWidth = ImGui.CalcTextSize("Not Discardable").X + 8;
            ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, statusWidth);
        }
        else if (config.ShowReason)
        {
            float reasonWidth = ImGui.CalcTextSize("Unique & Untradeable").X + 8;
            ImGui.TableSetupColumn("Reason", ImGuiTableColumnFlags.WidthFixed, reasonWidth);
        }
        else if (config.ShowActions)
        {
            float actionsWidth = ImGui.CalcTextSize("Remove").X + 16;
            ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, actionsWidth);
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
        else if (item.SpiritBond >= 100)
        {
            ImGui.TextColored(new Vector4(0.2f, 0.8f, 0.2f, 1f), "Spiritbonded");
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

