using System;
using System.Collections.Generic;
using System.Linq;
using ECommons.Automation.NeoTaskManager;
using Dalamud.Plugin.Services;
using Dalamud.Game.ClientState.Conditions;
using wahventory.Services.Helpers;
using wahventory.Models;
using wahventory.Core;

namespace wahventory.Services.Tasks;

public class PassiveTaskService : TaskServiceBase
{
    private readonly ICondition _condition;
    private readonly IClientState _clientState;
    private readonly IGameGui _gameGui;
    private readonly Configuration _configuration;
    
    // Events
    public event Action<string>? StatusChanged;
    public event Action? PassiveDiscardTriggered;
    
    private DateTime _idleStartTime = DateTime.Now;
    private DateTime _lastAutoDiscardTime = DateTime.MinValue;
    private bool _wasPlayerBusy = true;
    private bool _isMonitoring = false;
    private readonly TimeSpan _passiveDiscardCooldown = TimeSpan.FromMinutes(5);
    private readonly TimeSpan _monitoringInterval = TimeSpan.FromSeconds(5);

    // Safe zones where passive discard can operate
    private readonly List<uint> _passiveDiscardZones = new()
    {
        128, 129, 130, 131, 132, 133, 136, 144, // Main cities
        418, 419, 478, 628, 635, 819, 820, 962, 963, 1185, // Expansion cities
        177, 179, 1205, 181, 182, 183, // Inn rooms
        282, 283, 284, 340, 342, 343, 344, 341, 345, 346, 347, // Housing
        384, 385, 386, 423, 424, 425, 534, 535, 536, // More housing/barracks
        641, 642, 643, 644, 645, 646, 647, 648, 649, 650, 651, 652, 653, 654, 655, // Shirogane
        980, 981, 982, 983, 984, 985, 987, 1269, // Empyreum
        156, 759, 1186, 429, 629, 843, 844, 990, 991, // Other safe areas
        388, 389, 390, 391, 792, 886, 979, 1055, // Gold Saucer, etc.
        198, 199, 200, 201, 395, 396, 397, 398, 399, 571, 572, 576, 577, 578, 579, 580 // Special instances
    };

    public PassiveTaskService(
        TaskManager taskManager,
        IPluginLog log,
        ICondition condition,
        IClientState clientState,
        IGameGui gameGui,
        Configuration configuration) : base(taskManager, log)
    {
        _condition = condition;
        _clientState = clientState;
        _gameGui = gameGui;
        _configuration = configuration;
    }

    public void StartMonitoring()
    {
        if (_isMonitoring) return;
        
        _isMonitoring = true;
        Log.Information("Starting passive discard monitoring");
        
        TaskManager.Enqueue(() => MonitorPassiveDiscard());
    }

    public void StopMonitoring()
    {
        _isMonitoring = false;
        Log.Information("Stopping passive discard monitoring");
    }

