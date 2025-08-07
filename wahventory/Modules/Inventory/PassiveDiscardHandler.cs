using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using ECommons.Automation;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using Dalamud.Bindings.ImGui;
using wahventory.Services.Helpers;
using wahventory.Models;
using wahventory.Core;

namespace wahventory.Modules.Inventory;

public partial class InventoryManagementModule
{
    private bool _passiveDiscardEnabled => Settings.PassiveDiscard.Enabled;
    private int _passiveDiscardIdleTime => Settings.PassiveDiscard.IdleTimeSeconds;
    private DateTime _idleStartTime = DateTime.Now;
    private DateTime _lastAutoDiscardTime = DateTime.MinValue; // Track when we last executed auto-discard
    private bool _wasPlayerBusy = true; // Track previous busy state
    private readonly TimeSpan _passiveDiscardCooldown = TimeSpan.FromMinutes(5); // Cooldown between auto-discards
    private DateTime _lastItemCheckTime = DateTime.MinValue;
    private bool _hasItemsToDiscardCache = false;
    private readonly TimeSpan _itemCheckInterval = TimeSpan.FromSeconds(5); // Check for items every 5 seconds
    private readonly List<uint> _passiveDiscardZones = new()
    {
        128, // Limsa Lominsa Upper Decks
        129, // Limsa Lominsa Lower Decks
        130, // Ul'dah - Steps of Nald
        131, // Ul'dah - Steps of Thal
        132, // New Gridania
        133, // Old Gridania
        136, // East Shroud (Camp Tranquil)
        144, // The Gold Saucer (TerritoryIntendedUse#23)
        418, // Foundation
        419, // The Pillars
        478, // Idyllshire
        628, // Kugane
        635, // Rhalgr's Reach
        819, // The Crystarium
        820, // Eulmore
        962, // Old Sharlayan
        963, // Radz-at-Han
        1185, // Tuliyollal
        177, // Inn Room (Limsa Lominsa)
        179, // Inn Room (Gridania)
        1205, // Inn Room (Tuliyollal)
        181, // Private Inn Room (Limsa Lominsa)
        182, // Private Inn Room (Ul'dah)
        183, // Private Inn Room (Gridania)
        282, // Private Chambers - Mist
        283, // Private Chambers - Lavender Beds
        284, // Private Chambers - Goblet
        340, // The Lavender Beds (Ward)
        342, // The Lavender Beds (Private)
        343, // The Lavender Beds (Private)
        344, // The Lavender Beds (Private)
        341, // The Goblet (Ward)
        345, // The Goblet (Private)
        346, // The Goblet (Private)
        347, // The Goblet (Private)
        384, // Mist (Private Cottage)
        385, // Mist (Private House)
        386, // Mist (Private Mansion)
        423, // Company Workshop - Mist
        424, // Company Workshop - Goblet
        425, // Company Workshop - Lavender Beds
        534, // Twin Adder Barracks
        535, // Immortal Flames Barracks
        536, // Maelstrom Barracks
        533, // The Howling Eye (Coerthas)
        705, // Southern Thanalan (Special)
        706, // Central Thanalan (Special)
        710, // Kugane (Special)
        506, // Gold Saucer (Sub-area)
        573, // Private Estate - Mist
        574, // Private Estate - Lavender Beds
        575, // Private Estate - Goblet
        608, // Topmast Apartment (Mist)
        609, // Lily Hills Apartment (Lavender Beds)
        610, // Sultana's Breath Apartment (Goblet)
        641, // Shirogane (Main Ward)
        642, // Shirogane (Subdivision)
        643, // Shirogane (Private)
        644, // Shirogane (Private)
        645, // Shirogane (Private)
        646, // Shirogane (Private)
        647, // Shirogane (Private)
        648, // Shirogane (Private)
        649, // Shirogane
        650, // Shirogane (Subdivision)
        651, // Shirogane (Private)
        652, // Shirogane (Private)
        653, // Shirogane (Private)
        654, // Shirogane (Private)
        655, // Kobai Goten Apartment (Shirogane)
        980, // Empyreum
        981, // Empyreum (Subdivision)
        982, // Empyreum (Private)
        983, // Empyreum (Private)
        984, // Empyreum (Private)
        985, // Empyreum (Private)
        987, // Private Instance (Old Sharlayan)
        1269, // Private Instance
        156, // Mor Dhona (Revenant's Toll)
        478, // Idyllshire
        635, // Rhalgr's Reach
        759, // The Doman Enclave
        1186, // Solution Nine
        429, // The Forgotten Knight Inn (Ishgard)
        629, // Bokairo Inn (Kugane)
        843, // The Pendants (Crystarium)
        844, // The Wandering Stairs (Eulmore)
        990, // The Baldesion Annex (Old Sharlayan)
        991, // Meghaduta (Radz-at-Han)
        388, // Chocobo Square
        389, // Round Square  
        390, // Event Square
        391, // Cactpot Board
        792, // The Battlehall
        886, // The Firmament (Ishgard Restoration)
        979, // Empyreum Subdivision
        1055, // Unnamed Island (Island Sanctuary)
        198, 199, 200, 201, // Rising Stones instances
        395, 396, 397, 398, 399, // Ceremony of Eternal Bonding venues
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
        if (!HasItemsToDiscard())
        {
            return;
        }
        var isBusy = IsPlayerBusy();
        if (isBusy && !_wasPlayerBusy)
        {
            _wasPlayerBusy = true;
        }
        else if (!isBusy && _wasPlayerBusy)
        {
            _wasPlayerBusy = false;
            _idleStartTime = DateTime.Now;
        }
        if (isBusy)
        {
            return;
        }
        var idleDuration = DateTime.Now - _idleStartTime;
        if (idleDuration.TotalSeconds < _passiveDiscardIdleTime)
        {
            return;
        }
        if (!IsInAllowedZone())
        {
            return;
        }
        if (_lastAutoDiscardTime != DateTime.MinValue)
        {
            var timeSinceLastDiscard = DateTime.Now - _lastAutoDiscardTime;
            if (timeSinceLastDiscard < _passiveDiscardCooldown)
            {
                return;
            }
        }
        Plugin.Log.Information("[Passive Discard] Idle time reached, executing auto-discard");
        ExecuteAutoDiscard();
        _lastAutoDiscardTime = DateTime.Now;
    }
    

    
    private bool IsPlayerBusy()
    {
        var condition = Plugin.Condition;
        if (condition[ConditionFlag.InCombat])
            return true;
        if (condition[ConditionFlag.OccupiedInCutSceneEvent] || 
            condition[ConditionFlag.WatchingCutscene] ||
            condition[ConditionFlag.WatchingCutscene78])
            return true;
        if (condition[ConditionFlag.Crafting] || 
            condition[ConditionFlag.Gathering] ||
            condition[ConditionFlag.Fishing])
            return true;
        if (condition[ConditionFlag.BoundByDuty] || 
            condition[ConditionFlag.BoundByDuty56] ||
            condition[ConditionFlag.BoundByDuty95])
            return true;
        if (condition[ConditionFlag.TradeOpen])
            return true;
        if (condition[ConditionFlag.OccupiedInQuestEvent] ||
            condition[ConditionFlag.OccupiedSummoningBell])
            return true;
        if (condition[ConditionFlag.BetweenAreas] ||
            condition[ConditionFlag.BetweenAreas51])
            return true;
        unsafe
        {
            var inventoryManager = InventoryManager.Instance();
            if (inventoryManager == null)
                return true;
            var uiModule = UIModule.Instance();
            if (uiModule == null)
                return true;
            if (uiModule->IsMainCommandUnlocked(72)) // Retainer bell
            {
                var retainerList = Plugin.GameGui.GetAddonByName("RetainerList", 1);
                if (retainerList != IntPtr.Zero)
                    return true;
            }
            var itemSearch = Plugin.GameGui.GetAddonByName("ItemSearch", 1);
            if (itemSearch != IntPtr.Zero)
                return true;
        }
        
        return false;
    }
    
