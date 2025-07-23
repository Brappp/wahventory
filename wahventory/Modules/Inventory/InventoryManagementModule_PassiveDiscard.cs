using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using ECommons.Automation;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using ImGuiNET;
using wahventory.Helpers;
using wahventory.Models;

namespace wahventory.Modules.Inventory;

public partial class InventoryManagementModule
{
    // Passive discard settings - use property to access from config
    private bool _passiveDiscardEnabled => Settings.PassiveDiscard.Enabled;
    private int _passiveDiscardIdleTime => Settings.PassiveDiscard.IdleTimeSeconds;
    
    // Passive discard state
    private DateTime _idleStartTime = DateTime.Now;
    private DateTime _lastAutoDiscardTime = DateTime.MinValue; // Track when we last executed auto-discard
    private bool _wasPlayerBusy = true; // Track previous busy state
    private readonly TimeSpan _passiveDiscardCooldown = TimeSpan.FromMinutes(5); // Cooldown between auto-discards
    private DateTime _lastItemCheckTime = DateTime.MinValue;
    private bool _hasItemsToDiscardCache = false;
    private readonly TimeSpan _itemCheckInterval = TimeSpan.FromSeconds(5); // Check for items every 5 seconds
    private readonly List<uint> _passiveDiscardZones = new()
    {
        // === ARR Cities & Areas (TerritoryIntendedUse#0) ===
        128, // Limsa Lominsa Upper Decks
        129, // Limsa Lominsa Lower Decks
        130, // Ul'dah - Steps of Nald
        131, // Ul'dah - Steps of Thal
        132, // New Gridania
        133, // Old Gridania
        136, // East Shroud (Camp Tranquil)
        144, // The Gold Saucer (TerritoryIntendedUse#23)
        
        // === Heavensward Cities (TerritoryIntendedUse#0) ===
        418, // Foundation
        419, // The Pillars
        478, // Idyllshire
        
        // === Stormblood Cities (TerritoryIntendedUse#0) ===
        628, // Kugane
        635, // Rhalgr's Reach
        
        // === Shadowbringers Cities (TerritoryIntendedUse#0) ===
        819, // The Crystarium
        820, // Eulmore
        
        // === Endwalker Cities (TerritoryIntendedUse#0) ===
        962, // Old Sharlayan
        963, // Radz-at-Han
        
        // === Dawntrail Cities (TerritoryIntendedUse#0) ===
        1185, // Tuliyollal
        
        // === Inn Rooms (TerritoryIntendedUse#2) ===
        177, // Inn Room (Limsa Lominsa)
        179, // Inn Room (Gridania)
        1205, // Inn Room (Tuliyollal)
        
        // === Private Inn Rooms (TerritoryIntendedUse#6) ===
        181, // Private Inn Room (Limsa Lominsa)
        182, // Private Inn Room (Ul'dah)
        183, // Private Inn Room (Gridania)
        
        // === Private Chambers (TerritoryIntendedUse#14) ===
        282, // Private Chambers - Mist
        283, // Private Chambers - Lavender Beds
        284, // Private Chambers - Goblet
        
        // === Housing Districts (TerritoryIntendedUse#13/14) ===
        // Lavender Beds
        340, // The Lavender Beds (Ward)
        342, // The Lavender Beds (Private)
        343, // The Lavender Beds (Private)
        344, // The Lavender Beds (Private)
        
        // The Goblet
        341, // The Goblet (Ward)
        345, // The Goblet (Private)
        346, // The Goblet (Private)
        347, // The Goblet (Private)
        
        // Mist
        384, // Mist (Private Cottage)
        385, // Mist (Private House)
        386, // Mist (Private Mansion)
        
        // === Company Workshops (TerritoryIntendedUse#14) ===
        423, // Company Workshop - Mist
        424, // Company Workshop - Goblet
        425, // Company Workshop - Lavender Beds
        
        // === Grand Company Barracks (TerritoryIntendedUse#30) ===
        534, // Twin Adder Barracks
        535, // Immortal Flames Barracks
        536, // Maelstrom Barracks
        
        // === Special Safe Zones (TerritoryIntendedUse#29) ===
        533, // The Howling Eye (Coerthas)
        705, // Southern Thanalan (Special)
        706, // Central Thanalan (Special)
        710, // Kugane (Special)
        
        // === Gold Saucer Sub-areas (TerritoryIntendedUse#25) ===
        506, // Gold Saucer (Sub-area)
        
        // === Additional Private Areas (TerritoryIntendedUse#14) ===
        573, // Private Estate - Mist
        574, // Private Estate - Lavender Beds
        575, // Private Estate - Goblet
        
        // === Apartment Lobbies (TerritoryIntendedUse#14) ===
        608, // Topmast Apartment (Mist)
        609, // Lily Hills Apartment (Lavender Beds)
        610, // Sultana's Breath Apartment (Goblet)
        
        // === Shirogane Housing (TerritoryIntendedUse#14) ===
        649, // Shirogane
        650, // Shirogane (Subdivision)
        651, // Shirogane (Private)
        652, // Shirogane (Private)
        653, // Shirogane (Private)
        654, // Shirogane (Private)
        655, // Kobai Goten Apartment (Shirogane)
        
        // === Empyreum Housing (TerritoryIntendedUse#14) ===
        980, // Empyreum
        981, // Empyreum (Subdivision)
        982, // Empyreum (Private)
        983, // Empyreum (Private)
        984, // Empyreum (Private)
        985, // Empyreum (Private)
        
        // === Special Private Instances (TerritoryIntendedUse#15) ===
        987, // Private Instance (Old Sharlayan)
        1269, // Private Instance
        
        // === Potentially Missing Zones You Might Want ===
        // Major City/Hub Areas:
        // 156, // Mor Dhona (Revenant's Toll)
        // 418, // Foundation (Ishgard Lower)
        // 419, // The Pillars (Ishgard Upper)
        // 478, // Idyllshire
        // 628, // Kugane
        // 635, // Rhalgr's Reach
        // 759, // The Doman Enclave
        // 819, // The Crystarium
        // 886, // The Firmament
        // 1055, // Unnamed Island (Island Sanctuary)
        // 1186, // Solution Nine (DT hub - if different from 1185)
        
        // Gold Saucer Additional Areas:
        // 388, // Chocobo Square
        // 389, // Round Square  
        // 390, // Event Square
        // 391, // Cactpot Board
        // 792, // The Battlehall
        
        // Additional Inn Rooms by Expansion:
        // 392, // Cloud Nine (Ishgard Inn - HW)
        // 429, // Bokairo Inn (Kugane - SB)
        // 843, // The Pendants (Crystarium - ShB)
        // 844, // ?? (Eulmore Inn - ShB)
        // 990, // The Baldesion Annex (Old Sharlayan Inn - EW)
        // 991, // ?? (Radz-at-Han Inn - EW)
        // 1189, // ?? (Tuliyollal Inn - DT)
        
        // === Additional Major Cities/Hubs ===
        156, // Mor Dhona (Revenant's Toll)
        478, // Idyllshire
        635, // Rhalgr's Reach
        759, // The Doman Enclave
        1186, // Solution Nine
        
        // === Additional Inn Rooms ===
        429, // The Forgotten Knight Inn (Ishgard)
        629, // Bokairo Inn (Kugane)
        843, // The Pendants (Crystarium)
        844, // The Wandering Stairs (Eulmore)
        990, // The Baldesion Annex (Old Sharlayan)
        991, // Meghaduta (Radz-at-Han)
        
        // === Gold Saucer Sub-areas ===
        388, // Chocobo Square
        389, // Round Square  
        390, // Event Square
        391, // Cactpot Board
        792, // The Battlehall
        
        // === Additional Housing/Special Areas ===
        886, // The Firmament (Ishgard Restoration)
        979, // Empyreum Subdivision
        1055, // Unnamed Island (Island Sanctuary)
        
        // === Special Instance Areas ===
        198, 199, 200, 201, // Rising Stones instances
        395, 396, 397, 398, 399, // Ceremony of Eternal Bonding venues
        
        // === Additional Apartments/Private Estates ===
        571, 572, // Additional apartment instances
        576, 577, 578, 579, 580, // More apartment instances
    };
    
