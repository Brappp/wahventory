using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Lumina.Excel.Sheets;
using wahventory.Models;

namespace wahventory.Helpers;

public unsafe class InventoryHelpers
{
    private readonly IDataManager _dataManager;
    private readonly IPluginLog _log;
    
    // ARDiscard-level safety lists
    public static readonly HashSet<uint> HardcodedBlacklist = new()
    {
        16039, // Ala Mhigan earrings
        24589, // Aetheryte earrings
        33648, // Menphina's earrings
        41081, // Azeyma's earrings
        
        21197, // UCOB token
        23175, // UWU token
        28633, // TEA token
        36810, // DSR token
        38951, // TOP token
        
        10155, // Ceruleum Tank
        10373, // Magitek Repair Materials
    };
    
    // Currency range (1-99)
    public static readonly HashSet<uint> CurrencyRange = 
        Enumerable.Range(1, 99).Select(x => (uint)x).ToHashSet();
    
    // Safe unique items despite being unique/untradeable
    public static readonly HashSet<uint> SafeUniqueItems = new()
    {
        2962, // Onion Doublet
        3279, // Onion Gaskins
        3743, // Onion Patterns
        
        9387, // Antique Helm
        9388, // Antique Mail
        9389, // Antique Gauntlets
        9390, // Antique Breeches
        9391, // Antique Sollerets
        
        6223, // Mended Imperial Pot Helm
        6224, // Mended Imperial Short Robe
        
        7060, // Durability Draught
        14945, // Squadron Enlistment Manual
        15772, // Contemporary Warfare: Defense
        15773, // Contemporary Warfare: Offense
        15774, // Contemporary Warfare: Magicks
        4572, // Company-issue Tonic
        20790, // High Grade Company-issue Tonic
    };
    
    // Separate inventory type groups
    private static readonly InventoryType[] MainInventories = 
    {
        InventoryType.Inventory1,
        InventoryType.Inventory2, 
        InventoryType.Inventory3,
        InventoryType.Inventory4
    };
    
    private static readonly InventoryType[] ArmoryInventories = 
    {
        InventoryType.ArmoryMainHand,
        InventoryType.ArmoryOffHand,
        InventoryType.ArmoryHead,
        InventoryType.ArmoryBody,
        InventoryType.ArmoryHands,
        InventoryType.ArmoryLegs,
        InventoryType.ArmoryFeets,
        InventoryType.ArmoryEar,
        InventoryType.ArmoryNeck,
        InventoryType.ArmoryWrist,
        InventoryType.ArmoryRings
    };
    
    private static readonly InventoryType[] SaddlebagInventories = 
    {
        InventoryType.SaddleBag1,
        InventoryType.SaddleBag2,
        InventoryType.PremiumSaddleBag1,
        InventoryType.PremiumSaddleBag2
    };
    
    public InventoryHelpers(IDataManager dataManager, IPluginLog log)
    {
        _dataManager = dataManager;
        _log = log;
    }
    
    public List<InventoryItemInfo> GetAllItems(bool includeArmory = false, bool includeSaddlebag = false)
    {
        var items = new List<InventoryItemInfo>();
        var inventoryManager = InventoryManager.Instance();
        
        if (inventoryManager == null)
        {
            _log.Error("InventoryManager is null");
            return items;
        }
        
        // Always include main inventory
        foreach (var inventoryType in MainInventories)
        {
            AddItemsFromInventory(items, inventoryManager, inventoryType);
        }
        
        // Optionally include armory
        if (includeArmory)
        {
            foreach (var inventoryType in ArmoryInventories)
            {
                AddItemsFromInventory(items, inventoryManager, inventoryType);
            }
        }
        
        // Optionally include saddlebag (currently disabled in original)
        if (includeSaddlebag)
        {
            foreach (var inventoryType in SaddlebagInventories)
            {
                AddItemsFromInventory(items, inventoryManager, inventoryType);
            }
        }
        
        return items;
    }
    
