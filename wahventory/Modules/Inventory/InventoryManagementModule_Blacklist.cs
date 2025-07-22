using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using FFXIVClientStructs.FFXIV.Client.Game;
using ImGuiNET;
using wahventory.Helpers;
using wahventory.Models;
using Lumina.Excel.Sheets;

namespace wahventory.Modules.Inventory;

public partial class InventoryManagementModule
{
    // UI state for blacklist management
    private string _blacklistSearchFilter = string.Empty;
    private uint _itemToAdd = 0;
    private string _itemNameToAdd = string.Empty;
    private List<(uint Id, string Name, ushort Icon)> _searchResults = new();
    private bool _searchingItems = false;
    private DateTime _lastSearchTime = DateTime.MinValue;
    private readonly TimeSpan _searchDebounce = TimeSpan.FromMilliseconds(300);
    
    private void DrawBlacklistTab()
    {
        // Remove the extra child wrapper to prevent sizing issues
        // Header with explanation
        ImGui.TextWrapped("Items in the blacklist will never be selected for discard. This is in addition to the built-in safety lists.");
        ImGui.Spacing();
        
        // Add item section
        DrawAddToBlacklistSection();
        
        ImGui.Separator();
        ImGui.Spacing();
        
        // Current blacklist
        DrawCurrentBlacklist();
        
        // Built-in lists (read-only)
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        
        DrawBuiltInLists();
    }
    
    private void DrawAddToBlacklistSection()
    {
        ImGui.Text("Add Item to Blacklist:");
        
        // Search for item to add
        ImGui.SetNextItemWidth(300);
        if (ImGui.InputTextWithHint("##BlacklistSearch", "Search for item to add...", ref _itemNameToAdd, 100))
        {
            _itemToAdd = 0;
            _lastSearchTime = DateTime.Now;
            _searchingItems = true;
        }
        
        // Perform search after debounce
        if (_searchingItems && DateTime.Now - _lastSearchTime > _searchDebounce)
        {
            SearchItems(_itemNameToAdd);
            _searchingItems = false;
        }
        
        ImGui.SameLine();
        
        // Add by ID
        ImGui.SetNextItemWidth(100);
        int itemIdInput = (int)_itemToAdd;
        if (ImGui.InputInt("Item ID", ref itemIdInput, 0, 0))
        {
            _itemToAdd = itemIdInput >= 0 ? (uint)itemIdInput : 0;
        }
        
        ImGui.SameLine();
        
        // Add button
        var canAdd = _itemToAdd > 0;
        if (!canAdd) ImGui.BeginDisabled();
        
        if (ImGui.Button("Add to Blacklist"))
        {
            if (_itemToAdd > 0 && !Settings.BlacklistedItems.Contains(_itemToAdd))
            {
                Settings.BlacklistedItems.Add(_itemToAdd);
                _plugin.Configuration.Save();
                RefreshInventory();
                _itemToAdd = 0;
                _itemNameToAdd = string.Empty;
                _searchResults.Clear();
            }
        }
        
        if (!canAdd) ImGui.EndDisabled();
        
        // Show search results
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
        
        // Create a child region for scrollable results
        ImGui.BeginChild("SearchResults", new Vector2(0, 200), true);
        
        foreach (var (id, name, iconId) in _searchResults)
        {
            ImGui.PushID($"SearchResult_{id}");
            
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
            var isBlacklisted = Settings.BlacklistedItems.Contains(id);
            if (isBlacklisted)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, ColorSubdued);
                ImGui.Text($"{name} (ID: {id}) [Already Blacklisted]");
                ImGui.PopStyleColor();
            }
            else
            {
                if (ImGui.Selectable($"{name} (ID: {id})"))
                {
                    _itemToAdd = id;
                    _itemNameToAdd = name;
                }
            }
            
