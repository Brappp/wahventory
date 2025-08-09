using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Bindings.ImGui;
using FFXIVClientStructs.FFXIV.Client.Game;
using wahventory.Models;
using wahventory.Services.Helpers;
using wahventory.Core;

namespace wahventory.Modules.Inventory;

public partial class InventoryManagementModule
{
    // Global search variables
    private string _globalSearchQuery = string.Empty;
    private bool _showGlobalSearchResults = false;
    private List<GlobalSearchResult> _globalSearchResults = new();
    private DateTime _lastGlobalSearch = DateTime.MinValue;
    
    // Context menu variables
    private InventoryItemInfo? _contextMenuItem = null;
    private string _contextMenuLocation = string.Empty;
    
    public class GlobalSearchResult
    {
        public InventoryItemInfo Item { get; set; }
        public string Location { get; set; } // "Available Items", "Protected", "Tracker: CharacterName", etc.
        public string Container { get; set; } // "Inventory", "Armory", "Retainer: Name", etc.
        public bool IsSelected { get; set; }
        public bool IsBlacklisted { get; set; }
        public bool IsInGearset { get; set; }
        public bool IsProtected { get; set; }
    }
    
    /// <summary>
    /// Perform a global search across all inventories and tracked items
    /// </summary>
    private void PerformGlobalSearch()
    {
        _globalSearchResults.Clear();
        var query = _globalSearchQuery.ToLower();
        
        // Search in available items
        lock (_itemsLock)
        {
            foreach (var item in _allItems)
            {
                if (item.Name.ToLower().Contains(query))
                {
                    _globalSearchResults.Add(new GlobalSearchResult
                    {
                        Item = item,
                        Location = "Available Items",
                        Container = GetLocationName(item.Container),
                        IsSelected = _selectedItems.Contains(item.ItemId),
                        IsBlacklisted = BlacklistedItems.Contains(item.ItemId),
                        IsInGearset = InventoryHelpers.IsInGearset(item.ItemId),
                        IsProtected = false
                    });
                }
            }
        }
        
        // Search in protected items
        var protectedItems = GetProtectedItems();
        foreach (var item in protectedItems)
        {
            if (item.Name.ToLower().Contains(query))
            {
                _globalSearchResults.Add(new GlobalSearchResult
                {
                    Item = item,
                    Location = "Protected Items",
                    Container = GetLocationName(item.Container),
                    IsSelected = false,
                    IsBlacklisted = BlacklistedItems.Contains(item.ItemId),
                    IsInGearset = InventoryHelpers.IsInGearset(item.ItemId),
                    IsProtected = true
                });
            }
        }
        
        // Search in tracked inventories
        if (_trackerService != null && _trackerService.HasData)
        {
            var trackerResults = _trackerService.SearchItems(_globalSearchQuery);
            foreach (var trackedItem in trackerResults)
            {
                // Convert TrackedItem to InventoryItemInfo for consistency
                var item = new InventoryItemInfo
                {
                    ItemId = trackedItem.ItemId,
                    Name = trackedItem.ItemName,
                    Quantity = (int)trackedItem.Quantity,
                    IsHQ = trackedItem.IsHQ,
                    Container = InventoryType.Inventory1, // Default container, we'll use the string Container field
                    Slot = (short)trackedItem.Slot
                };
                
                // Get owner name from the container string (format: "OwnerName: Container")
                var ownerName = trackedItem.Container.Contains(":")
                    ? trackedItem.Container.Split(':')[0].Trim()
                    : "Unknown";
                
                _globalSearchResults.Add(new GlobalSearchResult
                {
                    Item = item,
                    Location = $"Tracker: {ownerName}",
                    Container = trackedItem.Container,
                    IsSelected = false,
                    IsBlacklisted = BlacklistedItems.Contains(item.ItemId),
                    IsInGearset = InventoryHelpers.IsInGearset(item.ItemId),
                    IsProtected = false
                });
            }
        }
        
        _lastGlobalSearch = DateTime.Now;
    }
    
