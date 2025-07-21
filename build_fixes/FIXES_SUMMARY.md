# WahVentory Build Fixes Summary

This document summarizes all the fixes applied to resolve the build errors in the WahVentory project.

## Issues Fixed

### 1. **InventoryHelpers.cs** - Item/ItemUICategory nullable reference issues (CS1061)
- **Problem**: Code was treating `Item` and `ItemUICategory` as nullable types with `.HasValue` and `.Value` properties
- **Solution**: Removed nullable handling since Lumina Excel sheets return structs directly. Changed from `item.HasValue` checks to `item.RowId == 0` checks, and removed all `.Value` accessors
- **Also fixed**: Removed reference to non-existent `Spiritbond` property on `InventoryItem` struct

### 2. **InventoryHelpers.cs** - InventoryType.InventoryLarge doesn't exist (CS0117)
- **Problem**: Referenced a non-existent inventory type
- **Solution**: Changed to use `AgentInventoryContext.Instance()->DiscardItem()` method instead of trying to move items to a non-existent inventory

### 3. **InventoryManagementModule_Discard.cs** - Static member access issues (CS0176)
- **Problem**: Accessing static members (`Log`, `ChatGui`, `GameGui`) through instance reference `_plugin`
- **Solution**: Changed all `_plugin.Log` to `Plugin.Log`, `_plugin.ChatGui` to `Plugin.ChatGui`, etc.

### 4. **ConfigWindow.cs** - Property ref issues (CS0206)
- **Problem**: Properties cannot be passed as `ref` parameters to ImGui methods
- **Solution**: Created local variables as intermediaries for all ImGui checkboxes and inputs:
  ```csharp
  // Before:
  ImGui.Checkbox("Setting", ref Configuration.SomeBoolProperty);
  
  // After:
  var value = Configuration.SomeBoolProperty;
  if (ImGui.Checkbox("Setting", ref value))
  {
      Configuration.SomeBoolProperty = value;
      changed = true;
  }
  ```

### 5. **InventoryManagementModule_UI.cs** - Missing UniversalisClient type (CS0246)
- **Problem**: Missing using statement for `UniversalisClient`
- **Solution**: Added `using WahVentory.External;` and fixed the log access pattern

## Files Modified

1. `F:\Github\wahventory\wahventory\Helpers\InventoryHelpers.cs`
2. `F:\Github\wahventory\wahventory\Modules\Inventory\InventoryManagementModule_Discard.cs`
3. `F:\Github\wahventory\wahventory\Windows\ConfigWindow.cs`
4. `F:\Github\wahventory\wahventory\Modules\Inventory\InventoryManagementModule_UI.cs`

## Build Instructions

After applying these fixes, the project should build successfully. Make sure you have:
1. The required FFXIVClientStructs NuGet package
2. The required Dalamud references
3. The required Lumina references

## Note on Code Patterns

The fixes reveal some important patterns for FFXIV plugin development:
- Lumina Excel sheets return structs directly, not nullable types
- Static services in plugins should be accessed via the type name, not instance references
- ImGui requires ref parameters, which means properties need local variable intermediaries
- For discarding items, use `AgentInventoryContext` rather than trying to move items to special inventories
