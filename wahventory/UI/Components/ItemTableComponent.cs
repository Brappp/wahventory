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

    public ItemTableComponent(IconCache iconCache)
    {
        _iconCache = iconCache;
    }
    
    public void DrawTable(
        IEnumerable<InventoryItemInfo> items,
        ItemTableConfig config)
    {
        using var style = ImRaii.PushStyle(ImGuiStyleVar.CellPadding, new Vector2(6, 3))
                                .Push(ImGuiStyleVar.ItemSpacing, new Vector2(4, 2));

        var columnCount = config.ShowMarketPrices ? 7 : 6;
        var flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp;
        if (config.Scrollable)
            flags |= ImGuiTableFlags.ScrollY;
        
        using (var table = ImRaii.Table($"ItemTable_{config.TableId}", columnCount, flags))
        {
            if (table)
            {
                SetupColumns(config);
                ImGui.TableHeadersRow();
                
                foreach (var item in items)
                {
                    DrawItemRow(item, config);
                }
            }
        }
    }
    
    private void SetupColumns(ItemTableConfig config)
    {
        float checkboxWidth = 22;
        float qtyWidth = ImGui.CalcTextSize("999").X + 8;
        float ilvlWidth = ImGui.CalcTextSize("999").X + 8;
        float locationWidth = ImGui.CalcTextSize("P.Saddlebag 9").X + 8;
        float categoryWidth = ImGui.CalcTextSize("Seasonal Miscellany").X + 8;

        if (config.ShowCheckbox)
        {
            ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoResize, checkboxWidth);
        }

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

        if (config.Scrollable)
        {
            ImGui.TableSetupScrollFreeze(0, 1);
        }
    }
    
    private void DrawItemRow(InventoryItemInfo item, ItemTableConfig config)
    {
        ImGui.TableNextRow();
        using var id = ImRaii.PushId(item.GetUniqueKey());
        
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
                ImGui.TextColored(Theme.ColorSubdued, "-");
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
            ImGui.TextColored(Theme.ColorSubdued, item.CategoryName);
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
                
                ImGui.TextColored(Theme.ColorWarning, item.Name.Substring(matchIndex, config.SearchFilter.Length));
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

        // Capture hover BEFORE same-line tags so the name itself is the right-click target.
        bool nameHovered = config.OnDrawContextMenu != null && ImGui.IsItemHovered();

        // Item tags
        if (item.IsHQ)
        {
            ImGui.SameLine();
            ImGui.TextColored(Theme.ColorHQItem, "[HQ]");
        }
        
        if (config.IsItemBlacklisted != null && config.IsItemBlacklisted(item))
        {
            ImGui.SameLine();
            ImGui.TextColored(Theme.ColorError, "[Blacklisted]");
        }
        
        if (!item.CanBeTraded)
        {
            ImGui.SameLine();
            ImGui.TextColored(Theme.ColorNotTradeable, "[Not Tradeable]");
        }
        
        if (config.OnDrawContextMenu != null)
        {
            var popupId = $"ctx_{item.GetUniqueKey()}";
            if (nameHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
            {
                ImGui.OpenPopup(popupId);
            }
            if (ImGui.BeginPopup(popupId))
            {
                config.OnDrawContextMenu(item);
                ImGui.EndPopup();
            }
        }
    }

    private void DrawItemPrice(InventoryItemInfo item, ItemTableConfig config)
    {
        if (!item.CanBeTraded)
        {
            ImGui.TextColored(Theme.ColorSubdued, "Untradable");
        }
        else if (item.MarketPrice.HasValue)
        {
            if (item.MarketPrice.Value > 0)
            {
                ImGui.TextColored(Theme.ColorPrice, $"{item.MarketPrice.Value:N0}g");
            }
            else
            {
                ImGui.TextColored(Theme.ColorSubdued, "No data");
            }
        }
        else
        {
            ImGui.TextColored(Theme.ColorSubdued, "Loading...");
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
                ImGui.TextColored(Theme.ColorNotTradeable, "N/A");
            }
            else
            {
                var total = item.MarketPrice.Value * item.Quantity;
                ImGui.Text($"{total:N0}g");
            }
        }
        else
        {
            ImGui.TextColored(Theme.ColorNotTradeable, "---");
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
    public bool Scrollable { get; set; } = false;
    public string SearchFilter { get; set; } = string.Empty;
    
    public Func<InventoryItemInfo, bool>? IsItemSelected { get; set; }
    public Func<InventoryItemInfo, bool>? IsItemBlacklisted { get; set; }
    public Action<InventoryItemInfo, bool>? OnItemSelectionChanged { get; set; }
    public Func<uint, bool>? IsFetchingPrice { get; set; }
    public Action<InventoryItemInfo>? OnPriceFetchRequested { get; set; }
    public Action<InventoryItemInfo>? OnDrawContextMenu { get; set; }
}

