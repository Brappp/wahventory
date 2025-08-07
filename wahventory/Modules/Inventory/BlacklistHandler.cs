using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Client.Game;
using Dalamud.Bindings.ImGui;
using wahventory.Services.Helpers;
using wahventory.Models;
using wahventory.Core;
using Lumina.Excel.Sheets;

namespace wahventory.Modules.Inventory;

public partial class InventoryManagementModule
{
    private string _itemNameToAdd = string.Empty;
    private uint _itemToAdd = 0;
    private List<(uint Id, string Name, ushort Icon)> _searchResults = new();
    
    private bool _searchingItems = false;
    private DateTime _lastSearchTime = DateTime.MinValue;
    private readonly TimeSpan _searchDelay = TimeSpan.FromMilliseconds(300);
    
    private void DrawBlacklistTab()
    {
        ImGui.TextWrapped("Manage your custom blacklist. Items added here will never be selected for discard.");
        ImGui.TextWrapped("This is in addition to the built-in safety lists shown in the Protected Items tab.");
        ImGui.Spacing();
        
        DrawAddToBlacklistSection();
        
        ImGui.Separator();
        ImGui.Spacing();
        
        DrawCurrentBlacklist();
    }
    
    private void DrawAddToBlacklistSection()
    {
        ImGui.Text("Add New Item to Blacklist:");
        ImGui.Spacing();
        
        ImGui.SetNextItemWidth(300);
        if (ImGui.InputTextWithHint("##AddItemName", "Search item name...", ref _itemNameToAdd, 100))
        {
            _lastSearchTime = DateTime.Now;
            _searchingItems = true;
        }
        
        if (_searchingItems && DateTime.Now - _lastSearchTime > _searchDelay)
        {
            SearchItems(_itemNameToAdd);
            _searchingItems = false;
        }
        
        ImGui.SameLine();
        
        ImGui.Text("or ID:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100);
        int itemId = (int)_itemToAdd;
        if (ImGui.InputInt("##AddItemId", ref itemId, 0, 0))
        {
            _itemToAdd = (uint)Math.Max(0, itemId);
        }
        
        ImGui.SameLine();
        
        var canAdd = _itemToAdd > 0;
        
        using (var disabled = ImRaii.Disabled(!canAdd))
        {
            if (ImGui.Button("Add to Blacklist"))
            {
                if (_itemToAdd > 0 && !BlacklistedItems.Contains(_itemToAdd))
                {
                    BlacklistedItems.Add(_itemToAdd);
                    SaveBlacklist();
                    RefreshInventory();
                    _itemToAdd = 0;
                    _itemNameToAdd = string.Empty;
                    _searchResults.Clear();
                }
            }
        }
        
        DrawSearchResults();
    }
    
    private void SearchItems(string searchTerm)
    {
        _searchResults.Clear();
        
        if (string.IsNullOrWhiteSpace(searchTerm) || searchTerm.Length < 2)
            return;
            
        try
        {
            var itemSheet = Plugin.DataManager.GetExcelSheet<Item>();
            if (itemSheet == null) return;
            
            var lowerSearch = searchTerm.ToLower();
            
            _searchResults = itemSheet
                .Where(item => item.RowId > 0 && 
                       !string.IsNullOrEmpty(item.Name.ExtractText()) &&
                       item.Name.ExtractText().ToLower().Contains(lowerSearch))
                .Select(item => ((uint)item.RowId, item.Name.ExtractText(), item.Icon))
                .OrderBy(item => item.Item2)
                .Take(20)
                .ToList();
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Failed to search items");
        }
    }
    
    private void DrawSearchResults()
    {
        if (!_searchResults.Any())
            return;
            
        ImGui.Spacing();
        ImGui.Text($"Search Results ({_searchResults.Count} items):");
        using (var child = ImRaii.Child("SearchResults", new Vector2(0, 200), true))
        {
            foreach (var (id, name, iconId) in _searchResults)
            {
                using var pushId = ImRaii.PushId($"SearchResult_{id}");
                if (iconId > 0)
                {
                    var icon = _iconCache.GetIcon(iconId);
                    if (icon != null)
                    {
                        ImGui.Image(icon.Handle, new Vector2(20, 20));
                        ImGui.SameLine();
                    }
                    else
                    {
                        ImGui.Dummy(new Vector2(20, 20));
                        ImGui.SameLine();
                    }
                }
                else
                {
                    ImGui.Dummy(new Vector2(20, 20));
                    ImGui.SameLine();
                }
                var isBlacklisted = BlacklistedItems.Contains(id);
                if (isBlacklisted)
                {
                    using (var color = ImRaii.PushColor(ImGuiCol.Text, ColorSubdued))
                    {
                        ImGui.Text($"{name} (ID: {id}) [Already Blacklisted]");
                    }
                }
                else
                {
                    if (ImGui.Selectable($"{name} (ID: {id})"))
                    {
                        _itemToAdd = id;
                        _itemNameToAdd = name;
                    }
                }
            }
        }
    }
    
    private void DrawCurrentBlacklist()
    {
        ImGui.Text($"Your Custom Blacklist ({BlacklistedItems.Count} items)");
        
        ImGui.Spacing();
        
        if (!BlacklistedItems.Any())
        {
            ImGui.TextColored(ColorSubdued, "No custom blacklisted items.");
            ImGui.TextColored(ColorInfo, "Add items using the controls above or select items in the Available Items tab and click 'Add to Blacklist'.");
        }
        else
        {
            var itemsToShow = BlacklistedItems.AsEnumerable();
            
            if (!string.IsNullOrWhiteSpace(_searchFilter))
            {
                var filteredIds = new List<uint>();
                foreach (var itemId in BlacklistedItems)
                {
                    string itemName = null;
                    lock (_itemsLock)
                    {
                        var itemInfo = _allItems.FirstOrDefault(i => i.ItemId == itemId);
                        itemName = itemInfo?.Name;
                    }
                    
                    if (string.IsNullOrEmpty(itemName))
                    {
                        try
                        {
                            var itemSheet = Plugin.DataManager.GetExcelSheet<Item>();
                            if (itemSheet != null)
                            {
                                var gameItem = itemSheet.GetRowOrDefault(itemId);
                                if (gameItem != null && gameItem.Value.RowId != 0)
                                {
                                    itemName = gameItem.Value.Name.ExtractText();
                                }
                            }
                        }
                        catch { }
                    }
                    
                    itemName ??= GetItemNameFromComment(itemId);
                    
                    if (itemName.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase) ||
                        itemId.ToString().Contains(_searchFilter))
                    {
                        filteredIds.Add(itemId);
                    }
                }
                itemsToShow = filteredIds;
            }
            
            DrawCustomBlacklistTable(itemsToShow);
            
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
    }
    
    private void DrawCustomBlacklistTable(IEnumerable<uint> itemIds)
    {
        using var style = ImRaii.PushStyle(ImGuiStyleVar.CellPadding, new Vector2(4, 2))
                                .Push(ImGuiStyleVar.ItemSpacing, new Vector2(4, 2));
        
        using (var table = ImRaii.Table("CustomBlacklistTable", 5, 
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable))
        {
            if (table)
            {
                float idWidth = ImGui.CalcTextSize("99999").X + 8;
                float ilvlWidth = ImGui.CalcTextSize("999").X + 8;
                float categoryWidth = ImGui.CalcTextSize("Seasonal Miscellany").X + 8;
                float actionsWidth = ImGui.CalcTextSize("Remove").X + 16;
                
                ImGui.TableSetupColumn("ID", ImGuiTableColumnFlags.WidthFixed | ImGuiTableColumnFlags.NoHide, idWidth);
                ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch | ImGuiTableColumnFlags.NoHide);
                ImGui.TableSetupColumn("iLvl", ImGuiTableColumnFlags.WidthFixed, ilvlWidth);
                ImGui.TableSetupColumn("Category", ImGuiTableColumnFlags.WidthFixed, categoryWidth);
                ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, actionsWidth);
                ImGui.TableHeadersRow();
                
                foreach (var itemId in itemIds)
                {
                    InventoryItemInfo itemInfo = null;
                    lock (_itemsLock)
                    {
                        itemInfo = _allItems.FirstOrDefault(i => i.ItemId == itemId);
                    }
                    string itemName = itemInfo?.Name;
                    string categoryName = itemInfo?.CategoryName ?? "Unknown";
                    ushort iconId = itemInfo?.IconId ?? 0;
                    int itemLevel = (int)(itemInfo?.ItemLevel ?? 0);
                    if (string.IsNullOrEmpty(itemName))
                    {
                        try
                        {
                            var itemSheet = Plugin.DataManager.GetExcelSheet<Item>();
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
                    if (string.IsNullOrEmpty(itemName))
                    {
                        itemName = GetItemNameFromComment(itemId);
                    }
                    
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.Text(itemId.ToString());
                    ImGui.TableNextColumn();
                    if (iconId > 0)
                    {
                        var icon = _iconCache.GetIcon(iconId);
                        if (icon != null)
                        {
                            var startY = ImGui.GetCursorPosY();
                            ImGui.SetCursorPosY(startY - 2);  // Lower the icon by 2 pixels
                            ImGui.Image(icon.Handle, new Vector2(20, 20));
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
                    
                    ImGui.TableNextColumn();
                    if (itemLevel > 0)
                    {
                        ImGui.Text(itemLevel.ToString());
                    }
                    else
                    {
                        ImGui.TextColored(ColorSubdued, "-");
                    }
                    
                    ImGui.TableNextColumn();
                    ImGui.Text(categoryName);
                    
                    ImGui.TableNextColumn();
                    if (ImGui.SmallButton($"Remove##bl_{itemId}"))
                    {
                        BlacklistedItems.Remove(itemId);
                        SaveBlacklist();
                        RefreshInventory();
                    }
                }
            }
        }
    }
    
    private string GetItemCategoryName(uint categoryId)
    {
        try
        {
            var categorySheet = Plugin.DataManager.GetExcelSheet<ItemUICategory>();
            if (categorySheet != null)
            {
                var category = categorySheet.GetRowOrDefault(categoryId);
                if (category != null && category.Value.RowId != 0)
                {
                    return category.Value.Name.ExtractText();
                }
            }
            return "Unknown";
        }
        catch
        {
            return "Unknown";
        }
    }
    
    private string GetItemNameFromComment(uint itemId)
    {
        return itemId switch
        {
            16039 => "Ala Mhigan Earrings",
            24589 => "Aetheryte Earrings",
            33648 => "Menphina's Earrings",
            41081 => "Azeyma's Earrings",
            21197 => "UCOB Token",
            23175 => "UWU Token",
            28633 => "TEA Token",
            36810 => "DSR Token",
            38951 => "TOP Token",
            10155 => "Ceruleum Tank",
            10373 => "Magitek Repair Materials",
            2962 => "Onion Doublet",
            3279 => "Onion Gaskins",
            3743 => "Onion Patterns",
            9387 => "Antique Helm",
            9388 => "Antique Mail",
            9389 => "Antique Gauntlets",
            9390 => "Antique Breeches",
            9391 => "Antique Sollerets",
            6223 => "Mended Imperial Pot Helm",
            6224 => "Mended Imperial Short Robe",
            7060 => "Durability Draught",
            14945 => "Squadron Enlistment Manual",
            15772 => "Contemporary Warfare: Defense",
            15773 => "Contemporary Warfare: Offense",
            15774 => "Contemporary Warfare: Magicks",
            4572 => "Company-issue Tonic",
            20790 => "High Grade Company-issue Tonic",
            _ => "Unknown Item"
        };
    }
}
