using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;
using Lumina.Excel.Sheets;
using wahventory.Helpers;
using wahventory.Models;

namespace wahventory.Modules.Inventory;

public partial class InventoryManagementModule
{
    // Search functionality for auto-discard
    private string _autoDiscardItemNameToAdd = string.Empty;
    private uint _autoDiscardItemToAdd = 0;
    private List<(uint Id, string Name, ushort Icon)> _autoDiscardSearchResults = new();
    
    private bool _autoDiscardSearchingItems = false;
    private DateTime _autoDiscardLastSearchTime = DateTime.MinValue;
    private readonly TimeSpan _autoDiscardSearchDelay = TimeSpan.FromMilliseconds(300);
    
    private void DrawAutoDiscardTab()
    {
        ImGui.TextWrapped("Manage your auto-discard list. Items added here will be automatically discarded when using the /wahventory auto command.");
        ImGui.TextWrapped("WARNING: This is a powerful feature. Only add items you are absolutely certain you want to discard automatically!");
        ImGui.Spacing();
        
        DrawAddToAutoDiscardSection();
        
        ImGui.Separator();
        ImGui.Spacing();
        
        DrawCurrentAutoDiscardList();
    }
    
    private void DrawAddToAutoDiscardSection()
    {
        ImGui.Text("Add New Item to Auto-Discard:");
        ImGui.Spacing();
        
        // Item search input
        ImGui.SetNextItemWidth(300);
        if (ImGui.InputTextWithHint("##AddAutoDiscardItemName", "Search item name...", ref _autoDiscardItemNameToAdd, 100))
        {
            _autoDiscardLastSearchTime = DateTime.Now;
            _autoDiscardSearchingItems = true;
        }
        
        if (_autoDiscardSearchingItems && DateTime.Now - _autoDiscardLastSearchTime > _autoDiscardSearchDelay)
        {
            SearchAutoDiscardItems(_autoDiscardItemNameToAdd);
            _autoDiscardSearchingItems = false;
        }
        
        ImGui.SameLine();
        
        // Add by ID
        ImGui.Text("or ID:");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100);
        int itemId = (int)_autoDiscardItemToAdd;
        if (ImGui.InputInt("##AddAutoDiscardItemId", ref itemId, 0, 0))
        {
            _autoDiscardItemToAdd = (uint)Math.Max(0, itemId);
        }
        
        ImGui.SameLine();
        
        // Add button
        var canAdd = _autoDiscardItemToAdd > 0;
        
        using (var disabled = ImRaii.Disabled(!canAdd))
        {
            if (ImGui.Button("Add to Auto-Discard"))
            {
                if (_autoDiscardItemToAdd > 0 && !Settings.AutoDiscardItems.Contains(_autoDiscardItemToAdd))
                {
                    Settings.AutoDiscardItems.Add(_autoDiscardItemToAdd);
                    _plugin.Configuration.Save();
                    RefreshInventory();
                    _autoDiscardItemToAdd = 0;
                    _autoDiscardItemNameToAdd = string.Empty;
                    _autoDiscardSearchResults.Clear();
                }
            }
        }
        
