using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI;
using wahventory.Core;
using wahventory.Models;
using wahventory.Services.Helpers;

namespace wahventory.Services;

public class PassiveDiscardService
{
    private readonly IClientState _clientState;
    private readonly ICondition _condition;
    private readonly IGameGui _gameGui;
    private readonly IPluginLog _log;
    private readonly InventorySettings _settings;
    
    private DateTime _idleStartTime = DateTime.Now;
    private DateTime _lastAutoDiscardTime = DateTime.MinValue;
    private bool _wasPlayerBusy = true;
    private readonly TimeSpan _passiveDiscardCooldown = TimeSpan.FromMinutes(5);
    private DateTime _lastItemCheckTime = DateTime.MinValue;
    private bool _hasItemsToDiscardCache = false;
    private readonly TimeSpan _itemCheckInterval = TimeSpan.FromSeconds(5);
    
    private readonly HashSet<uint> _passiveDiscardZones = new()
    {
        128, 129, 130, 131, 132, 133, 136, 144, 418, 419, 478, 628, 635, 819, 820, 962, 963, 1185,
        177, 179, 1205, 181, 182, 183, 282, 283, 284, 340, 341, 342, 343, 344, 345, 346, 347,
        384, 385, 386, 423, 424, 425, 534, 535, 536, 533, 705, 706, 710, 506, 573, 574, 575,
        608, 609, 610, 641, 642, 643, 644, 645, 646, 647, 648, 649, 650, 651, 652, 653, 654, 655,
        980, 981, 982, 983, 984, 985, 987, 1269, 156, 759, 1186, 429, 629, 843, 844, 990, 991,
        388, 389, 390, 391, 792, 886, 979, 1055, 198, 199, 200, 201, 395, 396, 397, 398, 399,
        571, 572, 576, 577, 578, 579, 580
    };
    
    public PassiveDiscardService(
        IClientState clientState,
        ICondition condition,
        IGameGui gameGui,
        IPluginLog log,
        InventorySettings settings)
    {
        _clientState = clientState;
        _condition = condition;
        _gameGui = gameGui;
        _log = log;
        _settings = settings;
    }
    
    public void Update(
        HashSet<uint> autoDiscardItems,
        IEnumerable<InventoryItemInfo> allItems,
        HashSet<uint> blacklistedItems,
        Action executeAutoDiscard)
    {
        if (!_settings.PassiveDiscard.Enabled)
            return;
        
        if (!HasItemsToDiscard(allItems, autoDiscardItems, blacklistedItems))
            return;
        
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
            return;
        
        var idleDuration = DateTime.Now - _idleStartTime;
        if (idleDuration.TotalSeconds < _settings.PassiveDiscard.IdleTimeSeconds)
            return;
        
        if (!IsInAllowedZone())
            return;
        
        if (_lastAutoDiscardTime != DateTime.MinValue)
        {
            var timeSinceLastDiscard = DateTime.Now - _lastAutoDiscardTime;
            if (timeSinceLastDiscard < _passiveDiscardCooldown)
                return;
        }
        
        _log.Information("[Passive Discard] Idle time reached, executing auto-discard");
        executeAutoDiscard();
        _lastAutoDiscardTime = DateTime.Now;
    }
    
    public bool IsPlayerBusy()
    {
        if (_condition[ConditionFlag.InCombat])
            return true;
        
        if (_condition[ConditionFlag.OccupiedInCutSceneEvent] || 
            _condition[ConditionFlag.WatchingCutscene] ||
            _condition[ConditionFlag.WatchingCutscene78])
            return true;
        
        if (_condition[ConditionFlag.Crafting] || 
            _condition[ConditionFlag.Gathering] ||
            _condition[ConditionFlag.Fishing])
            return true;
        
        if (_condition[ConditionFlag.BoundByDuty] || 
            _condition[ConditionFlag.BoundByDuty56] ||
            _condition[ConditionFlag.BoundByDuty95])
            return true;
        
        if (_condition[ConditionFlag.TradeOpen])
            return true;
        
        if (_condition[ConditionFlag.OccupiedInQuestEvent] ||
            _condition[ConditionFlag.OccupiedSummoningBell])
            return true;
        
        if (_condition[ConditionFlag.BetweenAreas] ||
            _condition[ConditionFlag.BetweenAreas51])
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
                var retainerList = _gameGui.GetAddonByName("RetainerList", 1);
                if (retainerList != IntPtr.Zero)
                    return true;
            }
            
