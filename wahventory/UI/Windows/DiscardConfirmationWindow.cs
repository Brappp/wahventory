using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using wahventory.Models;
using wahventory.Services;
using wahventory.Services.Helpers;

namespace wahventory.UI.Windows;

public class DiscardConfirmationWindow : Window, IDisposable
{
    private readonly DiscardService _discardService;
    private readonly IconCache _iconCache;
    
    private static readonly Vector4 ColorError = new(0.8f, 0.2f, 0.2f, 1f);
    private static readonly Vector4 ColorWarning = new(0.9f, 0.5f, 0.1f, 1f);
    private static readonly Vector4 ColorInfo = new(0.7f, 0.7f, 0.7f, 1f);
    private static readonly Vector4 ColorPrice = new(1f, 0.8f, 0.2f, 1f);
    private static readonly Vector4 ColorSubdued = new(0.6f, 0.6f, 0.6f, 1f);
    private static readonly Vector4 ColorSuccess = new(0.2f, 0.8f, 0.2f, 1f);
    private static readonly Vector4 ColorHQItem = new(0.6f, 0.8f, 1f, 1f);
    
    public DiscardConfirmationWindow(
        DiscardService discardService,
        IconCache iconCache)
        : base("Confirm Discard##DiscardConfirmation", ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse)
    {
        Size = new Vector2(700, 500);
        SizeCondition = ImGuiCond.FirstUseEver;
        
        _discardService = discardService;
        _iconCache = iconCache;
        
        _discardService.OnDiscardStarted += Show;
        _discardService.OnDiscardCompleted += Hide;
        _discardService.OnDiscardCancelled += Hide;
    }
    
    public void Show()
    {
        IsOpen = true;
    }
    
    public void Hide()
    {
        IsOpen = false;
    }
    
    public override void Draw()
    {
        if (!_discardService.IsDiscarding)
        {
            Hide();
            return;
        }
        
        using var styles = ImRaii.PushStyle(ImGuiStyleVar.WindowPadding, new Vector2(10, 10))
                                 .Push(ImGuiStyleVar.FramePadding, new Vector2(6, 5))
                                 .Push(ImGuiStyleVar.ItemSpacing, new Vector2(8, 6));
        
        // Warning header
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
        
        // Summary section
        using (var color = ImRaii.PushColor(ImGuiCol.ChildBg, new Vector4(0.145f, 0.145f, 0.145f, 1f)))
        {
            using (var child = ImRaii.Child("SummarySection", new Vector2(0, 80), true))
            {
                DrawSummary();
            }
        }
        
        ImGui.Spacing();
        
        // Items table
        ImGui.Text("Items to discard:");
            var tableHeight = (Size?.Y ?? 500) - 280;
        using (var color = ImRaii.PushColor(ImGuiCol.ChildBg, new Vector4(0.145f, 0.145f, 0.145f, 1f)))
        {
            using (var child = ImRaii.Child("ItemTable", new Vector2(0, tableHeight), true))
            {
                DrawItemsTable();
            }
        }
        
        // Error message
        if (!string.IsNullOrEmpty(_discardService.DiscardError))
        {
            ImGui.Spacing();
            using (var color = ImRaii.PushColor(ImGuiCol.ChildBg, new Vector4(0.541f, 0.227f, 0.227f, 0.3f)))
            {
                using (var child = ImRaii.Child("ErrorSection", new Vector2(0, 30), true, ImGuiWindowFlags.NoScrollbar))
                {
                    ImGui.TextColored(ColorError, _discardService.DiscardError);
                }
            }
        }
        
        // Progress bar
        if (_discardService.DiscardProgress > 0)
        {
            ImGui.Spacing();
            var progress = (float)_discardService.DiscardProgress / _discardService.TotalItems;
            using (var color = ImRaii.PushColor(ImGuiCol.PlotHistogram, ColorSuccess))
            {
                ImGui.ProgressBar(progress, new Vector2(-1, 25), 
                    $"Discarding... {_discardService.DiscardProgress}/{_discardService.TotalItems}");
            }
        }
        
        ImGui.Spacing();
        
        // Buttons
        DrawButtons();
    }
    
