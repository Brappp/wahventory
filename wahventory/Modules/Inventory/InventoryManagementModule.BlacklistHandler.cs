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

/// <summary>
/// Blacklist management functionality for InventoryManagementModule
/// </summary>
public partial class InventoryManagementModule
{
    private string _itemNameToAdd = string.Empty;
    private uint _itemToAdd = 0;
    private List<(uint Id, string Name, ushort Icon)> _searchResults = new();
    
    private bool _searchingItems = false;
    private DateTime _lastSearchTime = DateTime.MinValue;
    private readonly TimeSpan _searchDelay = TimeSpan.FromMilliseconds(300);
    
    private void DrawBlacklistTabContent()
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
        
        ImGui.SameLine();
        if (ImGui.Button("Add by ID") && _itemToAdd > 0)
        {
            AddItemToBlacklist(_itemToAdd);
            _itemToAdd = 0;
        }
        
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100);
        int tempId = (int)_itemToAdd;
        if (ImGui.InputInt("##ItemId", ref tempId, 0, 0))
        {
            _itemToAdd = (uint)Math.Max(0, tempId);
        }
        
        // Handle item search
        if (_searchingItems && DateTime.Now - _lastSearchTime > _searchDelay)
        {
            _searchingItems = false;
            _searchResults.Clear();
            
            if (!string.IsNullOrWhiteSpace(_itemNameToAdd))
            {
                SearchItems(_itemNameToAdd);
            }
        }
        
        DrawSearchResults();
    }
    
    private void SearchItems(string searchTerm)
    {
        try
        {
            var itemSheet = Plugin.DataManager.GetExcelSheet<Item>();
            if (itemSheet == null) return;
            
            var results = itemSheet
                .Where(i => i.RowId > 0 && 
                           !string.IsNullOrEmpty(i.Name.ToString()) &&
                           i.Name.ToString().Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                .Take(20)
                .Select(i => (i.RowId, i.Name.ToString(), i.Icon))
                .ToList();
            
            _searchResults = results;
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Failed to search items");
        }
    }
    
    private void DrawSearchResults()
    {
        if (_searchResults.Count > 0)
        {
            ImGui.Text("Search Results:");
            using var child = ImRaii.Child("SearchResults", new Vector2(0, 150), true);
            
            foreach (var (id, name, iconId) in _searchResults)
            {
                using var itemId = ImRaii.PushId((int)id);
                
                var icon = _iconCache.GetIcon(iconId);
                if (icon != null)
                {
                    ImGui.Image(icon.Handle, new Vector2(20, 20));
                    ImGui.SameLine();
                }
                
                if (ImGui.Selectable($"{name} (ID: {id})###{id}_blacklist_search"))
                {
                    AddItemToBlacklist(id);
                    _itemNameToAdd = string.Empty;
                    _searchResults.Clear();
                }
            }
        }
    }
    
    private void DrawCurrentBlacklist()
    {
        ImGui.Text($"Current Blacklist ({_taskCoordinator.BlacklistedItems.Count} items):");
        ImGui.Spacing();
        
        if (!_taskCoordinator.BlacklistedItems.Any())
        {
            ImGui.TextColored(ColorSubdued, "No items in blacklist");
            return;
        }
        
        using var child = ImRaii.Child("BlacklistItems", new Vector2(0, 300), true);
        using var table = ImRaii.Table("BlacklistTable", 4, 
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable);
        
        if (table)
        {
            ImGui.TableSetupColumn("ID", ImGuiTableColumnFlags.WidthFixed, 60);
            ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Category", ImGuiTableColumnFlags.WidthFixed, 120);
            ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, 80);
            ImGui.TableHeadersRow();
            
            var itemsToRemove = new List<uint>();
            
            foreach (var itemId in _taskCoordinator.BlacklistedItems.ToList())
            {
                ImGui.TableNextRow();
                
                ImGui.TableNextColumn();
                ImGui.Text(itemId.ToString());
                
                ImGui.TableNextColumn();
                var itemInfo = GetItemInfo(itemId);
                if (itemInfo.icon > 0)
                {
                    var icon = _iconCache.GetIcon(itemInfo.icon);
                    if (icon != null)
                    {
                        ImGui.Image(icon.Handle, new Vector2(20, 20));
                        ImGui.SameLine();
                    }
                }
                ImGui.Text(itemInfo.name);
                
                ImGui.TableNextColumn();
                ImGui.Text(itemInfo.category);
                
                ImGui.TableNextColumn();
                using (var color = ImRaii.PushColor(ImGuiCol.Button, ColorError))
                {
                    if (ImGui.SmallButton($"Remove###{itemId}"))
                    {
                        itemsToRemove.Add(itemId);
                    }
                }
            }
            
            // Remove items outside the loop
            foreach (var itemId in itemsToRemove)
            {
                _taskCoordinator.BlacklistedItems.Remove(itemId);
            }
            
            if (itemsToRemove.Any())
            {
                SaveBlacklist();
                _taskCoordinator.RefreshAll(_showArmory, _searchFilter);
            }
        }
    }
    
    private void AddItemToBlacklist(uint itemId)
    {
        if (!_taskCoordinator.BlacklistedItems.Contains(itemId))
        {
            _taskCoordinator.BlacklistedItems.Add(itemId);
            SaveBlacklist();
            _taskCoordinator.RefreshAll(_showArmory, _searchFilter);
            Plugin.Log.Information($"Added item {itemId} to blacklist");
        }
    }
    
    private (string name, string category, ushort icon) GetItemInfo(uint itemId)
    {
        try
        {
            // First check current inventory
            var inventoryItem = _cachedItems.FirstOrDefault(i => i.ItemId == itemId);
            if (inventoryItem != null)
            {
                return (inventoryItem.Name, inventoryItem.CategoryName, inventoryItem.IconId);
            }
            
            // Fall back to game data
            var itemSheet = Plugin.DataManager.GetExcelSheet<Item>();
            var item = itemSheet?.GetRowOrDefault(itemId);
            if (item != null && item.Value.RowId != 0)
            {
                var name = item.Value.Name.ToString();
                var category = GetItemCategoryName(item.Value.ItemUICategory.RowId);
                return (name, category, item.Value.Icon);
            }
            
            return ($"Unknown Item ({itemId})", "Unknown", 0);
        }
        catch
        {
            return ($"Unknown Item ({itemId})", "Unknown", 0);
        }
    }
    
    private string GetItemCategoryName(uint categoryId)
    {
        // TODO: Fix Lumina API usage
        return "Item Category";
    }
}