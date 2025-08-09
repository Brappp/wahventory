using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Bindings.ImGui;
using wahventory.Models;
using wahventory.Services;
using wahventory.Services.External;
using wahventory.Services.Helpers;
using wahventory.Core;

namespace wahventory.Modules.Inventory;

public partial class InventoryManagementModule
{
    private AutoInventoryTrackerService? _trackerService;
    private string _trackerSearchQuery = "";
    private List<TrackedItem> _trackerSearchResults = new();
    private TrackedInventory? _selectedInventory;
    private bool _autoTrackingEnabled = true;
    private DateTime _lastTrackerPriceFetch = DateTime.MinValue;
    private readonly TimeSpan _trackerPriceFetchDelay = TimeSpan.FromMilliseconds(500);
    
    // Initialize tracker in constructor
    private void InitializeTracker()
    {
        _trackerService = new AutoInventoryTrackerService(_plugin);
        _trackerService.OnInventoriesUpdated += () => {
            // Refresh search if active
            if (!string.IsNullOrWhiteSpace(_trackerSearchQuery))
            {
                _trackerSearchResults = _trackerService.SearchItems(_trackerSearchQuery);
            }
        };
    }
    
    private void DrawItemTrackerTab()
    {
        if (_trackerService == null)
        {
            InitializeTracker();
            return;
        }
        
        // Top controls
        DrawTrackerControls();
        
        ImGui.Separator();
        
        // Main content area with two panels
        var contentWidth = ImGui.GetContentRegionAvail().X;
        var leftPanelWidth = 250f * ImGuiHelpers.GlobalScale;
        var rightPanelWidth = contentWidth - leftPanelWidth - ImGui.GetStyle().ItemSpacing.X;
        
        // Left panel - Tracked inventories list
        using (var leftChild = ImRaii.Child("TrackerLeft", new Vector2(leftPanelWidth, -1), true))
        {
            DrawTrackedInventories();
        }
        
        ImGui.SameLine();
        
        // Right panel - Search or inventory details
        using (var rightChild = ImRaii.Child("TrackerRight", new Vector2(rightPanelWidth, -1), true))
        {
            if (_selectedInventory != null)
            {
                DrawInventoryDetails(_selectedInventory);
            }
            else
            {
                DrawTrackerSearch();
            }
        }
    }
    
    private void DrawTrackerControls()
    {
        // Auto-tracking toggle
        ImGui.Text("Automatic Tracking:");
        ImGui.SameLine();
        
        if (_autoTrackingEnabled)
        {
            ImGui.TextColored(ColorSuccess, "Enabled");
            ImGui.SameLine();
            ImGui.TextColored(ColorSubdued, "(Scans automatically when you open inventories)");
        }
        else
        {
            ImGui.TextColored(ColorWarning, "Manual Mode");
            ImGui.SameLine();
            
            // Manual scan buttons
            if (ImGui.Button("Scan Current Character"))
            {
                _trackerService?.ScanCurrentCharacter();
            }
            
            ImGui.SameLine();
            if (ImGui.Button("Scan Retainer"))
            {
                _trackerService?.ScanRetainer();
            }
        }
    }
    
    private void DrawTrackedInventories()
    {
        ImGui.Text("Tracked Inventories");
        ImGui.Separator();
        
        if (_trackerService == null || !_trackerService.HasData)
        {
            ImGui.TextWrapped("No inventories tracked yet. Scan your character or retainers to start tracking items.");
            return;
        }
        
        var inventories = _trackerService.Data.Inventories.Values
            .OrderBy(i => i.Type)
            .ThenBy(i => i.OwnerName)
            .ToList();
        
        foreach (var inventory in inventories)
        {
            var isSelected = _selectedInventory?.OwnerId == inventory.OwnerId;
            
            // Clean indented list without prefix symbols
            ImGui.Indent(10f);
            
            if (ImGui.Selectable($"{inventory.DisplayName}###{inventory.OwnerId}", isSelected, ImGuiSelectableFlags.SpanAllColumns))
            {
                _selectedInventory = isSelected ? null : inventory;
            }
            
            // Show last updated time
            ImGui.SameLine();
            ImGui.SetCursorPosX(ImGui.GetContentRegionMax().X - ImGui.CalcTextSize(inventory.LastUpdatedText).X);
            ImGui.TextColored(ColorSubdued, inventory.LastUpdatedText);
            
            ImGui.Unindent(10f);
            
            // Item count under name
            ImGui.Indent(15f);
            ImGui.TextColored(ColorInfo, $"{inventory.Items.Count} items");
            ImGui.Unindent(15f);
        }
        
        ImGui.Separator();
        
        // Clear data button at bottom
        if (ImGui.Button("Clear All Data"))
        {
            ImGui.OpenPopup("ClearTrackerData");
        }
        
        if (ImGui.BeginPopupModal("ClearTrackerData"))
        {
            ImGui.Text("Are you sure you want to clear all tracked inventory data?");
            ImGui.Text("This cannot be undone.");
            
            if (ImGui.Button("Clear", new Vector2(100, 0)))
            {
                _trackerService?.ClearData();
                _selectedInventory = null;
                ImGui.CloseCurrentPopup();
            }
            
            ImGui.SameLine();
            if (ImGui.Button("Cancel", new Vector2(100, 0)))
            {
                ImGui.CloseCurrentPopup();
            }
            
            ImGui.EndPopup();
        }
    }
    