    private void UpdatePassiveDiscard()
    {
        if (!_passiveDiscardEnabled)
        {
            return;
        }
        
        if (!_initialized)
        {
            Plugin.Log.Debug("[Passive Discard] Not initialized yet");
            return;
        }
        
        // Check if we have any items to discard first
        if (!HasItemsToDiscard())
        {
            return;
        }
        
        // Check if player is busy
        var isBusy = IsPlayerBusy();
        
        // Track state transitions
        if (isBusy && !_wasPlayerBusy)
        {
            // Player just became busy
            _wasPlayerBusy = true;
        }
        else if (!isBusy && _wasPlayerBusy)
        {
            // Player just became idle
            _wasPlayerBusy = false;
            _idleStartTime = DateTime.Now;
        }
        
        // If player is busy, don't proceed
        if (isBusy)
        {
            return;
        }
        
        // Check idle time
        var idleDuration = DateTime.Now - _idleStartTime;
        if (idleDuration.TotalSeconds < _passiveDiscardIdleTime)
        {
            return;
        }
        
        // Check zone restrictions
        if (!IsInAllowedZone())
        {
            return;
        }
        
        // Check cooldown
        if (_lastAutoDiscardTime != DateTime.MinValue)
        {
            var timeSinceLastDiscard = DateTime.Now - _lastAutoDiscardTime;
            if (timeSinceLastDiscard < _passiveDiscardCooldown)
            {
                // Still in cooldown period
                return;
            }
        }
        
        // Execute the auto-discard command
        Plugin.Log.Information("[Passive Discard] Idle time reached, executing auto-discard");
        ExecuteAutoDiscard();
        
        // Update the last discard time
        _lastAutoDiscardTime = DateTime.Now;
    }
    

    
    private bool IsPlayerBusy()
    {
        // Check various conditions that indicate the player is busy
        var condition = Plugin.Condition;
        
        // Player is in combat
        if (condition[ConditionFlag.InCombat])
            return true;
        
        // Player is in a cutscene
        if (condition[ConditionFlag.OccupiedInCutSceneEvent] || 
            condition[ConditionFlag.WatchingCutscene] ||
            condition[ConditionFlag.WatchingCutscene78])
            return true;
        
        // Player is crafting or gathering
        if (condition[ConditionFlag.Crafting] || 
            condition[ConditionFlag.Gathering] ||
            condition[ConditionFlag.Fishing])
            return true;
        
        // Player is in a duty
        if (condition[ConditionFlag.BoundByDuty] || 
            condition[ConditionFlag.BoundByDuty56] ||
            condition[ConditionFlag.BoundByDuty95])
            return true;
        
        // Player is trading
        if (condition[ConditionFlag.TradeOpen])
            return true;
        
        // Player is occupied with UI
        if (condition[ConditionFlag.OccupiedInQuestEvent] ||
            condition[ConditionFlag.OccupiedSummoningBell])
            return true;
        
        // Player is between areas
        if (condition[ConditionFlag.BetweenAreas] ||
            condition[ConditionFlag.BetweenAreas51])
            return true;
        
        // Check if any game UI windows are open
        unsafe
        {
            // Check if inventory is open
            var inventoryManager = InventoryManager.Instance();
            if (inventoryManager == null)
                return true;
            
            // Check common UI windows
            var uiModule = UIModule.Instance();
            if (uiModule == null)
                return true;
            
            // Check if retainer window is open
            if (uiModule->IsMainCommandUnlocked(72)) // Retainer bell
            {
                var retainerList = Plugin.GameGui.GetAddonByName("RetainerList", 1);
                if (retainerList != IntPtr.Zero)
                    return true;
            }
            
            // Check if market board is open
            var itemSearch = Plugin.GameGui.GetAddonByName("ItemSearch", 1);
            if (itemSearch != IntPtr.Zero)
                return true;
        }
        
        return false;
    }
    