    private void MonitorPassiveDiscard()
    {
        if (!_isMonitoring) return;
        
        try
        {
            var settings = _configuration.InventorySettings.PassiveDiscard;
            
            if (!settings.Enabled)
            {
                // Schedule next check and return
                TaskManager.EnqueueDelay((int)_monitoringInterval.TotalMilliseconds);
                TaskManager.Enqueue(() => MonitorPassiveDiscard());
                return;
            }

            var isBusy = IsPlayerBusy();
            var statusMessage = GetCurrentStatus(isBusy, settings);
            StatusChanged?.Invoke(statusMessage);

            // Track idle state transitions
            if (isBusy && !_wasPlayerBusy)
            {
                _wasPlayerBusy = true;
                Log.Debug("Player became busy");
            }
            else if (!isBusy && _wasPlayerBusy)
            {
                _wasPlayerBusy = false;
                _idleStartTime = DateTime.Now;
                Log.Debug("Player became idle");
            }

            // Check if conditions are met for passive discard
            if (ShouldTriggerPassiveDiscard(isBusy, settings))
            {
                Log.Information("Triggering passive discard");
                _lastAutoDiscardTime = DateTime.Now;
                PassiveDiscardTriggered?.Invoke();
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error in passive discard monitoring");
        }
        
        // Schedule next monitoring cycle
        TaskManager.EnqueueDelay((int)_monitoringInterval.TotalMilliseconds);
        TaskManager.Enqueue(() => MonitorPassiveDiscard());
    }

    private bool ShouldTriggerPassiveDiscard(bool isBusy, PassiveDiscardSettings settings)
    {
        if (isBusy) return false;
        if (!IsInAllowedZone()) return false;

        var idleDuration = DateTime.Now - _idleStartTime;
        if (idleDuration.TotalSeconds < settings.IdleTimeSeconds) return false;

        if (_lastAutoDiscardTime != DateTime.MinValue)
        {
            var timeSinceLastDiscard = DateTime.Now - _lastAutoDiscardTime;
            if (timeSinceLastDiscard < _passiveDiscardCooldown) return false;
        }

        return true;
    }

    private string GetCurrentStatus(bool isBusy, PassiveDiscardSettings settings)
    {
        if (!settings.Enabled) return "Disabled";
        
        if (isBusy) return "Player Busy";
        
        if (!IsInAllowedZone()) return "Not in Allowed Zone";
        
        var idleDuration = DateTime.Now - _idleStartTime;
        if (idleDuration.TotalSeconds < settings.IdleTimeSeconds)
        {
            return $"Waiting for idle ({(int)idleDuration.TotalSeconds}/{settings.IdleTimeSeconds}s)";
        }

        if (_lastAutoDiscardTime != DateTime.MinValue)
        {
            var timeSinceLastDiscard = DateTime.Now - _lastAutoDiscardTime;
            if (timeSinceLastDiscard < _passiveDiscardCooldown)
            {
                var remainingCooldown = _passiveDiscardCooldown - timeSinceLastDiscard;
                return $"Cooldown ({(int)remainingCooldown.TotalSeconds}s remaining)";
            }
        }

        return "Ready to execute auto-discard";
    }

    private bool IsPlayerBusy()
    {
        // Check various game conditions that indicate the player is busy
        if (_condition[ConditionFlag.InCombat]) return true;
        if (_condition[ConditionFlag.OccupiedInCutSceneEvent]) return true;
        if (_condition[ConditionFlag.WatchingCutscene]) return true;
        if (_condition[ConditionFlag.WatchingCutscene78]) return true;
        if (_condition[ConditionFlag.Crafting]) return true;
        if (_condition[ConditionFlag.Gathering]) return true;
        if (_condition[ConditionFlag.Fishing]) return true;
        if (_condition[ConditionFlag.BoundByDuty]) return true;
        if (_condition[ConditionFlag.BoundByDuty56]) return true;
        if (_condition[ConditionFlag.BoundByDuty95]) return true;
        if (_condition[ConditionFlag.TradeOpen]) return true;
        if (_condition[ConditionFlag.OccupiedInQuestEvent]) return true;
        if (_condition[ConditionFlag.OccupiedSummoningBell]) return true;
        if (_condition[ConditionFlag.BetweenAreas]) return true;
        if (_condition[ConditionFlag.BetweenAreas51]) return true;

        // Check for specific UI elements that indicate busy state
        try
        {
            var retainerList = _gameGui.GetAddonByName("RetainerList", 1);
            if (retainerList != IntPtr.Zero) return true;

            var itemSearch = _gameGui.GetAddonByName("ItemSearch", 1);
            if (itemSearch != IntPtr.Zero) return true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error checking UI addons for busy state");
        }

        return false;
    }

    private bool IsInAllowedZone()
    {
        var territory = _clientState.TerritoryType;
        return _passiveDiscardZones.Contains(territory);
    }

    public bool IsCurrentZoneAllowed()
    {
        return IsInAllowedZone();
    }

    public uint GetCurrentTerritoryId()
    {
        return _clientState.TerritoryType;
    }

    public TimeSpan GetIdleDuration()
    {
        return DateTime.Now - _idleStartTime;
    }

    public TimeSpan? GetRemainingCooldown()
    {
        if (_lastAutoDiscardTime == DateTime.MinValue) return null;
        
        var timeSinceLastDiscard = DateTime.Now - _lastAutoDiscardTime;
        var remaining = _passiveDiscardCooldown - timeSinceLastDiscard;
        
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    public override void Dispose()
    {
        StopMonitoring();
        base.Dispose();
    }
}