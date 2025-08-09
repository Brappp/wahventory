using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dalamud.Plugin.Services;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Newtonsoft.Json;
using wahventory.Core;
using wahventory.Models;
using wahventory.Services.Helpers;

namespace wahventory.Services;

public unsafe class AutoInventoryTrackerService : IDisposable
{
    private readonly Plugin _plugin;
    private readonly IPluginLog _log;
    private readonly IClientState _clientState;
    private readonly IAddonLifecycle _addonLifecycle;
    private readonly IFramework _framework;
    private readonly InventoryHelpers _inventoryHelpers;
    private readonly string _saveFile;
    
    private TrackerData _data = new();
    private DateTime _lastSave = DateTime.MinValue;
    private readonly TimeSpan _saveInterval = TimeSpan.FromSeconds(10);
    private DateTime _lastMainInventoryScan = DateTime.MinValue;
    private readonly TimeSpan _mainInventoryScanInterval = TimeSpan.FromSeconds(30);
    
    // Track what's currently open
    private bool _retainerWindowOpen = false;
    private bool _saddlebagOpen = false;
    private bool _armoireOpen = false;
    private string? _currentRetainerName = null;
    
    public event Action? OnInventoriesUpdated;
    
    public TrackerData Data => _data;
    public bool HasData => _data.Inventories.Any();
    
    public AutoInventoryTrackerService(Plugin plugin)
    {
        _plugin = plugin;
        _log = Plugin.Log;
        _clientState = Plugin.ClientState;
        _addonLifecycle = Plugin.AddonLifecycle;
        _framework = Plugin.Framework;
        _inventoryHelpers = new InventoryHelpers(Plugin.DataManager, _log);
        _saveFile = Path.Combine(Plugin.PluginInterface.ConfigDirectory.FullName, "tracked_inventories.json");
        
        LoadData();
        RegisterEvents();
    }
    
    private void RegisterEvents()
    {
        // Character login/logout
        _clientState.Login += OnLogin;
        _clientState.Logout += OnLogout;
        
        // Framework update for periodic scans
        _framework.Update += OnFrameworkUpdate;
        
        // Addon lifecycle events for automatic detection
        // Retainer windows
        _addonLifecycle.RegisterListener(AddonEvent.PostSetup, "RetainerList", OnRetainerListOpened);
        _addonLifecycle.RegisterListener(AddonEvent.PreFinalize, "RetainerList", OnRetainerListClosed);
        _addonLifecycle.RegisterListener(AddonEvent.PostSetup, "InventoryRetainer", OnRetainerInventoryOpened);
        _addonLifecycle.RegisterListener(AddonEvent.PreFinalize, "InventoryRetainer", OnRetainerInventoryClosed);
        _addonLifecycle.RegisterListener(AddonEvent.PostSetup, "InventoryRetainerLarge", OnRetainerInventoryOpened);
        _addonLifecycle.RegisterListener(AddonEvent.PreFinalize, "InventoryRetainerLarge", OnRetainerInventoryClosed);
        
        // Saddlebag
        _addonLifecycle.RegisterListener(AddonEvent.PostSetup, "InventoryBuddy", OnSaddlebagOpened);
        _addonLifecycle.RegisterListener(AddonEvent.PreFinalize, "InventoryBuddy", OnSaddlebagClosed);
        
        // Armoire
        _addonLifecycle.RegisterListener(AddonEvent.PostSetup, "Cabinet", OnArmoireOpened);
        _addonLifecycle.RegisterListener(AddonEvent.PreFinalize, "Cabinet", OnArmoireClosed);
        
        // FC Chest
        _addonLifecycle.RegisterListener(AddonEvent.PostSetup, "FreeCompanyChest", OnFCChestOpened);
        _addonLifecycle.RegisterListener(AddonEvent.PreFinalize, "FreeCompanyChest", OnFCChestClosed);
    }
    
    private void OnLogin()
    {
        _log.Info("Player logged in, scheduling inventory scan");
        // Delay scan to let game fully load
        _lastMainInventoryScan = DateTime.Now.AddSeconds(-25); // Will scan in 5 seconds
    }
    
    private void OnLogout(int type, int code)
    {
        SaveData();
    }
    