            ImGui.PopID();
        }
        
        ImGui.EndChild();
    }
    
    private void DrawCurrentBlacklist()
    {
        // Use TreeNodeEx for consistency with other collapsible sections
        var nodeFlags = ImGuiTreeNodeFlags.AllowItemOverlap | ImGuiTreeNodeFlags.SpanAvailWidth | ImGuiTreeNodeFlags.DefaultOpen;
        
        ImGui.PushID("CustomBlacklist");
        var open = ImGui.TreeNodeEx($"Custom Blacklist ({Settings.BlacklistedItems.Count} items)##CustomBlacklistHeader", nodeFlags);
        
        // Search filter on the same line
        ImGui.SameLine();
        var windowWidth = ImGui.GetWindowContentRegionMax().X;
        ImGui.SameLine(windowWidth - 210);
        ImGui.SetNextItemWidth(200);
        ImGui.InputTextWithHint("##BlacklistFilter", "Filter blacklist...", ref _blacklistSearchFilter, 100);
        
        if (open)
        {
            ImGui.Spacing();
            
            if (!Settings.BlacklistedItems.Any())
            {
                ImGui.TextColored(ColorSubdued, "No custom blacklisted items.");
            }
            else
            {
                // Filter the items if needed
                var itemsToShow = Settings.BlacklistedItems.AsEnumerable();
                
                if (!string.IsNullOrWhiteSpace(_blacklistSearchFilter))
                {
                    var filteredIds = new List<uint>();
                    foreach (var itemId in Settings.BlacklistedItems)
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
                        
                        itemName ??= GetItemNameFromComment(itemId);
                        
                        if (itemName.Contains(_blacklistSearchFilter, StringComparison.OrdinalIgnoreCase) ||
                            itemId.ToString().Contains(_blacklistSearchFilter))
                        {
                            filteredIds.Add(itemId);
                        }
                    }
                    itemsToShow = filteredIds;
                }
                
                DrawProtectedItemsTable(itemsToShow, "CustomBlacklist", true);
            }
            
            ImGui.TreePop();
        }
        
        ImGui.PopID();
        
        // Add small spacing between categories
        ImGui.Spacing();
    }
    
    private void DrawBuiltInLists()
    {
        ImGui.Text("Built-in Protected Lists (Read-Only):");
        ImGui.Spacing();
        
        // Ultimate tokens and special items
        var ultimateNodeFlags = ImGuiTreeNodeFlags.AllowItemOverlap | ImGuiTreeNodeFlags.SpanAvailWidth;
        ImGui.PushID("UltimateTokens");
        if (ImGui.TreeNodeEx($"Ultimate Tokens & Special Items ({InventoryHelpers.HardcodedBlacklist.Count} items)##UltimateTokensHeader", ultimateNodeFlags))
        {
            DrawProtectedItemsTable(InventoryHelpers.HardcodedBlacklist, "HardcodedList", false);
            ImGui.TreePop();
        }
        ImGui.PopID();
        
        ImGui.Spacing();
        
        // Currency items
        var currencyNodeFlags = ImGuiTreeNodeFlags.AllowItemOverlap | ImGuiTreeNodeFlags.SpanAvailWidth;
        ImGui.PushID("CurrencyItems");
        if (ImGui.TreeNodeEx($"Currency Items (IDs 1-99)##CurrencyItemsHeader", currencyNodeFlags))
        {
            ImGui.TextWrapped("All items with IDs from 1 to 99 are protected as currency items.");
            
            // Show a sample of currency items in table format
            var currencyItems = new HashSet<uint>();
            for (uint i = 1; i <= 99; i++)
            {
                currencyItems.Add(i);
            }
            DrawProtectedItemsTable(currencyItems, "CurrencyList", false);
            ImGui.TreePop();
        }
        ImGui.PopID();
        
        ImGui.Spacing();
    }
    
    private void DrawProtectedItemsTable(IEnumerable<uint> itemIds, string tableId, bool showRemoveButton)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(4, 2)); // Compact padding
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(4, 2));
        
        // Remove ScrollY to prevent table stretching
        if (ImGui.BeginTable($"ProtectedTable_{tableId}", showRemoveButton ? 5 : 4, 
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable))
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
            if (showRemoveButton)
            {
                ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, actionsWidth);
            }
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
                    itemName = GetItemNameFromComment(itemId);
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
                        ImGui.SetCursorPosY(startY - 2);  // Lower the icon by 2 pixels
                        ImGui.Image(icon.ImGuiHandle, new Vector2(20, 20));
                        ImGui.SetCursorPosY(startY);
                        ImGui.SameLine(0, 5);
                    }
                    else
                    {
                        // Reserve space for missing icon
                        ImGui.Dummy(new Vector2(20, 20));
                        ImGui.SameLine(0, 5);
                    }
                }
                else
                {
                    // Reserve space for missing icon
                    ImGui.Dummy(new Vector2(20, 20));
                    ImGui.SameLine(0, 5);
                }
                ImGui.Text(itemName);
                
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
                
                // Remove button (only for custom blacklist)
                if (showRemoveButton)
                {
                    ImGui.TableNextColumn();
                    if (ImGui.SmallButton($"Remove##bl_{itemId}"))
                    {
                        Settings.BlacklistedItems.Remove(itemId);
                        _plugin.Configuration.Save();
                        RefreshInventory();
                    }
                }
            }
            
            ImGui.EndTable();
        }
        
        ImGui.PopStyleVar(2); // Pop padding and spacing
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
        // Helper to get item names from the comments in InventoryHelpers.cs
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
