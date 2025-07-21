using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Interface;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;
using WahVentory.Helpers;
using WahVentory.Models;

namespace WahVentory.Modules.Inventory;

public partial class InventoryManagementModule
{
    private void DrawDiscardConfirmation()
    {
        var windowSize = new Vector2(800, 600);
        ImGui.SetNextWindowSize(windowSize, ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.FirstUseEver, new Vector2(0.5f, 0.5f));
        
        ImGui.Begin("Confirm Discard##DiscardConfirmation", ref _isDiscarding, ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse);
        
        // Header with warning
        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.TextColored(ColorError, FontAwesomeIcon.ExclamationTriangle.ToIconString());
        ImGui.PopFont();
        ImGui.SameLine();
        ImGui.TextColored(ColorError, "WARNING: This will permanently delete the following items!");
        
        ImGui.Separator();
        
        // Summary section
        DrawDiscardSummary();
        
        ImGui.Separator();
        
        // Items table
        ImGui.Text("Items to discard:");
        ImGui.BeginChild("ItemTable", new Vector2(0, 350), true);
        DrawDiscardItemsTable();
        ImGui.EndChild();
        
        // Error display
        if (!string.IsNullOrEmpty(_discardError))
        {
            ImGui.Separator();
            ImGui.TextColored(ColorError, _discardError);
        }
        
        // Progress bar
        if (_discardProgress > 0)
        {
            ImGui.Separator();
            var progress = (float)_discardProgress / _itemsToDiscard.Count;
            ImGui.ProgressBar(progress, new Vector2(-1, 0), $"Discarding... {_discardProgress}/{_itemsToDiscard.Count}");
        }
        
        ImGui.Separator();
        
        // Buttons
        DrawDiscardButtons();
        
        ImGui.End();
    }
    