    private void OnFrameworkUpdate(IFramework framework)
    {
        // Auto-save periodically
        if (_lastSave != DateTime.MinValue && DateTime.Now - _lastSave > _saveInterval)
        {
            SaveData();
            _lastSave = DateTime.MinValue;
        }
        
        // Scan main inventory periodically when logged in
        if (_clientState.LocalPlayer != null && 
            DateTime.Now - _lastMainInventoryScan > _mainInventoryScanInterval)
        {
            ScanCurrentCharacter();
            _lastMainInventoryScan = DateTime.Now;
        }
    }
    
    private void OnRetainerListOpened(AddonEvent type, AddonArgs args)
    {
        _retainerWindowOpen = true;
        _log.Debug("Retainer list opened");
    }
    
    private void OnRetainerListClosed(AddonEvent type, AddonArgs args)
    {
        _retainerWindowOpen = false;
        _currentRetainerName = null;
        _log.Debug("Retainer list closed");
    }
    
    private void OnRetainerInventoryOpened(AddonEvent type, AddonArgs args)
    {
        // Delay scan to let inventory load
        _framework.RunOnTick(() => ScanRetainer(), delayTicks: 10);
    }
    
    private void OnRetainerInventoryClosed(AddonEvent type, AddonArgs args)
    {
        _currentRetainerName = null;
    }
    
    private void OnSaddlebagOpened(AddonEvent type, AddonArgs args)
    {
        _saddlebagOpen = true;
        _log.Debug("Saddlebag opened, scanning");
        ScanCurrentCharacter(); // Re-scan to include saddlebags
    }
    
    private void OnSaddlebagClosed(AddonEvent type, AddonArgs args)
    {
        _saddlebagOpen = false;
    }
    
    private void OnArmoireOpened(AddonEvent type, AddonArgs args)
    {
        _armoireOpen = true;
        _log.Debug("Armoire opened");
        // Armoire scanning would need special handling
    }
    
    private void OnArmoireClosed(AddonEvent type, AddonArgs args)
    {
        _armoireOpen = false;
    }
    
    private void OnFCChestOpened(AddonEvent type, AddonArgs args)
    {
        _log.Debug("FC Chest opened");
        // FC chest scanning would need special handling
    }
    
    private void OnFCChestClosed(AddonEvent type, AddonArgs args)
    {
        _log.Debug("FC Chest closed");
    }
    
    public void ScanCurrentCharacter()
    {
        try
        {
            var player = _clientState.LocalPlayer;
            if (player == null) return;
            
            var characterId = _clientState.LocalContentId;
            var characterName = player.Name.ToString();
            var worldName = player.CurrentWorld.Value.Name.ToString();
            
            var inventory = new TrackedInventory
            {
                OwnerId = characterId,
                OwnerName = $"{characterName}@{worldName}",
                Type = OwnerType.Player,
                LastUpdated = DateTime.Now,
                Items = new List<TrackedItem>()
            };
            
            // Scan all player inventories
            ScanInventoryType(inventory, InventoryType.Inventory1, "Inventory");
            ScanInventoryType(inventory, InventoryType.Inventory2, "Inventory");
            ScanInventoryType(inventory, InventoryType.Inventory3, "Inventory");
            ScanInventoryType(inventory, InventoryType.Inventory4, "Inventory");
            
            // Scan equipped items
            ScanInventoryType(inventory, InventoryType.EquippedItems, "Equipped");
            
            // Scan armory
            ScanInventoryType(inventory, InventoryType.ArmoryMainHand, "Armory");
            ScanInventoryType(inventory, InventoryType.ArmoryOffHand, "Armory");
            ScanInventoryType(inventory, InventoryType.ArmoryHead, "Armory");
            ScanInventoryType(inventory, InventoryType.ArmoryBody, "Armory");
            ScanInventoryType(inventory, InventoryType.ArmoryHands, "Armory");
            ScanInventoryType(inventory, InventoryType.ArmoryLegs, "Armory");
            ScanInventoryType(inventory, InventoryType.ArmoryFeets, "Armory");
            ScanInventoryType(inventory, InventoryType.ArmoryEar, "Armory");
            ScanInventoryType(inventory, InventoryType.ArmoryNeck, "Armory");
            ScanInventoryType(inventory, InventoryType.ArmoryWrist, "Armory");
            ScanInventoryType(inventory, InventoryType.ArmoryRings, "Armory");
            
            // Scan saddlebags if available
            if (_saddlebagOpen || CanAccessSaddlebag())
            {
                ScanInventoryType(inventory, InventoryType.SaddleBag1, "Saddlebag");
                ScanInventoryType(inventory, InventoryType.SaddleBag2, "Saddlebag");
            }
            
            // Scan crystals
            ScanInventoryType(inventory, InventoryType.Crystals, "Crystals");
            
            _data.Inventories[characterId] = inventory;
            _log.Debug($"Auto-scanned {inventory.Items.Count} items for {characterName}");
            
            ScheduleSave();
            OnInventoriesUpdated?.Invoke();
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to scan current character");
        }
    }
    