    private void AddItemsFromInventory(List<InventoryItemInfo> items, InventoryManager* inventoryManager, InventoryType inventoryType)
    {
        var container = inventoryManager->GetInventoryContainer(inventoryType);
        if (container == null || container->Size == 0) return;
        
        for (var i = 0; i < container->Size; i++)
        {
            var slot = container->GetInventorySlot(i);
            if (slot == null || slot->ItemId == 0) continue;
            
            var itemInfo = CreateItemInfo(slot, inventoryType, (short)i);
            if (itemInfo != null)
            {
                items.Add(itemInfo);
            }
        }
    }
    
    private InventoryItemInfo? CreateItemInfo(InventoryItem* slot, InventoryType container, short slotIndex)
    {
        var itemSheet = _dataManager.GetExcelSheet<Item>();
        if (itemSheet == null) return null;
        
        var item = itemSheet.GetRow(slot->ItemId);
        if (item.RowId == 0) return null;
        
        var uiCategorySheet = _dataManager.GetExcelSheet<ItemUICategory>();
        var categoryName = "Miscellaneous";
        
        if (uiCategorySheet != null && item.ItemUICategory.RowId > 0)
        {
            var category = uiCategorySheet.GetRow(item.ItemUICategory.RowId);
            if (category.RowId != 0)
            {
                categoryName = category.Name.ExtractText();
            }
        }
        
        return new InventoryItemInfo
        {
            ItemId = slot->ItemId,
            Name = item.Name.ExtractText(),
            Quantity = slot->Quantity,
            Container = container,
            Slot = slotIndex,
            IsHQ = slot->Flags.HasFlag(InventoryItem.ItemFlags.HighQuality),
            IconId = item.Icon,
            CanBeDiscarded = !item.IsIndisposable,
            CanBeTraded = !item.IsUntradable,
            IsCollectable = item.IsCollectable,
            SpiritBond = 0, // Spiritbond not available in current struct
            Durability = slot->Condition,
            MaxDurability = 30000, // Standard max durability
            CategoryName = categoryName,
            ItemUICategory = item.ItemUICategory.RowId,
            ItemLevel = item.LevelItem.RowId,
            EquipLevel = item.LevelEquip,
            Rarity = item.Rarity,
            IsUnique = item.IsUnique,
            IsUntradable = item.IsUntradable,
            IsIndisposable = item.IsIndisposable,
            EquipSlotCategory = item.EquipSlotCategory.RowId
        };
    }
    
    public static bool IsInGearset(uint itemId)
    {
        var gearsetModule = RaptureGearsetModule.Instance();
        if (gearsetModule == null) return false;
        
        for (var i = 0; i < 100; i++) // Max 100 gearsets
        {
            var gearset = gearsetModule->GetGearset(i);
            if (gearset == null || !gearset->Flags.HasFlag(RaptureGearsetModule.GearsetFlag.Exists))
                continue;
            
            // Check all equipment slots
            for (var j = 0; j < gearset->Items.Length; j++)
            {
                if (gearset->Items[j].ItemId == itemId)
                    return true;
            }
        }
        
        return false;
    }
    
