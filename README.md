# WahVentory

Advanced inventory management plugin for Final Fantasy XIV.

## Features

- **Smart Safety Filters** - 9 different protection types to safeguard valuable items
- **Market Price Integration** - Real-time gil values via Universalis
- **Bulk Discard** - Safely discard multiple items with confirmation
- **Category Organization** - Items grouped by type with collapsible sections
- **Search & Filter** - Find items quickly with text search and filters
- **Gearset Protection** - Never accidentally discard gearset items
- **Two-Tab System** - View available vs protected items separately

## Safety Filters

WahVentory includes comprehensive safety filters to protect your valuable items:

1. **Ultimate Tokens** - Raid tokens and preorder items
2. **Currency Items** - Gil, tomestones, MGP, etc.
3. **Crystals & Shards** - Crafting materials
4. **Gearset Items** - Equipment in any gearset
5. **Indisposable Items** - Items that cannot be discarded
6. **High-Level Gear** - Equipment above specified item level (default: i600+)
7. **Unique & Untradeable** - One-of-a-kind items that can't be reacquired
8. **HQ Items** - High-quality crafted items
9. **Collectables** - Turn-in items for scrips

## Usage

Use `/wahventory` to open the inventory management window.

## Building

### Prerequisites

- XIVLauncher, FFXIV, and Dalamud installed
- .NET 8 SDK
- Visual Studio 2022 or JetBrains Rider

### Build Steps

1. Clone this repository
2. Open `wahventory.sln` in your IDE
3. Build the solution
4. The plugin DLL will be in `wahventory/bin/x64/Debug/wahventory.dll`

### Installing for Development

1. In-game, use `/xlsettings` to open Dalamud settings
2. Go to Experimental, add the full path to `wahventory.dll` to Dev Plugin Locations
3. Use `/xlplugins` to open Plugin Installer
4. Go to Dev Tools > Installed Dev Plugins and enable WahVentory

## Configuration

Access the configuration window through the settings button in the main window. You can:

- Toggle individual safety filters
- Set market price cache duration
- Configure auto-refresh for prices
- Adjust item level thresholds
- Set spiritbond thresholds

## License

This project is licensed under AGPL-3.0-or-later.

## Acknowledgments

Based on the Dalamud plugin template and inspired by various inventory management solutions.