    private void DrawDiscardSummary()
    {
        var totalItems = _itemsToDiscard.Count;
        var totalQuantity = _itemsToDiscard.Sum(i => i.Quantity);
        var totalValue = _itemsToDiscard.Where(i => i.MarketPrice.HasValue).Sum(i => i.MarketPrice!.Value * i.Quantity);
        var totalValueFormatted = totalValue > 0 ? $"{totalValue:N0} gil" : "Unknown";
        
        // Create a nice summary box
        if (ImGui.BeginTable("SummaryTable", 2, ImGuiTableFlags.Borders))
        {
            ImGui.TableSetupColumn("Label", ImGuiTableColumnFlags.WidthFixed, 150);
            ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthStretch);
            
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.Text("Total Items:");
            ImGui.TableSetColumnIndex(1);
            ImGui.Text($"{totalItems} unique items");
            
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.Text("Total Quantity:");
            ImGui.TableSetColumnIndex(1);
            ImGui.Text($"{totalQuantity} items");
            
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            ImGui.Text("Market Value:");
            ImGui.TableSetColumnIndex(1);
            if (totalValue > 0)
            {
                ImGui.TextColored(ColorPrice, totalValueFormatted);
            }
            else
            {
                ImGui.TextColored(ColorSubdued, totalValueFormatted);
            }
            
            ImGui.EndTable();
        }
    }
    
    private void DrawDiscardItemsTable()
    {
        if (ImGui.BeginTable("DiscardItemsTable", Settings.ShowMarketPrices ? 6 : 5, 
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY))
        {
            // Setup columns
            ImGui.TableSetupColumn("Icon", ImGuiTableColumnFlags.WidthFixed, 40);
            ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Qty", ImGuiTableColumnFlags.WidthFixed, 60);
            ImGui.TableSetupColumn("Location", ImGuiTableColumnFlags.WidthFixed, 120);
            if (Settings.ShowMarketPrices)
            {
                ImGui.TableSetupColumn("Unit Price", ImGuiTableColumnFlags.WidthFixed, 100);
                ImGui.TableSetupColumn("Total Value", ImGuiTableColumnFlags.WidthFixed, 100);
            }
            else
            {
                ImGui.TableSetupColumn("HQ", ImGuiTableColumnFlags.WidthFixed, 40);
            }
            
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableHeadersRow();
            
            foreach (var item in _itemsToDiscard)
            {
                DrawDiscardItemRow(item);
            }
            
            ImGui.EndTable();
        }
    }
    
    private void DrawDiscardItemRow(InventoryItemInfo item)
    {
        ImGui.TableNextRow();
        
        // Icon column
        ImGui.TableSetColumnIndex(0);
        var icon = _iconCache.GetIcon(item.IconId);
        if (icon != null)
        {
            ImGui.Image(icon.ImGuiHandle, new Vector2(32, 32));
        }
        
        // Item name column
        ImGui.TableSetColumnIndex(1);
        var nameColor = item.IsHQ ? ColorHQName : ImGui.GetStyle().Colors[(int)ImGuiCol.Text];
        ImGui.TextColored(nameColor, item.Name);
        if (item.IsHQ)
        {
            ImGui.SameLine();
            ImGui.TextColored(ColorHQItem, " [HQ]");
        }
        
        // Quantity column
        ImGui.TableSetColumnIndex(2);
        ImGui.Text($"{item.Quantity}");
        
        // Location column
        ImGui.TableSetColumnIndex(3);
        ImGui.Text(GetContainerDisplayName(item.Container));
        
        if (Settings.ShowMarketPrices)
        {
            // Unit price column
            ImGui.TableSetColumnIndex(4);
            if (!item.CanBeTraded)
            {
                ImGui.TextColored(ColorNotTradeable, "Not Tradeable");
            }
            else if (item.MarketPrice.HasValue && item.MarketPrice.Value > 0)
            {
                ImGui.Text($"{item.MarketPrice.Value:N0}");
            }
            else if (item.MarketPrice.HasValue && item.MarketPrice.Value == -1)
            {
                ImGui.TextColored(ColorSubdued, "N/A");
            }
            else
            {
                ImGui.TextColored(ColorLoading, "Loading...");
            }
            
            // Total value column
            ImGui.TableSetColumnIndex(5);
            if (!item.CanBeTraded)
            {
                ImGui.TextColored(ColorNotTradeable, "---");
            }
            else if (item.MarketPrice.HasValue && item.MarketPrice.Value > 0)
            {
                var totalValue = item.MarketPrice.Value * item.Quantity;
                ImGui.TextColored(ColorPrice, $"{totalValue:N0}");
            }
            else if (item.MarketPrice.HasValue && item.MarketPrice.Value == -1)
            {
                ImGui.TextColored(ColorSubdued, "N/A");
            }
            else
            {
                ImGui.TextColored(ColorLoading, "...");
            }
        }
        else
        {
            // HQ indicator column
            ImGui.TableSetColumnIndex(4);
            if (item.IsHQ)
            {
                ImGui.TextColored(ColorHQItem, "HQ");
            }
        }
    }
    
    private void DrawDiscardButtons()
    {
        var buttonWidth = 150f;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var totalWidth = buttonWidth * 2 + spacing;
        var availableWidth = ImGui.GetContentRegionAvail().X;
        var centerPos = (availableWidth - totalWidth) * 0.5f;
        
        if (centerPos > 0)
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + centerPos);
        
        // Start/Cancel button
        if (_discardProgress == 0)
        {
            if (ImGui.Button("Start Discarding", new Vector2(buttonWidth, 35)))
            {
                StartDiscarding();
            }
            
            ImGui.SameLine();
            
            if (ImGui.Button("Cancel", new Vector2(buttonWidth, 35)))
            {
                CancelDiscard();
            }
        }
        else
        {
            ImGui.BeginDisabled();
            ImGui.Button("Discarding...", new Vector2(buttonWidth, 35));
            ImGui.EndDisabled();
            
            ImGui.SameLine();
            
            if (ImGui.Button("Cancel", new Vector2(buttonWidth, 35)))
            {
                CancelDiscard();
            }
        }
    }
    
    private void PrepareDiscard()
    {
        Plugin.Log.Information($"PrepareDiscard called. Selected items count: {_selectedItems.Count}");
        
        // Get the actual individual items from inventory, not the grouped/combined ones
        var actualItemsToDiscard = new List<InventoryItemInfo>();
        
        foreach (var selectedItemId in _selectedItems)
        {
            // Find all actual inventory instances of this item ID from the original items
            var actualItems = _originalItems.Where(i => 
                i.ItemId == selectedItemId && 
                InventoryHelpers.IsSafeToDiscard(i, Settings.BlacklistedItems)).ToList();
                
            Plugin.Log.Information($"Found {actualItems.Count} instances of item {selectedItemId} to discard");
            
            // Copy over market price information from cache
            foreach (var item in actualItems)
            {
                if (_priceCache.TryGetValue(item.ItemId, out var cached))
                {
                    item.MarketPrice = cached.price;
                    item.MarketPriceFetchTime = cached.fetchTime;
                }
            }
            
            actualItemsToDiscard.AddRange(actualItems);
        }
        
        _itemsToDiscard = actualItemsToDiscard;
        Plugin.Log.Information($"Items to discard after filtering: {_itemsToDiscard.Count}");
        
        if (_itemsToDiscard.Count > 0)
        {
            _isDiscarding = true;
            _discardProgress = 0;
            _discardError = null;
            Plugin.Log.Information("Discard preparation successful, showing confirmation dialog");
        }
        else
        {
            Plugin.Log.Warning("No items to discard after filtering");
            Plugin.ChatGui.PrintError("No items selected for discard or all selected items cannot be safely discarded.");
        }
    }
    
    private void StartDiscarding()
    {
        Plugin.Log.Information($"StartDiscarding called. Items to discard: {_itemsToDiscard.Count}");
        _taskManager.Abort();
        _taskManager.Enqueue(() => DiscardNextItem());
        Plugin.Log.Information("Discard task enqueued");
    }
    
    private unsafe void DiscardNextItem()
    {
        Plugin.Log.Information($"DiscardNextItem called. Progress: {_discardProgress}/{_itemsToDiscard.Count}");
        
        if (_discardProgress >= _itemsToDiscard.Count)
        {
            Plugin.ChatGui.Print("Finished discarding items.");
            CancelDiscard();
            return;
        }
        
        var item = _itemsToDiscard[_discardProgress];
        Plugin.Log.Information($"Attempting to discard item: {item.Name} (ID: {item.ItemId})");
        
        try
        {
            _inventoryHelpers.DiscardItem(item);
            _discardProgress++;
            Plugin.Log.Information($"Discard call completed for {item.Name}");
            
            // Reset confirmation state for this item
            _confirmRetryCount = 0;
            _discardStartTime = DateTime.Now;
            
            _taskManager.EnqueueDelay(500);
            _taskManager.Enqueue(() => ConfirmDiscard());
        }
        catch (Exception ex)
        {
            _discardError = $"Failed to discard {item.Name}: {ex.Message}";
            Plugin.Log.Error(ex, $"Failed to discard {item.Name}");
        }
    }
    
    private unsafe void ConfirmDiscard()
    {
        Plugin.Log.Information($"ConfirmDiscard called, looking for dialog (retry {_confirmRetryCount})");
        
        // Check for timeout
        if (DateTime.Now - _discardStartTime > TimeSpan.FromSeconds(15))
        {
            Plugin.Log.Warning("Discard confirmation timed out");
            _discardError = "Discard confirmation timed out";
            CancelDiscard();
            return;
        }
        
        var addon = GetDiscardAddon();
        if (addon != null)
        {
            Plugin.Log.Information("Found discard dialog, clicking Yes");
            
            // Get the Yes button (should be YesButton like in ARDiscard)
            var selectYesno = (FFXIVClientStructs.FFXIV.Client.UI.AddonSelectYesno*)addon;
            if (selectYesno->YesButton != null)
            {
                Plugin.Log.Information("Yes button found, enabling and clicking");
                selectYesno->YesButton->AtkComponentBase.SetEnabledState(true);
                addon->FireCallbackInt(0);
                
                Plugin.Log.Information("Yes button clicked, waiting for response");
                _confirmRetryCount = 0; // Reset retry count
                _taskManager.EnqueueDelay(500);
                _taskManager.Enqueue(() => WaitForDiscardComplete());
            }
            else
            {
                Plugin.Log.Warning("Yes button not found in dialog");
                _confirmRetryCount++;
                if (_confirmRetryCount > 10)
                {
                    Plugin.Log.Error("Too many retries trying to find Yes button");
                    _discardError = "Could not find Yes button in dialog";
                    CancelDiscard();
                    return;
                }
                _taskManager.EnqueueDelay(100);
                _taskManager.Enqueue(() => ConfirmDiscard());
            }
        }
        else
        {
            _confirmRetryCount++;
            if (_confirmRetryCount > 50) // 5 seconds total
            {
                Plugin.Log.Warning("No discard dialog found after many retries, assuming no confirmation needed");
                _confirmRetryCount = 0;
                _taskManager.EnqueueDelay(200);
                _taskManager.Enqueue(() => DiscardNextItem());
            }
            else
            {
                Plugin.Log.Information("No discard dialog found yet, retrying");
                _taskManager.EnqueueDelay(100);
                _taskManager.Enqueue(() => ConfirmDiscard());
            }
        }
    }
    
    private unsafe void WaitForDiscardComplete()
    {
        Plugin.Log.Information("WaitForDiscardComplete called");
        
        // Check if dialog is still visible
        var addon = GetDiscardAddon();
        if (addon != null)
        {
            Plugin.Log.Information("Dialog still visible, waiting longer");
            _taskManager.EnqueueDelay(100);
            _taskManager.Enqueue(() => WaitForDiscardComplete());
        }
        else
        {
            Plugin.Log.Information("Dialog dismissed, continuing to next item");
            _taskManager.EnqueueDelay(200);
            _taskManager.Enqueue(() => DiscardNextItem());
        }
    }
    
    private unsafe AtkUnitBase* GetDiscardAddon()
    {
        for (int i = 1; i < 100; i++)
        {
            try
            {
                var addon = (AtkUnitBase*)Plugin.GameGui.GetAddonByName("SelectYesno", i);
                if (addon == null) return null;
                
                if (addon->IsVisible && addon->UldManager.LoadedState == FFXIVClientStructs.FFXIV.Component.GUI.AtkLoadState.Loaded)
                {
                    // Check if it's a discard dialog
                    var textNode = addon->UldManager.NodeList[15]->GetAsAtkTextNode();
                    if (textNode != null)
                    {
                        var text = Dalamud.Memory.MemoryHelper.ReadSeString(&textNode->NodeText).TextValue;
                        Plugin.Log.Information($"YesNo dialog text: {text}");
                        
                        if (text.Contains("Discard", StringComparison.OrdinalIgnoreCase) || 
                            text.Contains("discard", StringComparison.OrdinalIgnoreCase))
                        {
                            Plugin.Log.Information("Found discard confirmation dialog");
                            return addon;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, $"Error checking addon {i}");
                return null;
            }
        }
        
        Plugin.Log.Information("No discard dialog found");
        return null;
    }
    
    private void CancelDiscard()
    {
        _taskManager.Abort();
        _isDiscarding = false;
        _itemsToDiscard.Clear();
        _discardProgress = 0;
        _discardError = null;
        _confirmRetryCount = 0;
        _discardStartTime = DateTime.MinValue;
        
        // Refresh inventory after discard
        RefreshInventory();
    }
}