    private void DrawInventoryDetails(TrackedInventory inventory)
    {
        ImGui.Text($"Inventory: {inventory.DisplayName}");
        ImGui.TextColored(ColorSubdued, $"Last updated: {inventory.LastUpdatedText}");
        ImGui.Separator();
        
        // Group items by container
        var containerGroups = inventory.Items
            .GroupBy(i => i.Container)
            .OrderBy(g => g.Key)
            .ToList();
        
        foreach (var group in containerGroups)
        {
            if (ImGui.CollapsingHeader($"{group.Key} ({group.Count()} items)"))
            {
                // Group by item within container
                var itemGroups = group
                    .GroupBy(i => new { i.ItemId, i.IsHQ })
                    .Select(g => new
                    {
                        ItemId = g.Key.ItemId,
                        ItemName = g.First().ItemName,
                        IsHQ = g.Key.IsHQ,
                        TotalQuantity = g.Sum(x => (int)x.Quantity),
                        Stacks = g.Count(),
                        Items = g.ToList()
                    })
                    .OrderBy(i => i.ItemName)
                    .ToList();
                
                // Determine number of columns based on settings
                int columnCount = Settings.ShowMarketPrices ? 5 : 4;
                
                using (var table = ImRaii.Table($"##{group.Key}Items", columnCount))
                {
                    if (table)
                    {
                        ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
                        ImGui.TableSetupColumn("Quantity", ImGuiTableColumnFlags.WidthFixed, 80);
                        ImGui.TableSetupColumn("Details", ImGuiTableColumnFlags.WidthFixed, 100);
                        ImGui.TableSetupColumn("Sort By", ImGuiTableColumnFlags.WidthFixed, 120);
                        if (Settings.ShowMarketPrices)
                        {
                            ImGui.TableSetupColumn("Price", ImGuiTableColumnFlags.WidthFixed, 100);
                        }
                        ImGui.TableHeadersRow();
                        
                        foreach (var item in itemGroups)
                        {
                            ImGui.TableNextRow();
                            
                            ImGui.TableNextColumn();
                            
                            // Draw item icon
                            var itemSheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>();
                            if (itemSheet != null)
                            {
                                var itemData = itemSheet.GetRow(item.ItemId);
                                if (itemData.RowId != 0 && itemData.Icon > 0)
                                {
                                    var icon = Plugin.TextureProvider.GetFromGameIcon(new Dalamud.Interface.Textures.GameIconLookup(itemData.Icon));
                                    if (icon != null)
                                    {
                                        var wrap = icon.GetWrapOrEmpty();
                                        ImGui.Image(wrap.Handle, new Vector2(20, 20));
                                        ImGui.SameLine();
                                    }
                                }
                            }
                            
                            var cleanItemName = item.ItemName.TrimEnd('=', ' ');
                            
                            // Check if item is in a gear set
                            bool isInGearset = InventoryHelpers.IsInGearset(item.ItemId);
                            
                            if (item.IsHQ)
                            {
                                ImGui.TextColored(ColorHQName, $"{cleanItemName} "); 
                                ImGui.SameLine(0, 0);
                                ImGui.TextColored(ColorHQItem, FontAwesomeIcon.Certificate.ToIconString());
                            }
                            else
                            {
                                ImGui.Text(cleanItemName);
                            }
                            
                            // Add gear set tag if applicable
                            if (isInGearset)
                            {
                                ImGui.SameLine();
                                ImGui.TextColored(new Vector4(0.7f, 0.5f, 0.9f, 1f), "[Gear Set]");
                            }
                            
                            // Show market indicator if on market
                            if (group.Key == "Market")
                            {
                                ImGui.SameLine();
                                ImGui.TextColored(ColorPrice, " [ON MARKET]");
                            }
                            
                            ImGui.TableNextColumn();
                            ImGui.Text($"{item.TotalQuantity:N0}");
                            
                            ImGui.TableNextColumn();
                            if (item.Stacks > 1)
                            {
                                ImGui.TextColored(ColorInfo, $"{item.Stacks} stacks");
                            }
                            
                            // Sort By column - show relevant status
                            ImGui.TableNextColumn();
                            if (isInGearset)
                            {
                                ImGui.TextColored(new Vector4(0.7f, 0.5f, 0.9f, 1f), "Gear Set");
                            }
                            else if (item.IsHQ)
                            {
                                ImGui.TextColored(ColorHQItem, "HQ");
                            }
                            else
                            {
                                ImGui.TextColored(ColorSubdued, "-");
                            }
                            
                            // Price column if enabled
                            if (Settings.ShowMarketPrices)
                            {
                                ImGui.TableNextColumn();
                                
                                // Check if item is tradeable
                                var itemDataSheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>();
                                if (itemDataSheet != null)
                                {
                                    var itemData = itemDataSheet.GetRow(item.ItemId);
                                    if (itemData.RowId != 0 && !itemData.IsUntradable)
                                    {
                                        // Check if this row is visible (for auto-fetching)
                                        var rowY = ImGui.GetCursorScreenPos().Y;
                                        var windowY = ImGui.GetWindowPos().Y;
                                        var windowHeight = ImGui.GetWindowHeight();
                                        bool isVisible = rowY >= windowY && rowY <= windowY + windowHeight;
                                        
                                        // Use the shared price cache from main inventory module
                                        bool hasCachedPrice = false;
                                        long cachedPrice = 0;
                                        
                                        lock (_priceCacheLock)
                                        {
                                            if (_priceCache.TryGetValue(item.ItemId, out var cacheEntry))
                                            {
                                                // Check if cache is still valid (within 5 minutes)
                                                if (DateTime.Now - cacheEntry.fetchTime < TimeSpan.FromMinutes(5))
                                                {
                                                    hasCachedPrice = true;
                                                    cachedPrice = cacheEntry.price;
                                                }
                                            }
                                        }
                                        
                                        bool isFetching = false;
                                        lock (_fetchingPricesLock)
                                        {
                                            isFetching = _fetchingPrices.Contains(item.ItemId);
                                        }
                                        
                                        if (hasCachedPrice)
                                        {
                                            if (cachedPrice > 0)
                                            {
                                                ImGui.TextColored(ColorPrice, $"{cachedPrice:N0}g");
                                            }
                                            else
                                            {
                                                ImGui.TextColored(ColorSubdued, "No data");
                                            }
                                        }
                                        else if (isFetching)
                                        {
                                            using (var font = ImRaii.PushFont(UiBuilder.IconFont))
                                            {
                                                var time = ImGui.GetTime();
                                                var spinnerIcon = time % 1.0 < 0.5 ? FontAwesomeIcon.CircleNotch : FontAwesomeIcon.Circle;
                                                ImGui.TextColored(ColorLoading, spinnerIcon.ToIconString());
                                            }
                                            ImGui.SameLine();
                                            ImGui.TextColored(ColorLoading, "Loading");
                                        }
                                        else
                                        {
                                            // Auto-fetch if visible and not recently fetched
                                            if (isVisible && Settings.AutoRefreshPrices && DateTime.Now - _lastTrackerPriceFetch > _trackerPriceFetchDelay)
                                            {
                                                _lastTrackerPriceFetch = DateTime.Now;
                                                _ = FetchTrackerItemPrice(item.ItemId, item.IsHQ);
                                            }
                                            else
                                            {
                                                ImGui.TextColored(ColorSubdued, "---");
                                                ImGui.SameLine();
                                                using (var style = ImRaii.PushStyle(ImGuiStyleVar.FramePadding, new Vector2(2, 0)))
                                                using (var font = ImRaii.PushFont(UiBuilder.IconFont))
                                                {
                                                    if (ImGui.SmallButton(FontAwesomeIcon.DollarSign.ToIconString() + $"###{item.ItemId}_{item.IsHQ}"))
                                                    {
                                                        _ = FetchTrackerItemPrice(item.ItemId, item.IsHQ);
                                                    }
                                                }
                                                if (ImGui.IsItemHovered())
                                                    ImGui.SetTooltip("Fetch current market price");
                                            }
                                        }
                                    }
                                    else
                                    {
                                        ImGui.TextColored(ColorSubdued, "Untradable");
                                    }
                                }
                            }
                            
                            // Show detailed info on hover
                            if (ImGui.IsItemHovered() && item.Items.Count > 0)
                            {
                                ImGui.BeginTooltip();
                                foreach (var stack in item.Items)
                                {
                                    ImGui.Text($"Slot {stack.Slot}: {stack.Quantity}x");
                                    if (stack.Spiritbond > 0)
                                        ImGui.Text($"  Spiritbond: {stack.Spiritbond}%");
                                    if (stack.Condition < 30000 && stack.Condition > 0)
                                        ImGui.Text($"  Condition: {stack.Condition / 300}%");
                                }
                                ImGui.EndTooltip();
                            }
                        }
                    }
                }
            }
        }
        
        ImGui.Separator();
        
        // Actions for this inventory
        if (ImGui.Button($"Rescan {inventory.OwnerName}"))
        {
            if (inventory.Type == OwnerType.Player)
            {
                _trackerService?.ScanCurrentCharacter();
            }
            else if (inventory.Type == OwnerType.Retainer)
            {
                Plugin.ChatGui.Print($"Please open {inventory.OwnerName} and click 'Scan Retainer'");
            }
        }
        
        ImGui.SameLine();
        if (ImGui.Button("Clear This Inventory"))
        {
            _trackerService?.ClearInventory(inventory.OwnerId);
            _selectedInventory = null;
        }
    }
    