    private bool CanAccessSaddlebag()
    {
        // Check if player has a companion that provides saddlebag access
        var inventoryManager = InventoryManager.Instance();
        if (inventoryManager == null) return false;
        
        var saddlebag = inventoryManager->GetInventoryContainer(InventoryType.SaddleBag1);
        return saddlebag != null && saddlebag->Size > 0;
    }
    
    public void ScanRetainer()
    {
        try
        {
            var retainerManager = RetainerManager.Instance();
            if (retainerManager == null) return;
            
            var activeRetainer = retainerManager->GetActiveRetainer();
            if (activeRetainer == null) return;
            
            // Get retainer name
            var retainerName = GetRetainerName(activeRetainer);
            if (string.IsNullOrEmpty(retainerName)) return;
            
            // Use the retainer's ID from the structure
            var retainerId = activeRetainer->RetainerId;
            
            var inventory = new TrackedInventory
            {
                OwnerId = retainerId,
                OwnerName = retainerName,
                Type = OwnerType.Retainer,
                LastUpdated = DateTime.Now,
                Items = new List<TrackedItem>()
            };
            
            // Scan retainer inventories
            ScanInventoryType(inventory, InventoryType.RetainerPage1, "Inventory");
            ScanInventoryType(inventory, InventoryType.RetainerPage2, "Inventory");
            ScanInventoryType(inventory, InventoryType.RetainerPage3, "Inventory");
            ScanInventoryType(inventory, InventoryType.RetainerPage4, "Inventory");
            ScanInventoryType(inventory, InventoryType.RetainerPage5, "Inventory");
            ScanInventoryType(inventory, InventoryType.RetainerPage6, "Inventory");
            ScanInventoryType(inventory, InventoryType.RetainerPage7, "Inventory");
            
            // Scan retainer equipped
            ScanInventoryType(inventory, InventoryType.RetainerEquippedItems, "Equipped");
            
            // Scan retainer crystals
            ScanInventoryType(inventory, InventoryType.RetainerCrystals, "Crystals");
            
            // Scan retainer market
            ScanInventoryType(inventory, InventoryType.RetainerMarket, "Market");
            
            _data.Inventories[retainerId] = inventory;
            _currentRetainerName = retainerName;
            _log.Info($"Auto-scanned {inventory.Items.Count} items for retainer {retainerName}");
            
            ScheduleSave();
            OnInventoriesUpdated?.Invoke();
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to scan retainer");
        }
    }
    
    private string GetRetainerName(RetainerManager.Retainer* retainer)
    {
        if (retainer == null) return "";
        
        try
        {
            // Get name from the retainer structure
            var nameBytes = new byte[32];
            for (int i = 0; i < 32 && retainer->Name[i] != 0; i++)
            {
                nameBytes[i] = retainer->Name[i];
            }
            return System.Text.Encoding.UTF8.GetString(nameBytes).TrimEnd('\0');
        }
        catch
        {
            return $"Retainer_{retainer->RetainerId}";
        }
    }
    