        DrawAutoDiscardSearchResults();
    }
    
    private void SearchAutoDiscardItems(string searchTerm)
    {
        _autoDiscardSearchResults.Clear();
        
        if (string.IsNullOrWhiteSpace(searchTerm) || searchTerm.Length < 2)
            return;
            
        try
        {
            var itemSheet = Plugin.DataManager.GetExcelSheet<Item>();
            if (itemSheet == null) return;
            
            var lowerSearch = searchTerm.ToLower();
            
            _autoDiscardSearchResults = itemSheet
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
    
    private void DrawAutoDiscardSearchResults()
    {
        if (!_autoDiscardSearchResults.Any())
            return;
            
        ImGui.Spacing();
        ImGui.Text($"Search Results ({_autoDiscardSearchResults.Count} items):");
        
        // Create a child region for scrollable results
        using (var child = ImRaii.Child("AutoDiscardSearchResults", new Vector2(0, 200), true))
        {
            foreach (var (id, name, iconId) in _autoDiscardSearchResults)
            {
                using var pushId = ImRaii.PushId($"AutoDiscardSearchResult_{id}");
                
                // Draw item with icon
                if (iconId > 0)
                {
                    var icon = _iconCache.GetIcon(iconId);
                    if (icon != null)
                    {
                        ImGui.Image(icon.ImGuiHandle, new Vector2(20, 20));
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
                
                // Item name and ID
                var isAutoDiscard = Settings.AutoDiscardItems.Contains(id);
                if (isAutoDiscard)
                {
                    using (var color = ImRaii.PushColor(ImGuiCol.Text, ColorSubdued))
                    {
                        ImGui.Text($"{name} (ID: {id}) [Already in Auto-Discard]");
                    }
                }
                else
                {
                    if (ImGui.Selectable($"{name} (ID: {id})"))
                    {
                        _autoDiscardItemToAdd = id;
                        _autoDiscardItemNameToAdd = name;
                    }
                }
            }
        }
    }
    
    private void DrawCurrentAutoDiscardList()
    {
        ImGui.Text($"Your Auto-Discard List ({Settings.AutoDiscardItems.Count} items)");
        
        ImGui.Spacing();
        
        if (!Settings.AutoDiscardItems.Any())
        {
            ImGui.TextColored(ColorSubdued, "No auto-discard items configured.");
            ImGui.TextColored(ColorInfo, "Add items using the controls above. Be careful - these items will be automatically discarded!");
        }
        else
        {
            // Filter the items using the main search filter
            var itemsToShow = Settings.AutoDiscardItems.AsEnumerable();
            
            if (!string.IsNullOrWhiteSpace(_searchFilter))
            {
                var filteredIds = new List<uint>();
                foreach (var itemId in Settings.AutoDiscardItems)
                {
                    // Get item info for filtering
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
                    
                    itemName ??= GetItemNameFromAutoDiscardComment(itemId);
                    
                    if (itemName.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase) ||
                        itemId.ToString().Contains(_searchFilter))
                    {
                        filteredIds.Add(itemId);
                    }
                }
                itemsToShow = filteredIds;
            }
            
            DrawAutoDiscardItemsTable(itemsToShow);
            
            ImGui.Spacing();
            
            // Bulk actions
            if (ImGui.Button("Clear All Auto-Discard Items"))
            {
                ImGui.OpenPopup("ClearAutoDiscardConfirm");
            }
            
            using (var popup = ImRaii.PopupModal("ClearAutoDiscardConfirm"))
            {
                if (popup)
                {
                    ImGui.Text("Are you sure you want to clear all auto-discard items?");
                    ImGui.Text($"This will remove {Settings.AutoDiscardItems.Count} items from your auto-discard list.");
                    ImGui.Spacing();
                    
                    if (ImGui.Button("Yes, Clear All", new Vector2(120, 0)))
                    {
                        Settings.AutoDiscardItems.Clear();
                        _plugin.Configuration.Save();
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
    
    private void DrawAutoDiscardItemsTable(IEnumerable<uint> itemIds)
    {
        using var style = ImRaii.PushStyle(ImGuiStyleVar.CellPadding, new Vector2(4, 2))
                                .Push(ImGuiStyleVar.ItemSpacing, new Vector2(4, 2));
        
        using (var table = ImRaii.Table("AutoDiscardTable", 5, 
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable))
        {
            if (table)
            {
                // Use dynamic widths based on content
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
                    
                    // Fallback to hardcoded names for known items
                    if (string.IsNullOrEmpty(itemName))
                    {
                        itemName = GetItemNameFromAutoDiscardComment(itemId);
                    }
                    
                    ImGui.TableNextRow();
                    
                    // ID column
                    ImGui.TableNextColumn();
                    ImGui.Text(itemId.ToString());
                    
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
                    
                    // Highlight if currently in inventory
                    if (itemInfo != null)
                    {
                        ImGui.SameLine();
                        ImGui.TextColored(ColorWarning, "[In Inventory]");
                    }
                    
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
                    
                    // Remove button
                    ImGui.TableNextColumn();
                    if (ImGui.SmallButton($"Remove##ad_{itemId}"))
                    {
                        Settings.AutoDiscardItems.Remove(itemId);
                        _plugin.Configuration.Save();
                        RefreshInventory();
                    }
                }
            }
        }
    }
    
    private string GetItemNameFromAutoDiscardComment(uint itemId)
    {
        // Return a placeholder name for unknown items
        return $"Unknown Item ({itemId})";
    }
    
    // Public method to execute auto-discard
    public void ExecuteAutoDiscard()
    {
        if (Settings.AutoDiscardItems.Count == 0)
        {
            Plugin.ChatGui.PrintError("No items configured for auto-discard. Add items in the Auto Discard tab.");
            return;
        }
        
        // Find all items in inventory that match auto-discard list
        List<InventoryItemInfo> itemsToDiscard;
        lock (_itemsLock)
        {
            itemsToDiscard = _allItems
                .Where(item => Settings.AutoDiscardItems.Contains(item.ItemId) && 
                              item.CanBeDiscarded &&
                              !Settings.BlacklistedItems.Contains(item.ItemId))
                .ToList();
        }
        
        if (!itemsToDiscard.Any())
        {
            Plugin.ChatGui.PrintError("No auto-discard items found in inventory.");
            return;
        }
        
        // Prepare for discard
        _itemsToDiscard = itemsToDiscard;
        _discardProgress = 0;
        _discardError = null;
        
        // Skip confirmation popup and start discarding immediately
        Plugin.ChatGui.Print($"Auto-discarding {itemsToDiscard.Count} item(s)...");
        StartDiscarding();
    }
} 