    private void DrawTrackerSearch()
    {
        ImGui.Text("Search All Tracked Items");
        ImGui.Separator();
        
        // Search box
        ImGui.SetNextItemWidth(300);
        if (ImGui.InputText("##TrackerSearch", ref _trackerSearchQuery, 100))
        {
            if (_trackerSearchQuery.Length >= 2 && _trackerService != null)
            {
                _trackerSearchResults = _trackerService.SearchItems(_trackerSearchQuery);
            }
            else
            {
                _trackerSearchResults.Clear();
            }
        }
        
        ImGui.SameLine();
        ImGui.TextColored(ColorSubdued, "Search across all tracked inventories");
        
        // Search results
        if (_trackerSearchResults.Any())
        {
            ImGui.Separator();
            ImGui.Text($"Found {_trackerSearchResults.Count} results:");
            
            using (var table = ImRaii.Table("SearchResults", 5))
            {
                if (table)
                {
                    ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
                    ImGui.TableSetupColumn("Quantity", ImGuiTableColumnFlags.WidthFixed, 80);
                    ImGui.TableSetupColumn("Location", ImGuiTableColumnFlags.WidthFixed, 250);
                    ImGui.TableSetupColumn("Quality", ImGuiTableColumnFlags.WidthFixed, 60);
                    ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, 100);
                    ImGui.TableHeadersRow();
                    
                    foreach (var item in _trackerSearchResults)
                    {
                        ImGui.TableNextRow();
                        
                        ImGui.TableNextColumn();
                        
                        // Draw item icon
                        var itemSheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>();
                        if (itemSheet != null)
                        {
                            var itemData = itemSheet.GetRow(item.ItemId);
                            if (itemData.RowId != 0 && itemData.Icon > 0)
                            {
                                var icon = Plugin.TextureProvider.GetFromGameIcon(new Dalamud.Interface.Textures.GameIconLookup(itemData.Icon));
                                if (icon != null)
                                {
                                    var wrap = icon.GetWrapOrEmpty();
                                    ImGui.Image(wrap.Handle, new Vector2(20, 20));
                                    ImGui.SameLine();
                                }
                            }
                        }
                        
                        var cleanItemName = item.ItemName.TrimEnd('=', ' ');
                        ImGui.Text(cleanItemName);
                        
                        ImGui.TableNextColumn();
                        ImGui.Text($"{item.Quantity:N0}");
                        
                        ImGui.TableNextColumn();
                        // Highlight if it's on market
                        if (item.Container.Contains("Market"))
                        {
                            ImGui.TextColored(ColorPrice, item.Container);
                        }
                        else
                        {
                            ImGui.Text(item.Container);
                        }
                        
                        ImGui.TableNextColumn();
                        if (item.IsHQ)
                        {
                            ImGui.TextColored(ColorHQItem, "HQ");
                        }
                        
                        ImGui.TableNextColumn();
                        // Price button and display using shared cache
                        bool hasCachedPrice = false;
                        long cachedPrice = 0;
                        
                        lock (_priceCacheLock)
                        {
                            if (_priceCache.TryGetValue(item.ItemId, out var cacheEntry))
                            {
                                // Check if cache is still valid (within 5 minutes)
                                if (DateTime.Now - cacheEntry.fetchTime < TimeSpan.FromMinutes(5))
                                {
                                    hasCachedPrice = true;
                                    cachedPrice = cacheEntry.price;
                                }
                            }
                        }
                        
                        bool isFetching = false;
                        lock (_fetchingPricesLock)
                        {
                            isFetching = _fetchingPrices.Contains(item.ItemId);
                        }
                        
                        if (hasCachedPrice)
                        {
                            if (cachedPrice > 0)
                            {
                                ImGui.TextColored(ColorPrice, $"{cachedPrice:N0}g");
                            }
                            else
                            {
                                ImGui.TextColored(ColorSubdued, "No data");
                            }
                        }
                        else if (isFetching)
                        {
                            ImGui.TextColored(ColorLoading, "Loading...");
                        }
                        else
                        {
                            if (ImGui.SmallButton($"Price###{item.ItemId}"))
                            {
                                _ = FetchTrackerItemPrice(item.ItemId, item.IsHQ);
                            }
                        }
                    }
                }
            }
        }
        else if (!string.IsNullOrWhiteSpace(_trackerSearchQuery) && _trackerSearchQuery.Length >= 2)
        {
            ImGui.Separator();
            ImGui.TextColored(ColorWarning, "No items found matching your search.");
        }
        