    public static SafetyAssessment AssessItemSafety(InventoryItemInfo item, InventorySettings settings)
    {
        var assessment = new SafetyAssessment
        {
            ItemId = item.ItemId,
            IsSafeToDiscard = true
        };
        
        // Check hardcoded blacklist
        if (HardcodedBlacklist.Contains(item.ItemId))
        {
            assessment.SafetyFlags.Add("Ultimate Token / Special Item");
            assessment.IsSafeToDiscard = false;
            assessment.FlagColor = SafetyFlagColor.Critical;
        }
        
        // Check currency range
        if (CurrencyRange.Contains(item.ItemId))
        {
            assessment.SafetyFlags.Add("Currency Item");
            assessment.IsSafeToDiscard = false;
            assessment.FlagColor = SafetyFlagColor.Critical;
        }
        
        // Check user blacklist
        if (settings.BlacklistedItems.Contains(item.ItemId))
        {
            assessment.SafetyFlags.Add("User Blacklisted");
            assessment.IsSafeToDiscard = false;
            assessment.FlagColor = SafetyFlagColor.Critical;
        }
        
        // Check if in gearset
        if (IsInGearset(item.ItemId))
        {
            assessment.SafetyFlags.Add("In Gearset");
            assessment.IsSafeToDiscard = false;
            assessment.FlagColor = SafetyFlagColor.Warning;
        }
        
        // Check if indisposable
        if (item.IsIndisposable || !item.CanBeDiscarded)
        {
            assessment.SafetyFlags.Add("Cannot Be Discarded");
            assessment.IsSafeToDiscard = false;
            assessment.FlagColor = SafetyFlagColor.Critical;
        }
        
        // Check for high level gear
        if (item.IsGear && item.ItemLevel >= settings.SafetyFilters.MaxGearItemLevel)
        {
            assessment.SafetyFlags.Add($"High Level Gear (i{item.ItemLevel})");
            if (assessment.FlagColor < SafetyFlagColor.Warning)
                assessment.FlagColor = SafetyFlagColor.Warning;
        }
        
        // Check unique/untradeable combo
        if (item.IsUnique && item.IsUntradable && !SafeUniqueItems.Contains(item.ItemId))
        {
            assessment.SafetyFlags.Add("Unique & Untradeable");
            if (assessment.FlagColor < SafetyFlagColor.Warning)
                assessment.FlagColor = SafetyFlagColor.Warning;
        }
        
        // Check HQ
        if (item.IsHQ)
        {
            assessment.SafetyFlags.Add("High Quality");
            if (assessment.FlagColor < SafetyFlagColor.Caution)
                assessment.FlagColor = SafetyFlagColor.Caution;
        }
        
        // Check collectables
        if (item.IsCollectable)
        {
            assessment.SafetyFlags.Add("Collectable");
            if (assessment.FlagColor < SafetyFlagColor.Info)
                assessment.FlagColor = SafetyFlagColor.Info;
        }
        
        // Check spiritbond
        if (item.SpiritBond >= settings.SafetyFilters.MinSpiritbondToFilter)
        {
            assessment.SafetyFlags.Add($"Spiritbond {item.SpiritBond}%");
            if (assessment.FlagColor < SafetyFlagColor.Info)
                assessment.FlagColor = SafetyFlagColor.Info;
        }
        
        return assessment;
    }
    
    public static bool IsSafeToDiscard(InventoryItemInfo item, HashSet<uint> userBlacklist)
    {
        // Never discard hardcoded blacklist items
        if (HardcodedBlacklist.Contains(item.ItemId)) return false;
        if (CurrencyRange.Contains(item.ItemId)) return false;
        
        // Never discard user blacklisted items
        if (userBlacklist.Contains(item.ItemId)) return false;
        
        // Never discard gearset items
        if (IsInGearset(item.ItemId)) return false;
        
        // Never discard indisposable items
        if (item.IsIndisposable || !item.CanBeDiscarded) return false;
        
        return true;
    }
    
    public void DiscardItem(InventoryItemInfo item)
    {
        var inventoryManager = InventoryManager.Instance();
        if (inventoryManager == null)
        {
            throw new InvalidOperationException("InventoryManager is null");
        }
        
        var container = inventoryManager->GetInventoryContainer(item.Container);
        if (container == null)
        {
            throw new InvalidOperationException($"Container {item.Container} not found");
        }
        
        var slot = container->GetInventorySlot(item.Slot);
        if (slot == null || slot->ItemId != item.ItemId)
        {
            throw new InvalidOperationException($"Item {item.Name} not found in expected slot");
        }
        
        // Use AgentInventoryContext to discard the item
        var agentInventoryContext = FFXIVClientStructs.FFXIV.Client.UI.Agent.AgentInventoryContext.Instance();
        if (agentInventoryContext == null)
        {
            throw new InvalidOperationException("AgentInventoryContext is null");
        }
        
        agentInventoryContext->DiscardItem(slot, item.Container, item.Slot, 0);
    }
}
