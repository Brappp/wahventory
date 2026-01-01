# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

WahVentory is a Dalamud plugin for Final Fantasy XIV that provides advanced inventory management features including item filtering, market price lookups, blacklist/auto-discard lists, and automated item discarding.

## Build Commands

```bash
# Build the plugin (from solution directory)
dotnet build WahVentory.sln

# Build release
dotnet build WahVentory.sln -c Release

# The plugin DLL outputs to wahventory/bin/x64/Release/
```

## Architecture

### Core Structure

- **Core/Plugin.cs**: Main plugin entry point, initializes Dalamud services via `[PluginService]` attributes, sets up window system and modules
- **Core/Configuration.cs**: Serializable settings classes (`Configuration`, `InventorySettings`, `SearchBarSettings`, `SafetyFilters`)
- **Core/ConfigurationManager.cs**: Handles JSON persistence for config, blacklist, and auto-discard lists to `%appdata%/XIVLauncher/pluginConfigs/wahventory/`

### Modules

The plugin has two main modules:

1. **InventoryManagementModule** (Modules/Inventory/)
   - Split into `InventoryManagementModule.cs` (logic) and `InventoryManagementModule.UI.cs` (ImGui rendering)
   - Manages inventory scanning, filtering, selection state, and discard operations
   - Uses services for specific functionality

2. **SearchModule** (Modules/Search/)
   - Provides in-game inventory search bar overlay
   - Handles keyboard shortcuts and filter application
   - `Inventories/` subfolder contains different inventory type handlers (Normal, Large, Chocobo, Retainer, Armoury)
   - `Filters/` subfolder contains filter implementations (Name, Type, Job, Level)

### Services (Services/)

- **DiscardService**: Handles automated item discarding using ECommons TaskManager
- **PriceService**: Manages market price caching and fetch scheduling
- **PassiveDiscardService**: Background auto-discard when player is idle in safe zones
- **ItemFilterService**: Applies safety filters and categorizes items
- **ItemSearchService**: Item database search functionality
- **External/UniversalisClient**: HTTP client for Universalis market API

### UI (UI/)

- **Windows/**: Dalamud `Window` implementations (MainWindow, ConfigWindow, DiscardConfirmationWindow)
- **Components/**: Reusable ImGui components (FilterPanelComponent, ItemTableComponent, SearchComponent)

## Key Dependencies

- **Dalamud.NET.Sdk v14**: Dalamud plugin SDK for FFXIV
- **ECommons**: Utility library providing TaskManager for async game operations
- **Lumina**: Game data access (Excel sheets for items, worlds, etc.)
- **FFXIVClientStructs**: Native game struct definitions for inventory/UI interaction

## Important Patterns

### Thread Safety
The `InventoryManagementModule` uses `_stateLock` object for thread-safe access to item collections since price fetches run async.

### Game UI Interaction
`DiscardService` interacts with game UI through `AtkUnitBase` pointers to find and click dialog buttons. Uses `IGameGui.GetAddonByName()` to locate UI elements.

### Configuration Persistence
Config saves are debounced with `_configSaveInterval` to avoid excessive file writes during UI interaction.

## Commands

- `/wahventory` - Opens main window
- `/wahventory auto` - Executes auto-discard for configured items
- `/wahventory search` - Opens search bar settings
