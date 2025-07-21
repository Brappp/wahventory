# WahVentory Runtime Fix - NullReferenceException in TaskManager

## Issue
The plugin was throwing a `NullReferenceException` when creating the `TaskManager` from ECommons:
```
System.NullReferenceException: Object reference not set to an instance of an object.
   at ECommons.Automation.NeoTaskManager.TaskManager..ctor(TaskManagerConfiguration defaultConfiguration)
```

## Root Cause
ECommons requires initialization before any of its components can be used. The `TaskManager` was being created in the `InventoryManagementModule` constructor before ECommons was initialized.

## Solution
Added proper ECommons initialization in the Plugin constructor:

1. **Added ECommons using statement** in Plugin.cs:
   ```csharp
   using ECommons;
   ```

2. **Initialize ECommons** at the start of the Plugin constructor:
   ```csharp
   public Plugin()
   {
       // Initialize ECommons
       ECommonsMain.Init(PluginInterface, this);
       
       // ... rest of initialization
   }
   ```

3. **Dispose ECommons** in the Plugin's Dispose method:
   ```csharp
   public void Dispose()
   {
       // ... other cleanup
       
       // Uninitialize ECommons
       ECommonsMain.Dispose();
   }
   ```

## Files Modified
- `F:\Github\wahventory\wahventory\Plugin.cs`

## Important Note
ECommons must be initialized before creating any ECommons components like TaskManager. The initialization must happen in the main Plugin constructor, not in module constructors.