    private void ScanInventoryType(TrackedInventory inventory, InventoryType type, string containerName)
    {
        var inventoryManager = InventoryManager.Instance();
        if (inventoryManager == null) return;
        
        var container = inventoryManager->GetInventoryContainer(type);
        if (container == null || container->Size == 0) return;
        
        var itemSheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>();
        if (itemSheet == null) return;
        
        for (var i = 0; i < container->Size; i++)
        {
            var slot = container->GetInventorySlot(i);
            if (slot == null || slot->ItemId == 0) continue;
            
            var item = itemSheet.GetRow(slot->ItemId);
            if (item.RowId == 0) continue;
            
            var trackedItem = new TrackedItem
            {
                ItemId = slot->ItemId,
                ItemName = item.Name.ToString(),
                Quantity = (uint)slot->Quantity,
                IsHQ = slot->Flags.HasFlag(InventoryItem.ItemFlags.HighQuality),
                Container = containerName,
                Slot = i,
                Spiritbond = slot->SpiritbondOrCollectability,
                Condition = slot->Condition,
                GlamourId = slot->GlamourId,
                Stain1 = slot->Stains[0],
                Stain2 = slot->Stains[1],
                Materia = new byte[] { 
                    (byte)slot->Materia[0], 
                    (byte)slot->Materia[1], 
                    (byte)slot->Materia[2], 
                    (byte)slot->Materia[3], 
                    (byte)slot->Materia[4] 
                }
            };
            
            inventory.Items.Add(trackedItem);
        }
    }
    
    public List<TrackedItem> SearchItems(string query)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
            return new List<TrackedItem>();
        
        var results = new List<(TrackedItem item, TrackedInventory inventory)>();
        
        foreach (var inventory in _data.Inventories.Values)
        {
            var matches = inventory.Items
                .Where(i => i.ItemName.Contains(query, StringComparison.OrdinalIgnoreCase))
                .Select(i => (i, inventory));
            
            results.AddRange(matches);
        }
        
        return results
            .GroupBy(r => new { r.item.ItemId, r.item.IsHQ, r.inventory.OwnerId })
            .Select(g => {
                var first = g.First();
                return new TrackedItem
                {
                    ItemId = first.item.ItemId,
                    ItemName = first.item.ItemName,
                    Quantity = (uint)g.Sum(x => x.item.Quantity),
                    IsHQ = first.item.IsHQ,
                    Container = $"{first.inventory.DisplayName} - {first.item.Container}",
                    Slot = first.item.Slot,
                    MarketPrice = first.item.MarketPrice,
                    MarketListedDate = first.item.MarketListedDate
                };
            })
            .OrderBy(i => i.ItemName)
            .ToList();
    }
    
    public void ClearData()
    {
        _data = new TrackerData();
        SaveData();
        OnInventoriesUpdated?.Invoke();
    }
    
    public void ClearInventory(ulong ownerId)
    {
        if (_data.Inventories.ContainsKey(ownerId))
        {
            _data.Inventories.Remove(ownerId);
            ScheduleSave();
            OnInventoriesUpdated?.Invoke();
        }
    }
    
    private void ScheduleSave()
    {
        _lastSave = DateTime.Now;
    }
    
    private void SaveData()
    {
        try
        {
            var json = JsonConvert.SerializeObject(_data, Formatting.Indented);
            File.WriteAllText(_saveFile, json);
            _log.Debug("Saved tracker data");
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to save tracker data");
        }
    }
    
    private void LoadData()
    {
        try
        {
            if (File.Exists(_saveFile))
            {
                var json = File.ReadAllText(_saveFile);
                _data = JsonConvert.DeserializeObject<TrackerData>(json) ?? new TrackerData();
                _log.Info($"Loaded {_data.Inventories.Count} tracked inventories");
            }
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to load tracker data");
            _data = new TrackerData();
        }
    }
    
    public void Dispose()
    {
        // Unregister all events
        _clientState.Login -= OnLogin;
        _clientState.Logout -= OnLogout;
        _framework.Update -= OnFrameworkUpdate;
        
        _addonLifecycle.UnregisterListener(OnRetainerListOpened);
        _addonLifecycle.UnregisterListener(OnRetainerListClosed);
        _addonLifecycle.UnregisterListener(OnRetainerInventoryOpened);
        _addonLifecycle.UnregisterListener(OnRetainerInventoryClosed);
        _addonLifecycle.UnregisterListener(OnSaddlebagOpened);
        _addonLifecycle.UnregisterListener(OnSaddlebagClosed);
        _addonLifecycle.UnregisterListener(OnArmoireOpened);
        _addonLifecycle.UnregisterListener(OnArmoireClosed);
        _addonLifecycle.UnregisterListener(OnFCChestOpened);
        _addonLifecycle.UnregisterListener(OnFCChestClosed);
        
        SaveData();
    }
}
