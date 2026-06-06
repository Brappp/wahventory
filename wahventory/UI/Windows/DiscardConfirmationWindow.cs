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

    public DiscardConfirmationWindow(
        DiscardService discardService,
        IconCache iconCache)
        : base("Confirm discard##DiscardConfirmation", ImGuiWindowFlags.NoCollapse)
    {
        Size = new Vector2(560, 420);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(440, 280),
            MaximumSize = new Vector2(1200, 900),
        };

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

        using var styles = ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(8, 6));

        DrawHeader();

        ImGui.Spacing();

        var spacing = ImGui.GetStyle().ItemSpacing.Y;
        const float buttonHeight = 28f;
        const float progressHeight = 20f;

        var reservedBottom = buttonHeight + spacing + progressHeight + spacing;
        var hasError = !string.IsNullOrEmpty(_discardService.DiscardError);
        if (hasError) reservedBottom += ImGui.GetTextLineHeight() + spacing;
        var tableHeight = ImGui.GetContentRegionAvail().Y - reservedBottom;
        if (tableHeight < 100) tableHeight = 100;

        using (var color = ImRaii.PushColor(ImGuiCol.ChildBg, new Vector4(0.12f, 0.12f, 0.12f, 1f)))
        {
            using (ImRaii.Child("ItemTable", new Vector2(0, tableHeight), true))
            {
                DrawItemsTable();
            }
        }

        if (hasError)
        {
            ImGui.TextColored(Theme.ColorError, _discardService.DiscardError);
        }

        if (_discardService.DiscardProgress > 0)
        {
            var progress = (float)_discardService.DiscardProgress / _discardService.TotalItems;
            using (var color = ImRaii.PushColor(ImGuiCol.PlotHistogram, Theme.ColorSuccess))
            {
                ImGui.ProgressBar(progress, new Vector2(-1, progressHeight),
                    $"Discarding {_discardService.DiscardProgress} / {_discardService.TotalItems}");
            }
        }
        else
        {
            ImGui.Dummy(new Vector2(0, progressHeight));
        }

        ImGui.Spacing();
        DrawButtons();
    }

    private void DrawHeader()
    {
        var items = _discardService.ItemsToDiscard;
        var totalItems = items.Count;
        var totalQuantity = items.Sum(i => i.Quantity);
        var totalValue = items.Where(i => i.MarketPrice.HasValue).Sum(i => i.MarketPrice!.Value * i.Quantity);

        using (ImRaii.PushFont(UiBuilder.IconFont))
        {
            ImGui.TextColored(Theme.ColorWarning, FontAwesomeIcon.TrashAlt.ToIconString());
        }
        ImGui.SameLine();
        ImGui.AlignTextToFramePadding();
        ImGui.Text($"Discard {totalQuantity:N0} item{(totalQuantity == 1 ? "" : "s")}");
        ImGui.SameLine();
        ImGui.TextColored(Theme.ColorSubdued, $"· {totalItems} unique");
        if (totalValue > 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(Theme.ColorSubdued, "·");
            ImGui.SameLine();
            ImGui.TextColored(Theme.ColorPrice, $"{totalValue:N0} gil");
        }

        using (ImRaii.PushColor(ImGuiCol.Text, Theme.ColorSubdued))
        {
            ImGui.TextWrapped("This cannot be undone.");
        }
    }
    
    private void DrawItemsTable()
    {
        using var style = ImRaii.PushStyle(ImGuiStyleVar.CellPadding, new Vector2(6, 3));

        using (var table = ImRaii.Table("DiscardTable", 4,
            ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingStretchProp))
        {
            if (table)
            {
                ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
                ImGui.TableSetupColumn("Qty", ImGuiTableColumnFlags.WidthFixed, 60);
                ImGui.TableSetupColumn("Location", ImGuiTableColumnFlags.WidthFixed, 130);
                ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthFixed, 100);
                ImGui.TableSetupScrollFreeze(0, 1);
                ImGui.TableHeadersRow();

                foreach (var item in _discardService.ItemsToDiscard)
                {
                    ImGui.TableNextRow();

                    ImGui.TableNextColumn();
                    if (item.IconId > 0)
                    {
                        var icon = _iconCache.GetIcon(item.IconId);
                        if (icon != null)
                        {
                            ImGui.Image(icon.Handle, new Vector2(18, 18));
                            ImGui.SameLine(0, 6);
                        }
                    }
                    ImGui.Text(item.Name);
                    if (item.IsHQ)
                    {
                        ImGui.SameLine();
                        ImGui.TextColored(Theme.ColorHQItem, "[HQ]");
                    }

                    ImGui.TableNextColumn();
                    ImGui.Text(item.Quantity.ToString("N0"));

                    ImGui.TableNextColumn();
                    ImGui.TextColored(Theme.ColorSubdued, GetLocationName(item.Container));

                    ImGui.TableNextColumn();
                    if (item.MarketPrice.HasValue && item.MarketPrice.Value > 0)
                    {
                        ImGui.TextColored(Theme.ColorPrice, $"{item.MarketPrice.Value * item.Quantity:N0}");
                    }
                    else
                    {
                        ImGui.TextColored(Theme.ColorSubdued, "—");
                    }
                }
            }
        }
    }
    
    private void DrawButtons()
    {
        var buttonHeight = 28f;
        var primaryWidth = 140f;
        var cancelWidth = 100f;
        var spacing = ImGui.GetStyle().ItemSpacing.X;

        var rowWidth = primaryWidth + cancelWidth + spacing;
        var avail = ImGui.GetContentRegionAvail().X;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + Math.Max(0, avail - rowWidth));

        if (_discardService.DiscardProgress == 0)
        {
            using (ImRaii.PushColor(ImGuiCol.Button, new Vector4(0, 0, 0, 0))
                                  .Push(ImGuiCol.ButtonHovered, new Vector4(0.3f, 0.3f, 0.3f, 0.5f))
                                  .Push(ImGuiCol.Text, Theme.ColorSubdued))
            {
                if (ImGui.Button("Cancel", new Vector2(cancelWidth, buttonHeight)))
                {
                    _discardService.CancelDiscard();
                    Hide();
                }
            }

            ImGui.SameLine();

            using (ImRaii.PushColor(ImGuiCol.Button, new Vector4(0.541f, 0.227f, 0.227f, 1f))
                                  .Push(ImGuiCol.ButtonHovered, new Vector4(0.641f, 0.327f, 0.327f, 1f)))
            {
                if (ImGui.Button("Discard items", new Vector2(primaryWidth, buttonHeight)))
                {
                    _discardService.StartDiscarding();
                }
            }
        }
        else
        {
            using (ImRaii.Disabled())
            {
                ImGui.Button("Discarding…", new Vector2(cancelWidth, buttonHeight));
            }

            ImGui.SameLine();

            using (ImRaii.PushColor(ImGuiCol.Button, new Vector4(0.541f, 0.227f, 0.227f, 1f))
                                  .Push(ImGuiCol.ButtonHovered, new Vector4(0.641f, 0.327f, 0.327f, 1f)))
            {
                if (ImGui.Button("Stop", new Vector2(primaryWidth, buttonHeight)))
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