    /// <summary>
    /// Draw the global search results window
    /// </summary>
    private void DrawGlobalSearchResults()
    {
        if (!_showGlobalSearchResults || !_globalSearchResults.Any())
            return;
        
        var windowSize = new Vector2(800, 400);
        ImGui.SetNextWindowSize(windowSize, ImGuiCond.FirstUseEver);
        
        if (ImGui.Begin($"Search Results: \"{_globalSearchQuery}\"###GlobalSearchResults", ref _showGlobalSearchResults))
        {
            // Summary
            ImGui.Text($"Found {_globalSearchResults.Count} items matching \"{_globalSearchQuery}\"");
            
            // Group by location
            var groupedResults = _globalSearchResults
                .GroupBy(r => r.Location)
                .OrderBy(g => g.Key);
            
            ImGui.Text("Results by location:");
            foreach (var group in groupedResults)
            {
                ImGui.SameLine();
                ImGui.TextColored(ColorInfo, $"{group.Key}: {group.Count()}");
            }
            
            ImGui.Separator();
            
            // Results table
            using (var table = ImRaii.Table("GlobalSearchResultsTable", 7,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY))
            {
                if (table)
                {
                    ImGui.TableSetupColumn("Location", ImGuiTableColumnFlags.WidthFixed, 120);
                    ImGui.TableSetupColumn("Container", ImGuiTableColumnFlags.WidthFixed, 100);
                    ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableSetupColumn("Qty", ImGuiTableColumnFlags.WidthFixed, 50);
                    ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, 100);
                    ImGui.TableSetupColumn("Price", ImGuiTableColumnFlags.WidthFixed, 80);
                    ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, 60);
                    ImGui.TableSetupScrollFreeze(0, 1);
                    ImGui.TableHeadersRow();
                    
                    foreach (var result in _globalSearchResults)
                    {
                        ImGui.TableNextRow();
                        
                        // Location
                        ImGui.TableNextColumn();
                        ImGui.TextColored(ColorInfo, result.Location);
                        
                        // Container
                        ImGui.TableNextColumn();
                        ImGui.Text(result.Container);
                        
                        // Item name with icon
                        ImGui.TableNextColumn();
                        DrawItemNameWithIcon(result.Item);
                        
                        // Show right-click context menu
                        if (ImGui.IsItemClicked(ImGuiMouseButton.Right))
                        {
                            _contextMenuItem = result.Item;
                            _contextMenuLocation = result.Location;
                            ImGui.OpenPopup($"ItemContext_{result.Item.ItemId}");
                        }
                        DrawItemContextMenu(result.Item, result.Location);
                        
                        // Quantity
                        ImGui.TableNextColumn();
                        ImGui.Text(result.Item.Quantity.ToString());
                        
                        // Status
                        ImGui.TableNextColumn();
                        DrawItemStatusTags(result);
                        
                        // Price
                        ImGui.TableNextColumn();
                        DrawItemPriceCompact(result.Item);
                        
                        // Actions
                        ImGui.TableNextColumn();
                        if (ImGui.SmallButton($"Go###{result.Item.GetUniqueKey()}"))
                        {
                            NavigateToItem(result);
                        }
                        if (ImGui.IsItemHovered())
                        {
                            ImGui.SetTooltip($"Navigate to {result.Location}");
                        }
                    }
                }
            }
            