    private bool IsInAllowedZone()
    {
        var territory = Plugin.ClientState.TerritoryType;
        return _passiveDiscardZones.Contains(territory);
    }
    
    private bool HasItemsToDiscard()
    {
        if (_lastItemCheckTime != DateTime.MinValue && 
            DateTime.Now - _lastItemCheckTime < _itemCheckInterval)
        {
            return _hasItemsToDiscardCache;
        }
        RefreshInventory();
        
        lock (_itemsLock)
        {
            _hasItemsToDiscardCache = _originalItems.Any(item => 
                AutoDiscardItems.Contains(item.ItemId) && 
                InventoryHelpers.IsSafeToDiscard(item, BlacklistedItems));
            _lastItemCheckTime = DateTime.Now;
            return _hasItemsToDiscardCache;
        }
    }
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
        using (var font = ImRaii.PushFont(UiBuilder.IconFont))
        {
            ImGui.Text(FontAwesomeIcon.Robot.ToIconString());
        }
        ImGui.SameLine();
        ImGui.Text("Passive Discard Settings");
        
        ImGui.TextWrapped("Passive discard will automatically discard items from your auto-discard list when you are idle.");
        ImGui.Spacing();
        var enabled = _passiveDiscardEnabled;
        if (ImGui.Checkbox("Enable Passive Discard", ref enabled))
        {
            SetPassiveDiscardEnabled(enabled);
        }
        

        
        using (var disabled = ImRaii.Disabled(!_passiveDiscardEnabled))
        {
            ImGui.Spacing();
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