        // Summary stats
        if (_trackerService != null && _trackerService.HasData)
        {
            ImGui.Separator();
            ImGui.Text("Tracker Statistics:");
            
            var totalInventories = _trackerService.Data.Inventories.Count;
            var totalItems = _trackerService.Data.Inventories.Values.Sum(i => i.Items.Count);
            var uniqueItems = _trackerService.Data.Inventories.Values
                .SelectMany(i => i.Items)
                .Select(i => i.ItemId)
                .Distinct()
                .Count();
            
            ImGui.BulletText($"Tracked Inventories: {totalInventories}");
            ImGui.BulletText($"Total Items: {totalItems:N0}");
            ImGui.BulletText($"Unique Items: {uniqueItems:N0}");
            
            var oldestUpdate = _trackerService.Data.Inventories.Values
                .OrderBy(i => i.LastUpdated)
                .FirstOrDefault();
            
            if (oldestUpdate != null)
            {
                ImGui.BulletText($"Oldest scan: {oldestUpdate.DisplayName} ({oldestUpdate.LastUpdatedText})");
            }
        }
    }
    
    private async Task FetchTrackerItemPrice(uint itemId, bool isHQ)
    {
        // Use the shared fetching system to avoid duplicates
        lock (_fetchingPricesLock)
        {
            if (_fetchingPrices.Contains(itemId))
                return;
            _fetchingPrices.Add(itemId);
        }
        
        try
        {
            // Get the item data to check if it's tradeable
            var itemSheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>();
            if (itemSheet != null)
            {
                var itemData = itemSheet.GetRow(itemId);
                if (itemData.RowId != 0 && !itemData.IsUntradable)
                {
                    // Use the existing UniversalisClient
                    if (_universalisClient != null)
                    {
                        var priceResult = await _universalisClient.GetMarketPrice(itemId, isHQ);
                        if (priceResult != null)
                        {
                            lock (_priceCacheLock)
                            {
                                _priceCache[itemId] = (priceResult.Price, DateTime.Now);
                            }
                            
                            // Also update any items in the main inventory with this price
                            lock (_itemsLock)
                            {
                                var itemsToUpdate = _allItems.Where(i => i.ItemId == itemId).ToList();
                                foreach (var item in itemsToUpdate)
                                {
                                    item.MarketPrice = priceResult.Price > 0 ? priceResult.Price : -1;
                                    item.MarketPriceFetchTime = DateTime.Now;
                                }
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning($"Failed to fetch price for item {itemId}: {ex.Message}");
        }
        finally
        {
            lock (_fetchingPricesLock)
            {
                _fetchingPrices.Remove(itemId);
            }
        }
    }
}