    private bool IsInAllowedZone()
    {
        var territory = Plugin.ClientState.TerritoryType;
        
        // Check if we're in one of the allowed safe zones
        return _passiveDiscardZones.Contains(territory);
    }
    
    private bool HasItemsToDiscard()
    {
        // Use cached value if it's recent
        if (_lastItemCheckTime != DateTime.MinValue && 
            DateTime.Now - _lastItemCheckTime < _itemCheckInterval)
        {
            return _hasItemsToDiscardCache;
        }
        
        // Refresh inventory to get current items
        RefreshInventory();
        
        lock (_itemsLock)
        {
            // Check if there are any items in the auto-discard list
            _hasItemsToDiscardCache = _originalItems.Any(item => 
                AutoDiscardItems.Contains(item.ItemId) && 
                InventoryHelpers.IsSafeToDiscard(item, BlacklistedItems));
            _lastItemCheckTime = DateTime.Now;
            return _hasItemsToDiscardCache;
        }
    }
    
    // Settings methods
    public void SetPassiveDiscardEnabled(bool enabled)
    {
        Settings.PassiveDiscard.Enabled = enabled;
        _plugin.ConfigManager.SaveConfiguration();
    }
    
    public void SetPassiveDiscardIdleTime(int seconds)
    {
        Settings.PassiveDiscard.IdleTimeSeconds = Math.Max(10, Math.Min(300, seconds)); // 10s to 5min
        _plugin.ConfigManager.SaveConfiguration();
    }
    

    
    private void DrawPassiveDiscardSettings()
    {
        // Header
        using (var font = ImRaii.PushFont(UiBuilder.IconFont))
        {
            ImGui.Text(FontAwesomeIcon.Robot.ToIconString());
        }
        ImGui.SameLine();
        ImGui.Text("Passive Discard Settings");
        
        ImGui.TextWrapped("Passive discard will automatically discard items from your auto-discard list when you are idle.");
        ImGui.Spacing();
        
        // Enable checkbox
        var enabled = _passiveDiscardEnabled;
        if (ImGui.Checkbox("Enable Passive Discard", ref enabled))
        {
            SetPassiveDiscardEnabled(enabled);
        }
        

        
        using (var disabled = ImRaii.Disabled(!_passiveDiscardEnabled))
        {
            ImGui.Spacing();
            
            // Idle time setting
            ImGui.Text("Idle Time Required:");
            ImGui.SameLine();
            ImGui.SetNextItemWidth(100);
            var idleTime = _passiveDiscardIdleTime;
            if (ImGui.InputInt("##IdleTime", ref idleTime, 5, 10))
            {
                SetPassiveDiscardIdleTime(idleTime);
            }
            ImGui.SameLine();
            ImGui.Text("seconds");
            
            ImGui.Spacing();
            ImGui.Text("Zone Restrictions:");
            ImGui.TextWrapped("Passive discard only works in safe zones: Cities, Housing Areas, Inn Rooms, Barracks, Gold Saucer, and other non-combat areas.");
            
            // Status display
            ImGui.Spacing();
            ImGui.Text("Status:");
            ImGui.SameLine();
            
            if (!_passiveDiscardEnabled)
            {
                ImGui.TextColored(ColorSubdued, "Disabled");
            }
            else if (!HasItemsToDiscard())
            {
                ImGui.TextColored(ColorSubdued, "No items to discard");
            }
            else if (IsPlayerBusy())
            {
                ImGui.TextColored(ColorWarning, "Player Busy");
            }
            else if (!IsInAllowedZone())
            {
                ImGui.TextColored(ColorWarning, "Not in Allowed Zone");
            }
            else
            {
                var idleDuration = DateTime.Now - _idleStartTime;
                if (idleDuration.TotalSeconds < _passiveDiscardIdleTime)
                {
                    ImGui.TextColored(ColorInfo, $"Waiting for idle ({(int)idleDuration.TotalSeconds}/{_passiveDiscardIdleTime}s)");
                }
                else if (_lastAutoDiscardTime != DateTime.MinValue)
                {
                    var timeSinceLastDiscard = DateTime.Now - _lastAutoDiscardTime;
                    if (timeSinceLastDiscard < _passiveDiscardCooldown)
                    {
                        var remainingCooldown = _passiveDiscardCooldown - timeSinceLastDiscard;
                        ImGui.TextColored(ColorSubdued, $"Cooldown ({(int)remainingCooldown.TotalSeconds}s remaining)");
                    }
                    else
                    {
                        ImGui.TextColored(ColorSuccess, "Ready to execute auto-discard");
                    }
                }
                else
                {
                    ImGui.TextColored(ColorSuccess, "Ready to execute auto-discard");
                }
            }
            
            // Debug info
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Text("Debug Info:");
            ImGui.Text($"Current Territory ID: {Plugin.ClientState.TerritoryType}");
            
            if (ImGui.Button("Copy Territory ID"))
            {
                ImGui.SetClipboardText(Plugin.ClientState.TerritoryType.ToString());
            }
            ImGui.SameLine();
            if (ImGui.Button("Log Territory Info"))
            {
                var territory = Plugin.ClientState.TerritoryType;
                var isInList = _passiveDiscardZones.Contains(territory);
                Plugin.Log.Information($"[Passive Discard] Current Territory: {territory}, In Safe Zone List: {isInList}");
                Plugin.ChatGui.Print($"Territory ID: {territory} - In Safe Zone List: {isInList}");
            }
            
            ImGui.SameLine();
            if (ImGui.Button("Export Zone List"))
            {
                var sortedZones = _passiveDiscardZones.OrderBy(z => z).ToList();
                var zoneList = string.Join(", ", sortedZones);
                ImGui.SetClipboardText(zoneList);
                Plugin.ChatGui.Print($"Copied {sortedZones.Count} zone IDs to clipboard");
            }
        }
    }
} 