    private void DrawSummary()
    {
        var items = _discardService.ItemsToDiscard;
        var totalItems = items.Count;
        var totalQuantity = items.Sum(i => i.Quantity);
        var totalValue = items.Where(i => i.MarketPrice.HasValue).Sum(i => i.MarketPrice!.Value * i.Quantity);
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
    
    private void DrawItemsTable()
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
                
                foreach (var item in _discardService.ItemsToDiscard)
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
                            ImGui.Image(icon.Handle, new Vector2(20, 20));
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
    
    private void DrawButtons()
    {
        var buttonWidth = 120f;
        var buttonHeight = 30f;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var totalWidth = buttonWidth * 2 + spacing;
        var availableWidth = ImGui.GetContentRegionAvail().X;
        var centerPos = (availableWidth - totalWidth) * 0.5f;
        
        if (centerPos > 0)
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + centerPos);
        
        if (_discardService.DiscardProgress == 0)
        {
            using (var color = ImRaii.PushColor(ImGuiCol.Button, new Vector4(0.541f, 0.227f, 0.227f, 1f))
                                     .Push(ImGuiCol.ButtonHovered, new Vector4(0.641f, 0.327f, 0.327f, 1f)))
            {
                if (ImGui.Button("Start Discarding", new Vector2(buttonWidth, buttonHeight)))
                {
                    _discardService.StartDiscarding();
                }
            }
            
            ImGui.SameLine();
            
            if (ImGui.Button("Cancel", new Vector2(buttonWidth, buttonHeight)))
            {
                _discardService.CancelDiscard();
                Hide();
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
                    _discardService.CancelDiscard();
                    Hide();
                }
            }
        }
    }
    
    private string GetLocationName(FFXIVClientStructs.FFXIV.Client.Game.InventoryType container)
    {
        return container switch
        {
            FFXIVClientStructs.FFXIV.Client.Game.InventoryType.Inventory1 => "Inventory (1)",
            FFXIVClientStructs.FFXIV.Client.Game.InventoryType.Inventory2 => "Inventory (2)",
            FFXIVClientStructs.FFXIV.Client.Game.InventoryType.Inventory3 => "Inventory (3)",
            FFXIVClientStructs.FFXIV.Client.Game.InventoryType.Inventory4 => "Inventory (4)",
            FFXIVClientStructs.FFXIV.Client.Game.InventoryType.ArmoryMainHand => "Armory (Main Hand)",
            FFXIVClientStructs.FFXIV.Client.Game.InventoryType.ArmoryOffHand => "Armory (Off Hand)",
            FFXIVClientStructs.FFXIV.Client.Game.InventoryType.ArmoryHead => "Armory (Head)",
            FFXIVClientStructs.FFXIV.Client.Game.InventoryType.ArmoryBody => "Armory (Body)",
            FFXIVClientStructs.FFXIV.Client.Game.InventoryType.ArmoryHands => "Armory (Hands)",
            FFXIVClientStructs.FFXIV.Client.Game.InventoryType.ArmoryLegs => "Armory (Legs)",
            FFXIVClientStructs.FFXIV.Client.Game.InventoryType.ArmoryFeets => "Armory (Feet)",
            FFXIVClientStructs.FFXIV.Client.Game.InventoryType.ArmoryEar => "Armory (Earrings)",
            FFXIVClientStructs.FFXIV.Client.Game.InventoryType.ArmoryNeck => "Armory (Necklace)",
            FFXIVClientStructs.FFXIV.Client.Game.InventoryType.ArmoryWrist => "Armory (Bracelets)",
            FFXIVClientStructs.FFXIV.Client.Game.InventoryType.ArmoryRings => "Armory (Rings)",
            _ => container.ToString()
        };
    }
    
    public void Dispose()
    {
        _discardService.OnDiscardStarted -= Show;
        _discardService.OnDiscardCompleted -= Hide;
        _discardService.OnDiscardCancelled -= Hide;
    }
}

