using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Dalamud.Bindings.ImGui;
using Lumina.Excel.Sheets;
using wahventory.Services.Helpers;
using wahventory.Models;
using wahventory.Core;

namespace wahventory.Modules.Inventory;

/// <summary>
/// Auto-discard management functionality for InventoryManagementModule
/// </summary>
public partial class InventoryManagementModule
{
    private string _autoDiscardItemNameToAdd = string.Empty;
    private uint _autoDiscardItemToAdd = 0;
    private List<(uint Id, string Name, ushort Icon)> _autoDiscardSearchResults = new();
    
    private bool _autoDiscardSearchingItems = false;
    private DateTime _autoDiscardLastSearchTime = DateTime.MinValue;
    private readonly TimeSpan _autoDiscardSearchDelay = TimeSpan.FromMilliseconds(300);
    
    private void DrawAutoDiscardTabContent()
    {
        var passiveDiscardOpen = ImGui.CollapsingHeader("Passive Discard Settings", ImGuiTreeNodeFlags.DefaultOpen);
        if (passiveDiscardOpen)
        {
            ImGui.Indent();
            DrawPassiveDiscardSettings();
            ImGui.Unindent();
            ImGui.Spacing();
        }
        
        ImGui.Separator();
        ImGui.Spacing();
        ImGui.TextWrapped("Manage your auto-discard list. Items added here will be automatically discarded when using the /wahventory auto command.");
        ImGui.TextWrapped("WARNING: This is a powerful feature. Only add items you are absolutely certain you want to discard automatically!");
        ImGui.Spacing();
        
        DrawAddToAutoDiscardSection();
        
        ImGui.Separator();
        ImGui.Spacing();
        
        DrawCurrentAutoDiscardList();
    }
    
    private void DrawPassiveDiscardSettings()
    {
        var settings = Settings.PassiveDiscard;
        bool changed = false;
        
        using (var color = ImRaii.PushColor(ImGuiCol.ChildBg, new Vector4(0.145f, 0.145f, 0.145f, 1f)))
        {
            using (var child = ImRaii.Child("PassiveSettings", new Vector2(0, 200), true))
            {
                using (var font = ImRaii.PushFont(UiBuilder.IconFont))
                {
                    ImGui.TextColored(ColorWarning, FontAwesomeIcon.ExclamationTriangle.ToIconString());
                }
                ImGui.SameLine();
                ImGui.Text("Passive Discard");
                ImGui.Spacing();
                
                ImGui.TextWrapped("Passive discard automatically runs auto-discard when you've been idle in a safe zone.");
                ImGui.Spacing();
                
                var enabled = settings.Enabled;
                if (ImGui.Checkbox("Enable Passive Discard", ref enabled))
                {
                    settings.Enabled = enabled;
                    _taskCoordinator.UpdatePassiveDiscardSettings(enabled);
                    changed = true;
                }
                
                if (settings.Enabled)
                {
                    ImGui.Spacing();
                    
                    var idleTime = settings.IdleTimeSeconds;
                    ImGui.Text("Idle Time (seconds):");
                    ImGui.SetNextItemWidth(150);
                    if (ImGui.SliderInt("##IdleTime", ref idleTime, 30, 600, $"{idleTime}s"))
                    {
                        settings.IdleTimeSeconds = idleTime;
                        changed = true;
                    }
                    
                    ImGui.Spacing();
                    
                    // Status display
                    var status = GetPassiveDiscardStatus();
                    var statusColor = GetPassiveDiscardStatusColor(status);
                    
                    ImGui.Text("Status: ");
                    ImGui.SameLine();
                    ImGui.TextColored(statusColor, status);
                    
                    if (status.Contains("Ready"))
                    {
                        ImGui.SameLine();
                        using (var color2 = ImRaii.PushColor(ImGuiCol.Text, ColorWarning))
                        {
                            ImGui.Text("⚠");
                        }
                        if (ImGui.IsItemHovered())
                        {
                            ImGui.SetTooltip("Passive discard is ready to execute. It will run automatically when conditions are met.");
                        }
                    }
                }
            }
        }
        
        if (changed)
        {
            _plugin.ConfigManager.SaveConfiguration();
        }
    }
    
    private string GetPassiveDiscardStatus()
    {
        if (!Settings.PassiveDiscard.Enabled)
            return "Disabled";
        
        var idleDuration = _taskCoordinator.PassiveTasks.GetIdleDuration();
        var remainingCooldown = _taskCoordinator.PassiveTasks.GetRemainingCooldown();
        var isInAllowedZone = _taskCoordinator.PassiveTasks.IsCurrentZoneAllowed();
        
        if (!isInAllowedZone)
            return "Not in Safe Zone";
        
        if (remainingCooldown.HasValue && remainingCooldown.Value > TimeSpan.Zero)
            return $"Cooldown ({(int)remainingCooldown.Value.TotalSeconds}s)";
        
        if (idleDuration.TotalSeconds < Settings.PassiveDiscard.IdleTimeSeconds)
            return $"Waiting for Idle ({(int)idleDuration.TotalSeconds}/{Settings.PassiveDiscard.IdleTimeSeconds}s)";
        
        return "Ready to Execute";
    }
    
