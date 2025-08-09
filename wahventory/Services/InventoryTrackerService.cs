using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Newtonsoft.Json;
using wahventory.Core;
using wahventory.Models;
using wahventory.Services.Helpers;

namespace wahventory.Services;

public unsafe class InventoryTrackerService : IDisposable
{
    private readonly Plugin _plugin;
    private readonly IPluginLog _log;
    private readonly IClientState _clientState;
    private readonly InventoryHelpers _inventoryHelpers;
    private readonly string _saveFile;
    
    private TrackerData _data = new();
    private DateTime _lastSave = DateTime.MinValue;
    private readonly TimeSpan _saveInterval = TimeSpan.FromSeconds(10);
    
    public event Action? OnInventoriesUpdated;
    
    public TrackerData Data => _data;
    public bool HasData => _data.Inventories.Any();
    
    public InventoryTrackerService(Plugin plugin)
    {
        _plugin = plugin;
        _log = Plugin.Log;
        _clientState = Plugin.ClientState;
        _inventoryHelpers = new InventoryHelpers(Plugin.DataManager, _log);
        _saveFile = Path.Combine(Plugin.PluginInterface.ConfigDirectory.FullName, "tracked_inventories.json");
        
        LoadData();
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
            
            // Scan saddlebags
            ScanInventoryType(inventory, InventoryType.SaddleBag1, "Saddlebag");
            ScanInventoryType(inventory, InventoryType.SaddleBag2, "Saddlebag");
            
            // Scan crystals
            ScanInventoryType(inventory, InventoryType.Crystals, "Crystals");
            
            _data.Inventories[characterId] = inventory;
            _log.Info($"Scanned {inventory.Items.Count} items for {characterName}");
            
            ScheduleSave();
            OnInventoriesUpdated?.Invoke();
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to scan current character");
        }
    }
    
    public void ScanRetainer()
    {
        try
        {
            var retainerManager = RetainerManager.Instance();
            if (retainerManager == null) return;
            
            var activeRetainer = retainerManager->GetActiveRetainer();
            if (activeRetainer == null) return;
            
            // Get retainer info from the UI
            var agentRetainer = AgentModule.Instance()->GetAgentByInternalId(AgentId.Retainer);
            if (agentRetainer == null || !agentRetainer->IsAgentActive()) return;
            
            // Use the retainer's ID from the structure
            var retainerId = activeRetainer->RetainerId;
            var retainerName = GetRetainerName();
            
            if (string.IsNullOrEmpty(retainerName)) return;
            
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
            _log.Info($"Scanned {inventory.Items.Count} items for retainer {retainerName}");
            
            ScheduleSave();
            OnInventoriesUpdated?.Invoke();
        }
        catch (Exception ex)
        {
            _log.Error(ex, "Failed to scan retainer");
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
    
    private string GetRetainerName()
    {
        try
        {
            // Try to get retainer name from the retainer window
            var addon = Plugin.GameGui.GetAddonByName("RetainerList");
            if (!addon.IsNull)
            {
                var addonPtr = (AtkUnitBase*)addon.Address;
                if (addonPtr != null && addonPtr->IsVisible)
                {
                    // This would need proper reverse engineering of the retainer list addon
                    // For now, return a placeholder
                    return $"Retainer_{DateTime.Now.Ticks}";
                }
            }
            return "";
        }
        catch
        {
            return "";
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
                    Slot = first.item.Slot
                };
            })
            .OrderBy(i => i.ItemName)
            .ToList();
    }
    
    public Dictionary<ulong, List<TrackedItem>> GetItemLocations(uint itemId)
    {
        var locations = new Dictionary<ulong, List<TrackedItem>>();
        
        foreach (var inventory in _data.Inventories.Values)
        {
            var items = inventory.Items.Where(i => i.ItemId == itemId).ToList();
            if (items.Any())
            {
                locations[inventory.OwnerId] = items;
            }
        }
        
        return locations;
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
    
    public void Update()
    {
        if (_lastSave != DateTime.MinValue && DateTime.Now - _lastSave > _saveInterval)
        {
            SaveData();
            _lastSave = DateTime.MinValue;
        }
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
        if (_lastSave != DateTime.MinValue)
        {
            SaveData();
        }
    }
}
