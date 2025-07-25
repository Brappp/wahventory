using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
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
        
        using var styles = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(10, 10))
                                 .Push(ImGuiStyleVar.FramePadding, new Vector2(6, 5))
                                 .Push(ImGuiStyleVar.ItemSpacing, new Vector2(8, 6));
        
        ImGui.Begin("Confirm Discard##DiscardConfirmation", ref _isDiscarding, ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse);
        
        using (var color = ImRaii.PushColor(ImGuiCol.ChildBg, new Vector4(0.541f, 0.227f, 0.227f, 0.3f)))
        {
            using (var child = ImRaii.Child("WarningHeader", new Vector2(0, 36), true, ImGuiWindowFlags.NoScrollbar))
            {
                ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 5);
                using (var font = ImRaii.PushFont(UiBuilder.IconFont))
                {
                    ImGui.TextColored(ColorError, FontAwesomeIcon.ExclamationTriangle.ToIconString());
                }
                ImGui.SameLine();
                ImGui.Text("WARNING: This will permanently delete the following items!");
            }
        }
        
        ImGui.Spacing();
        
        using (var color = ImRaii.PushColor(ImGuiCol.ChildBg, new Vector4(0.145f, 0.145f, 0.145f, 1f)))
        {
            using (var child = ImRaii.Child("SummarySection", new Vector2(0, 80), true))
            {
                DrawDiscardSummary();
            }
        }
        
        ImGui.Spacing();
        
        ImGui.Text("Items to discard:");
        var tableHeight = windowSize.Y - 280;
        using (var color = ImRaii.PushColor(ImGuiCol.ChildBg, new Vector4(0.145f, 0.145f, 0.145f, 1f)))
        {
            using (var child = ImRaii.Child("ItemTable", new Vector2(0, tableHeight), true))
            {
                DrawDiscardItemsTable();
            }
        }
        
        if (!string.IsNullOrEmpty(_discardError))
        {
            ImGui.Spacing();
            using (var color = ImRaii.PushColor(ImGuiCol.ChildBg, new Vector4(0.541f, 0.227f, 0.227f, 0.3f)))
            {
                using (var child = ImRaii.Child("ErrorSection", new Vector2(0, 30), true, ImGuiWindowFlags.NoScrollbar))
                {
                    ImGui.TextColored(ColorError, _discardError);
                }
            }
        }
        
        if (_discardProgress > 0)
        {
            ImGui.Spacing();
            var progress = (float)_discardProgress / _itemsToDiscard.Count;
            using (var color = ImRaii.PushColor(ImGuiCol.PlotHistogram, ColorSuccess))
            {
                ImGui.ProgressBar(progress, new Vector2(-1, 25), $"Discarding... {_discardProgress}/{_itemsToDiscard.Count}");
            }
        }
        
        ImGui.Spacing();
        
        DrawDiscardButtons();
        
        ImGui.End();
    }
    
    private void DrawDiscardSummary()
    {
        var totalItems = _itemsToDiscard.Count;
        var totalQuantity = _itemsToDiscard.Sum(i => i.Quantity);
        var totalValue = _itemsToDiscard.Where(i => i.MarketPrice.HasValue).Sum(i => i.MarketPrice!.Value * i.Quantity);
        var totalValueFormatted = totalValue > 0 ? $"{totalValue:N0} gil" : "Unknown";
        
        ImGui.Columns(3, "SummaryColumns", false);
        
        using (var font = ImRaii.PushFont(UiBuilder.IconFont))
        {
            ImGui.TextColored(ColorInfo, FontAwesomeIcon.List.ToIconString());
        }
        ImGui.SameLine();
        ImGui.Text("Total Items:");
        ImGui.TextColored(ColorWarning, $"{totalItems} unique items");
        
        ImGui.NextColumn();
        
        using (var font = ImRaii.PushFont(UiBuilder.IconFont))
        {
            ImGui.TextColored(ColorInfo, FontAwesomeIcon.LayerGroup.ToIconString());
        }
        ImGui.SameLine();
        ImGui.Text("Total Quantity:");
        ImGui.TextColored(ColorWarning, $"{totalQuantity} items");
        
        ImGui.NextColumn();
        
        using (var font = ImRaii.PushFont(UiBuilder.IconFont))
        {
            ImGui.TextColored(ColorPrice, FontAwesomeIcon.Coins.ToIconString());
        }
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
        using var style = ImRaii.PushStyle(ImGuiStyleVar.CellPadding, new Vector2(4, 4));
        
        using (var table = ImRaii.Table("DiscardTable", 5, 
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | 
            ImGuiTableFlags.Resizable))
        {
            if (table)
            {
                ImGui.TableSetupColumn("ID", ImGuiTableColumnFlags.WidthFixed, 60);
                ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Quantity", ImGuiTableColumnFlags.WidthFixed, 80);
                ImGui.TableSetupColumn("Location", ImGuiTableColumnFlags.WidthFixed, 120);
                ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthFixed, 100);
                ImGui.TableSetupScrollFreeze(0, 1);
                ImGui.TableHeadersRow();
                
                foreach (var item in _itemsToDiscard)
                {
                    ImGui.TableNextRow();
                    
                    ImGui.TableNextColumn();
                    ImGui.Text(item.ItemId.ToString());
                    
                    ImGui.TableNextColumn();
                    if (item.IconId > 0)
                    {
                        var icon = _iconCache.GetIcon(item.IconId);
                        if (icon != null)
                        {
                            ImGui.Image(icon.ImGuiHandle, new Vector2(20, 20));
                            ImGui.SameLine();
                        }
                    }
                    ImGui.Text(item.Name);
                    if (item.IsHQ)
                    {
                        ImGui.SameLine();
                        ImGui.TextColored(ColorHQItem, "[HQ]");
                    }
                    
                    ImGui.TableNextColumn();
                    ImGui.Text(item.Quantity.ToString());
                    
                    ImGui.TableNextColumn();
                    ImGui.Text(GetLocationName(item.Container));
                    
                    ImGui.TableNextColumn();
                    if (item.MarketPrice.HasValue && item.MarketPrice.Value > 0)
                    {
                        ImGui.TextColored(ColorPrice, $"{item.MarketPrice.Value * item.Quantity:N0} gil");
                    }
                    else
                    {
                        ImGui.TextColored(ColorSubdued, "N/A");
                    }
                }
            }
        }
    }
    
    private void DrawDiscardItemRow(InventoryItemInfo item)
    {
        ImGui.TableNextRow();
        
        ImGui.TableNextColumn();
        
        var iconSize = new Vector2(20, 20);
        if (item.IconId > 0)
        {
            var icon = _iconCache.GetIcon(item.IconId);
            if (icon != null)
            {
                var startY = ImGui.GetCursorPosY();
                ImGui.SetCursorPosY(startY - 2);
                ImGui.Image(icon.ImGuiHandle, iconSize);
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
        
        var nameColor = item.IsHQ ? ColorHQName : ImGui.GetStyle().Colors[(int)ImGuiCol.Text];
        ImGui.TextColored(nameColor, item.Name);
        if (item.IsHQ)
        {
            ImGui.SameLine();
            ImGui.TextColored(ColorHQItem, " [HQ]");
        }
        ImGui.TableNextColumn();
        ImGui.Text($"{item.Quantity}");
        
        ImGui.TableNextColumn();
        if (item.ItemLevel > 0)
        {
            ImGui.Text(item.ItemLevel.ToString());
        }
        else
        {
            ImGui.TextColored(ColorSubdued, "-");
        }
        
        ImGui.TableNextColumn();
        ImGui.Text(GetContainerDisplayName(item.Container));
        
        if (Settings.ShowMarketPrices)
        {
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
            using (var color = ImRaii.PushColor(ImGuiCol.Button, new Vector4(0.541f, 0.227f, 0.227f, 1f))
                                     .Push(ImGuiCol.ButtonHovered, new Vector4(0.641f, 0.327f, 0.327f, 1f)))
            {
                if (ImGui.Button("Start Discarding", new Vector2(buttonWidth, buttonHeight)))
                {
                    StartDiscarding();
                }
            }
            
            ImGui.SameLine();
            
            using (var disabled = ImRaii.Disabled(_isDiscarding))
            {
                if (ImGui.Button("Cancel", new Vector2(buttonWidth, buttonHeight)))
                {
                    CancelDiscard();
                }
            }
        }
        else
        {
            using (var disabled = ImRaii.Disabled())
            {
                ImGui.Button("Discarding...", new Vector2(buttonWidth, buttonHeight));
            }
            
            ImGui.SameLine();
            
            using (var color = ImRaii.PushColor(ImGuiCol.Button, new Vector4(0.541f, 0.541f, 0.227f, 1f))
                                     .Push(ImGuiCol.ButtonHovered, new Vector4(0.641f, 0.641f, 0.327f, 1f)))
            {
                if (ImGui.Button("Cancel", new Vector2(buttonWidth, buttonHeight)))
                {
                    CancelDiscard();
                }
            }
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
                    InventoryHelpers.IsSafeToDiscard(i, BlacklistedItems)).ToList();
                    
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