    private Vector4 GetPassiveDiscardStatusColor(string status)
    {
        return status switch
        {
            "Disabled" => ColorSubdued,
            "Not in Safe Zone" => ColorSubdued,
            "Ready to Execute" => ColorWarning,
            _ when status.Contains("Cooldown") => ColorInfo,
            _ when status.Contains("Waiting") => ColorCaution,
            _ => ImGui.GetStyle().Colors[(int)ImGuiCol.Text]
        };
    }
    
    private void DrawAddToAutoDiscardSection()
    {
        ImGui.Text("Add New Item to Auto-Discard:");
        ImGui.Spacing();
        
        ImGui.SetNextItemWidth(300);
        if (ImGui.InputTextWithHint("##AddAutoDiscardItemName", "Search item name...", ref _autoDiscardItemNameToAdd, 100))
        {
            _autoDiscardLastSearchTime = DateTime.Now;
            _autoDiscardSearchingItems = true;
        }
        
        ImGui.SameLine();
        if (ImGui.Button("Add by ID##AutoDiscard") && _autoDiscardItemToAdd > 0)
        {
            AddItemToAutoDiscard(_autoDiscardItemToAdd);
            _autoDiscardItemToAdd = 0;
        }
        
        ImGui.SameLine();
        ImGui.SetNextItemWidth(100);
        int tempId = (int)_autoDiscardItemToAdd;
        if (ImGui.InputInt("##AutoDiscardItemId", ref tempId, 0, 0))
        {
            _autoDiscardItemToAdd = (uint)Math.Max(0, tempId);
        }
        
        // Handle item search
        if (_autoDiscardSearchingItems && DateTime.Now - _autoDiscardLastSearchTime > _autoDiscardSearchDelay)
        {
            _autoDiscardSearchingItems = false;
            _autoDiscardSearchResults.Clear();
            
            if (!string.IsNullOrWhiteSpace(_autoDiscardItemNameToAdd))
            {
                SearchAutoDiscardItems(_autoDiscardItemNameToAdd);
            }
        }
        
        DrawAutoDiscardSearchResults();
    }
    
    private void SearchAutoDiscardItems(string searchTerm)
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
            
            _autoDiscardSearchResults = results;
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Failed to search auto-discard items");
        }
    }
    
    private void DrawAutoDiscardSearchResults()
    {
        if (_autoDiscardSearchResults.Count > 0)
        {
            ImGui.Text("Search Results:");
            using var child = ImRaii.Child("AutoDiscardSearchResults", new Vector2(0, 150), true);
            
            foreach (var (id, name, iconId) in _autoDiscardSearchResults)
            {
                using var itemId = ImRaii.PushId((int)id);
                
                var icon = _iconCache.GetIcon(iconId);
                if (icon != null)
                {
                    ImGui.Image(icon.Handle, new Vector2(20, 20));
                    ImGui.SameLine();
                }
                
                if (ImGui.Selectable($"{name} (ID: {id})###{id}_autodiscard_search"))
                {
                    AddItemToAutoDiscard(id);
                    _autoDiscardItemNameToAdd = string.Empty;
                    _autoDiscardSearchResults.Clear();
                }
            }
        }
    }
    
    private void DrawCurrentAutoDiscardList()
    {
        ImGui.Text($"Current Auto-Discard List ({_taskCoordinator.AutoDiscardItems.Count} items):");
        ImGui.Spacing();
        
        if (!_taskCoordinator.AutoDiscardItems.Any())
        {
            ImGui.TextColored(ColorSubdued, "No items in auto-discard list");
            return;
        }
        
        using var child = ImRaii.Child("AutoDiscardItems", new Vector2(0, 300), true);
        using var table = ImRaii.Table("AutoDiscardTable", 4, 
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable);
        
        if (table)
        {
            ImGui.TableSetupColumn("ID", ImGuiTableColumnFlags.WidthFixed, 60);
            ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Category", ImGuiTableColumnFlags.WidthFixed, 120);
            ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthFixed, 80);
            ImGui.TableHeadersRow();
            
            var itemsToRemove = new List<uint>();
            
            foreach (var itemId in _taskCoordinator.AutoDiscardItems.ToList())
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
                _taskCoordinator.AutoDiscardItems.Remove(itemId);
            }
            
            if (itemsToRemove.Any())
            {
                SaveAutoDiscard();
                _taskCoordinator.RefreshAll(_showArmory, _searchFilter);
            }
        }
    }
    
    private void AddItemToAutoDiscard(uint itemId)
    {
        if (!_taskCoordinator.AutoDiscardItems.Contains(itemId))
        {
            _taskCoordinator.AutoDiscardItems.Add(itemId);
            SaveAutoDiscard();
            _taskCoordinator.RefreshAll(_showArmory, _searchFilter);
            Plugin.Log.Information($"Added item {itemId} to auto-discard list");
        }
    }
}