            ImGui.End();
        }
    }
    
    /// <summary>
    /// Draw item name with icon
    /// </summary>
    private void DrawItemNameWithIcon(InventoryItemInfo item)
    {
        // Try to get icon
        if (item.IconId > 0)
        {
            var icon = _iconCache.GetIcon(item.IconId);
            if (icon != null)
            {
                ImGui.Image(icon.Handle, new Vector2(20, 20));
                ImGui.SameLine();
            }
        }
        
        // Item name
        if (item.IsHQ)
        {
            ImGui.TextColored(ColorHQName, item.Name);
            ImGui.SameLine(0, 0);
            ImGui.TextColored(ColorHQItem, " " + FontAwesomeIcon.Certificate.ToIconString());
        }
        else
        {
            ImGui.Text(item.Name);
        }
    }
    
    /// <summary>
    /// Draw status tags for search result
    /// </summary>
    private void DrawItemStatusTags(GlobalSearchResult result)
    {
        bool hasTag = false;
        
        if (result.IsInGearset)
        {
            ImGui.TextColored(new Vector4(0.7f, 0.5f, 0.9f, 1f), "[Gear]");
            hasTag = true;
        }
        
        if (result.IsBlacklisted)
        {
            if (hasTag) ImGui.SameLine(0, 2);
            ImGui.TextColored(ColorError, "[BL]");
            hasTag = true;
        }
        
        if (result.IsProtected)
        {
            if (hasTag) ImGui.SameLine(0, 2);
            ImGui.TextColored(ColorWarning, "[Prot]");
            hasTag = true;
        }
        
        if (result.IsSelected)
        {
            if (hasTag) ImGui.SameLine(0, 2);
            ImGui.TextColored(ColorSuccess, "[Sel]");
            hasTag = true;
        }
        
        if (!hasTag)
        {
            ImGui.TextColored(ColorSubdued, "-");
        }
    }
    
    /// <summary>
    /// Draw compact price display
    /// </summary>
    private void DrawItemPriceCompact(InventoryItemInfo item)
    {
        if (!Settings.ShowMarketPrices)
        {
            ImGui.TextColored(ColorSubdued, "-");
            return;
        }
        
        lock (_priceCacheLock)
        {
            if (_priceCache.TryGetValue(item.ItemId, out var cacheEntry))
            {
                if (DateTime.Now - cacheEntry.fetchTime < TimeSpan.FromMinutes(5))
                {
                    if (cacheEntry.price > 0)
                    {
                        ImGui.TextColored(ColorPrice, $"{cacheEntry.price:N0}g");
                    }
                    else
                    {
                        ImGui.TextColored(ColorSubdued, "No data");
                    }
                    return;
                }
            }
        }
        
        ImGui.TextColored(ColorSubdued, "---");
    }
    
    /// <summary>
    /// Navigate to the item in its respective tab/view
    /// </summary>
    private void NavigateToItem(GlobalSearchResult result)
    {
        _showGlobalSearchResults = false;
        
        if (result.Location == "Available Items")
        {
            // Switch to Available Items tab
            // This would need to be implemented based on your tab system
            // For now, just select the item
            lock (_selectedItemsLock)
            {
                _selectedItems.Clear();
                _selectedItems.Add(result.Item.ItemId);
            }
            
            // Set search filter to highlight the item
            _searchFilter = result.Item.Name;
        }
        else if (result.Location == "Protected Items")
        {
            // Switch to Protected Items tab
            // Implementation would go here
        }
        else if (result.Location.StartsWith("Tracker:"))
        {
            // Switch to Item Tracker tab and select the inventory
            // Implementation would go here
            _trackerSearchQuery = result.Item.Name;
        }
    }
    
    /// <summary>
    /// Draw the context menu for an item
    /// </summary>
    private void DrawItemContextMenu(InventoryItemInfo item, string location = "")
    {
        if (ImGui.BeginPopupContextItem($"ItemContext_{item.ItemId}"))
        {
            ImGui.Text(item.Name);
            ImGui.Separator();
            
            // Market price options
            if (Settings.ShowMarketPrices)
            {
                if (ImGui.MenuItem("Check Market Price", ""))
                {
                    _ = FetchMarketPrice(item);
                }
                
                if (ImGui.MenuItem("Open on Universalis", ""))
                {
                    var url = $"https://universalis.app/market/{item.ItemId}";
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = url,
                        UseShellExecute = true
                    });
                }
                
                ImGui.Separator();
            }
            
            // Item management options
            bool isBlacklisted = BlacklistedItems.Contains(item.ItemId);
            bool isAutoDiscard = AutoDiscardItems.Contains(item.ItemId);
            bool isInGearset = InventoryHelpers.IsInGearset(item.ItemId);
            
            if (!isBlacklisted)
            {
                if (ImGui.MenuItem("Add to Blacklist", ""))
                {
                    BlacklistedItems.Add(item.ItemId);
                    SaveBlacklist();
                    RefreshInventory();
                }
            }
            else
            {
                if (ImGui.MenuItem("Remove from Blacklist", ""))
                {
                    BlacklistedItems.Remove(item.ItemId);
                    SaveBlacklist();
                    RefreshInventory();
                }
            }
            
            if (!isAutoDiscard)
            {
                if (ImGui.MenuItem("Add to Auto-Discard", ""))
                {
                    AutoDiscardItems.Add(item.ItemId);
                    SaveAutoDiscard();
                    RefreshInventory();
                }
            }
            else
            {
                if (ImGui.MenuItem("Remove from Auto-Discard", ""))
                {
                    AutoDiscardItems.Remove(item.ItemId);
                    SaveAutoDiscard();
                    RefreshInventory();
                }
            }
            
            ImGui.Separator();
            
            // Search options
            if (ImGui.MenuItem("Find in All Inventories", ""))
            {
                _globalSearchQuery = item.Name;
                PerformGlobalSearch();
                _showGlobalSearchResults = true;
            }
            
            if (_trackerService != null && ImGui.MenuItem("Find in Tracker", ""))
            {
                _trackerSearchQuery = item.Name;
                // Switch to tracker tab - implementation needed
            }
            
            ImGui.Separator();
            
            // Status indicators
            if (isInGearset)
            {
                ImGui.MenuItem("In Gear Set", "", false, false);
            }
            
            if (item.IsHQ)
            {
                ImGui.MenuItem("High Quality", "", false, false);
            }
            
            if (!item.CanBeTraded)
            {
                ImGui.MenuItem("Untradeable", "", false, false);
            }
            
            ImGui.Separator();
            
            // Utility options
            if (ImGui.MenuItem("Copy Item Name", ""))
            {
                ImGui.SetClipboardText(item.Name);
            }
            
            if (ImGui.MenuItem("Copy Item ID", ""))
            {
                ImGui.SetClipboardText(item.ItemId.ToString());
            }
            
            // Show item details on hover
            if (ImGui.IsItemHovered())
            {
                ImGui.BeginTooltip();
                ImGui.Text($"Item ID: {item.ItemId}");
                ImGui.Text($"Category: {item.CategoryName}");
                ImGui.Text($"Item Level: {item.ItemLevel}");
                ImGui.Text($"Location: {location}");
                ImGui.EndTooltip();
            }
            
            ImGui.EndPopup();
        }
    }
}