            var itemSearch = _gameGui.GetAddonByName("ItemSearch", 1);
            if (itemSearch != IntPtr.Zero)
                return true;
        }
        
        return false;
    }
    
    public bool IsInAllowedZone()
    {
        var territory = _clientState.TerritoryType;
        return _passiveDiscardZones.Contains(territory);
    }
    
    private bool HasItemsToDiscard(
        IEnumerable<InventoryItemInfo> allItems,
        HashSet<uint> autoDiscardItems,
        HashSet<uint> blacklistedItems)
    {
        if (_lastItemCheckTime != DateTime.MinValue && 
            DateTime.Now - _lastItemCheckTime < _itemCheckInterval)
        {
            return _hasItemsToDiscardCache;
        }
        
        _hasItemsToDiscardCache = allItems.Any(item => 
            autoDiscardItems.Contains(item.ItemId) && 
            InventoryHelpers.IsSafeToDiscard(item, blacklistedItems));
        
        _lastItemCheckTime = DateTime.Now;
        return _hasItemsToDiscardCache;
    }
    
    public PassiveDiscardStatus GetStatus(
        HashSet<uint> autoDiscardItems,
        IEnumerable<InventoryItemInfo> allItems,
        HashSet<uint> blacklistedItems)
    {
        if (!_settings.PassiveDiscard.Enabled)
            return new PassiveDiscardStatus { State = PassiveDiscardState.Disabled };
        
        if (!HasItemsToDiscard(allItems, autoDiscardItems, blacklistedItems))
            return new PassiveDiscardStatus { State = PassiveDiscardState.NoItems };
        
        if (IsPlayerBusy())
            return new PassiveDiscardStatus { State = PassiveDiscardState.PlayerBusy };
        
        if (!IsInAllowedZone())
            return new PassiveDiscardStatus { State = PassiveDiscardState.NotInAllowedZone };
        
        var idleDuration = DateTime.Now - _idleStartTime;
        if (idleDuration.TotalSeconds < _settings.PassiveDiscard.IdleTimeSeconds)
        {
            return new PassiveDiscardStatus
            {
                State = PassiveDiscardState.WaitingForIdle,
                IdleSeconds = (int)idleDuration.TotalSeconds,
                RequiredIdleSeconds = _settings.PassiveDiscard.IdleTimeSeconds
            };
        }
        
        if (_lastAutoDiscardTime != DateTime.MinValue)
        {
            var timeSinceLastDiscard = DateTime.Now - _lastAutoDiscardTime;
            if (timeSinceLastDiscard < _passiveDiscardCooldown)
            {
                return new PassiveDiscardStatus
                {
                    State = PassiveDiscardState.Cooldown,
                    CooldownSecondsRemaining = (int)(_passiveDiscardCooldown - timeSinceLastDiscard).TotalSeconds
                };
            }
        }
        
        return new PassiveDiscardStatus { State = PassiveDiscardState.Ready };
    }
}

public class PassiveDiscardStatus
{
    public PassiveDiscardState State { get; set; }
    public int IdleSeconds { get; set; }
    public int RequiredIdleSeconds { get; set; }
    public int CooldownSecondsRemaining { get; set; }
}

public enum PassiveDiscardState
{
    Disabled,
    NoItems,
    PlayerBusy,
    NotInAllowedZone,
    WaitingForIdle,
    Cooldown,
    Ready
}

