using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Interface;
using FFXIVClientStructs.FFXIV.Component.GUI;
using ImGuiNET;
using wahventory.Helpers;
using wahventory.Models;

namespace wahventory.Modules.Inventory;

public partial class InventoryManagementModule
{
    private void DrawDiscardConfirmation()
    {
        var windowSize = new Vector2(700, 500);
        ImGui.SetNextWindowSize(windowSize, ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.FirstUseEver, new Vector2(0.5f, 0.5f));
        
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(10, 10));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(6, 5));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8, 6));
        
        ImGui.Begin("Confirm Discard##DiscardConfirmation", ref _isDiscarding, ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse);
        
        // Header with warning
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.541f, 0.227f, 0.227f, 0.3f));
        ImGui.BeginChild("WarningHeader", new Vector2(0, 36), true, ImGuiWindowFlags.NoScrollbar);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 5);
        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.TextColored(ColorError, FontAwesomeIcon.ExclamationTriangle.ToIconString());
        ImGui.PopFont();
        ImGui.SameLine();
        ImGui.Text("WARNING: This will permanently delete the following items!");
        ImGui.EndChild();
        ImGui.PopStyleColor();
        
        ImGui.Spacing();
        
        // Summary section
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.145f, 0.145f, 0.145f, 1f));
        ImGui.BeginChild("SummarySection", new Vector2(0, 80), true);
        DrawDiscardSummary();
        ImGui.EndChild();
        ImGui.PopStyleColor();
        
        ImGui.Spacing();
        
        ImGui.Text("Items to discard:");
        var tableHeight = windowSize.Y - 280;
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.145f, 0.145f, 0.145f, 1f));
        ImGui.BeginChild("ItemTable", new Vector2(0, tableHeight), true);
        DrawDiscardItemsTable();
        ImGui.EndChild();
        ImGui.PopStyleColor();
        
        // Error display
        if (!string.IsNullOrEmpty(_discardError))
        {
            ImGui.Spacing();
            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.541f, 0.227f, 0.227f, 0.3f));
            ImGui.BeginChild("ErrorSection", new Vector2(0, 30), true, ImGuiWindowFlags.NoScrollbar);
            ImGui.TextColored(ColorError, _discardError);
            ImGui.EndChild();
            ImGui.PopStyleColor();
        }
        
        // Progress bar
        if (_discardProgress > 0)
        {
            ImGui.Spacing();
            var progress = (float)_discardProgress / _itemsToDiscard.Count;
            ImGui.PushStyleColor(ImGuiCol.PlotHistogram, ColorSuccess);
            ImGui.ProgressBar(progress, new Vector2(-1, 25), $"Discarding... {_discardProgress}/{_itemsToDiscard.Count}");
            ImGui.PopStyleColor();
        }
        
        ImGui.Spacing();
        
        // Buttons
        DrawDiscardButtons();
        
        ImGui.End();
        ImGui.PopStyleVar(3);
    }
    
    private void DrawDiscardSummary()
    {
        var totalItems = _itemsToDiscard.Count;
        var totalQuantity = _itemsToDiscard.Sum(i => i.Quantity);
        var totalValue = _itemsToDiscard.Where(i => i.MarketPrice.HasValue).Sum(i => i.MarketPrice!.Value * i.Quantity);
        var totalValueFormatted = totalValue > 0 ? $"{totalValue:N0} gil" : "Unknown";
        
        ImGui.Columns(3, "SummaryColumns", false);
        
        // Total Items
        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.TextColored(ColorInfo, FontAwesomeIcon.List.ToIconString());
        ImGui.PopFont();
        ImGui.SameLine();
        ImGui.Text("Total Items:");
        ImGui.TextColored(ColorWarning, $"{totalItems} unique items");
        
        ImGui.NextColumn();
        
        // Total Quantity
        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.TextColored(ColorInfo, FontAwesomeIcon.LayerGroup.ToIconString());
        ImGui.PopFont();
        ImGui.SameLine();
        ImGui.Text("Total Quantity:");
        ImGui.TextColored(ColorWarning, $"{totalQuantity} items");
        
        ImGui.NextColumn();
        
        // Market Value
        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.TextColored(ColorPrice, FontAwesomeIcon.Coins.ToIconString());
        ImGui.PopFont();
        ImGui.SameLine();
        ImGui.Text("Market Value:");
        if (totalValue > 0)
        {
            ImGui.TextColored(ColorPrice, totalValueFormatted);
        }
        else
        {
            ImGui.TextColored(ColorSubdued, totalValueFormatted);
        }
        
        ImGui.Columns(1);
    }
    
    private void DrawDiscardItemsTable()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(4, 4));
        
        if (ImGui.BeginTable("DiscardItemsTable", Settings.ShowMarketPrices ? 5 : 4, 
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.Resizable))
        {
            ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Qty", ImGuiTableColumnFlags.WidthFixed, 40);
            ImGui.TableSetupColumn("Location", ImGuiTableColumnFlags.WidthFixed, 100);
            if (Settings.ShowMarketPrices)
            {
                ImGui.TableSetupColumn("Price", ImGuiTableColumnFlags.WidthFixed, 80);
                ImGui.TableSetupColumn("Total", ImGuiTableColumnFlags.WidthFixed, 80);
            }
            else
            {
                ImGui.TableSetupColumn("Status", ImGuiTableColumnFlags.WidthFixed, 120);
            }
            
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableHeadersRow();
            
            foreach (var item in _itemsToDiscard)
            {
                DrawDiscardItemRow(item);
            }
            
            ImGui.EndTable();
        }
        
        ImGui.PopStyleVar(); // Pop CellPadding
    }
    
    private void DrawDiscardItemRow(InventoryItemInfo item)
    {
        ImGui.TableNextRow();
        
        // Item name column with icon
        ImGui.TableNextColumn();
        
        // Item icon and name aligned properly  
        var iconSize = new Vector2(20, 20);
        if (item.IconId > 0)
        {
            var icon = _iconCache.GetIcon(item.IconId);
            if (icon != null)
            {
                var startY = ImGui.GetCursorPosY();
                ImGui.SetCursorPosY(startY - 2);  // Lower the icon by 2 pixels
                ImGui.Image(icon.ImGuiHandle, iconSize);
                ImGui.SetCursorPosY(startY);
                ImGui.SameLine(0, 5);
            }
            else
            {
                // Reserve space for missing icon
                ImGui.Dummy(iconSize);
                ImGui.SameLine(0, 5);
            }
        }
        else
        {
            // Reserve space for missing icon
            ImGui.Dummy(iconSize);
            ImGui.SameLine(0, 5);
        }
        
        var nameColor = item.IsHQ ? ColorHQName : ImGui.GetStyle().Colors[(int)ImGuiCol.Text];
        ImGui.TextColored(nameColor, item.Name);
        if (item.IsHQ)
        {
            ImGui.SameLine();
            ImGui.TextColored(ColorHQItem, " [HQ]");
        }
        // Quantity column
        ImGui.TableNextColumn();
        ImGui.Text($"{item.Quantity}");
        
        // Location column
        ImGui.TableNextColumn();
        ImGui.Text(GetContainerDisplayName(item.Container));
        
        if (Settings.ShowMarketPrices)
        {
            // Price column
            ImGui.TableNextColumn();
            if (!item.CanBeTraded)
            {
                ImGui.TextColored(ColorNotTradeable, "Not Tradeable");
            }
            else if (item.MarketPrice.HasValue && item.MarketPrice.Value > 0)
            {
                ImGui.Text($"{item.MarketPrice.Value:N0}g");
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
            ImGui.TableNextColumn();
            if (!item.CanBeTraded)
            {
                ImGui.TextColored(ColorNotTradeable, "---");
            }
            else if (item.MarketPrice.HasValue && item.MarketPrice.Value > 0)
            {
                var totalValue = item.MarketPrice.Value * item.Quantity;
                ImGui.TextColored(ColorPrice, $"{totalValue:N0}g");
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
            // Status column
            ImGui.TableNextColumn();
            if (!item.CanBeTraded)
            {
                ImGui.TextColored(ColorError, "Not Tradeable");
            }
            else if (!item.CanBeDiscarded)
            {
                ImGui.TextColored(ColorError, "Not Discardable");
            }
            else if (item.IsHQ)
            {
                ImGui.TextColored(ColorHQItem, "High Quality");
            }
            else if (item.IsCollectable)
            {
                ImGui.TextColored(ColorLoading, "Collectable");
            }
            else
            {
                ImGui.TextColored(ColorSuccess, "Normal");
            }
        }
    }
    
    private void DrawDiscardButtons()
    {
        var buttonWidth = 120f;
        var buttonHeight = 30f;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var totalWidth = buttonWidth * 2 + spacing;
        var availableWidth = ImGui.GetContentRegionAvail().X;
        var centerPos = (availableWidth - totalWidth) * 0.5f;
        
        if (centerPos > 0)
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + centerPos);
        
        if (_discardProgress == 0)
        {
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.541f, 0.227f, 0.227f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.641f, 0.327f, 0.327f, 1f));
            
            if (ImGui.Button("Start Discarding", new Vector2(buttonWidth, buttonHeight)))
            {
                StartDiscarding();
            }
            ImGui.PopStyleColor(2);
            
            ImGui.SameLine();
            
            if (ImGui.Button("Cancel", new Vector2(buttonWidth, buttonHeight)))
            {
                CancelDiscard();
            }
        }
        else
        {
            ImGui.BeginDisabled();
            ImGui.Button("Discarding...", new Vector2(buttonWidth, buttonHeight));
            ImGui.EndDisabled();
            
            ImGui.SameLine();
            
            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.541f, 0.541f, 0.227f, 1f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.641f, 0.641f, 0.327f, 1f));
            if (ImGui.Button("Cancel", new Vector2(buttonWidth, buttonHeight)))
            {
                CancelDiscard();
            }
            ImGui.PopStyleColor(2);
        }
    }
    
    private void PrepareDiscard()
    {
        List<uint> selectedItemsCopy;
        lock (_selectedItemsLock)
        {
            selectedItemsCopy = new List<uint>(_selectedItems);
        }
        
        Plugin.Log.Information($"PrepareDiscard called. Selected items count: {selectedItemsCopy.Count}");
        
        var actualItemsToDiscard = new List<InventoryItemInfo>();
        
        lock (_itemsLock)
        {
            foreach (var selectedItemId in selectedItemsCopy)
            {
                var actualItems = _originalItems.Where(i => 
                    i.ItemId == selectedItemId && 
                    InventoryHelpers.IsSafeToDiscard(i, Settings.BlacklistedItems)).ToList();
                    
                Plugin.Log.Information($"Found {actualItems.Count} instances of item {selectedItemId} to discard");
                
                foreach (var item in actualItems)
                {
                    lock (_priceCacheLock)
                    {
                        if (_priceCache.TryGetValue(item.ItemId, out var cached))
                        {
                            item.MarketPrice = cached.price;
                            item.MarketPriceFetchTime = cached.fetchTime;
                        }
                    }
                }
                
                actualItemsToDiscard.AddRange(actualItems);
            }
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
            
            var selectYesno = (FFXIVClientStructs.FFXIV.Client.UI.AddonSelectYesno*)addon;
            if (selectYesno->YesButton != null)
            {
                Plugin.Log.Information("Yes button found, enabling and clicking");
                selectYesno->YesButton->AtkComponentBase.SetEnabledState(true);
                addon->FireCallbackInt(0);
                
                Plugin.Log.Information("Yes button clicked, waiting for response");
                _confirmRetryCount = 0;
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
            if (_confirmRetryCount > 50)
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
        
        RefreshInventory();
    }
}
