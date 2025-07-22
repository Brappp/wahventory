using System;
using System.Numerics;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using ImGuiNET;
using wahventory.Modules.Inventory;

namespace wahventory.Windows;

public class MainWindow : Window, IDisposable
{
    private Plugin Plugin;
    private InventoryManagementModule InventoryModule;

    public MainWindow(Plugin plugin, InventoryManagementModule inventoryModule)
        : base("wahventory - Inventory Manager", ImGuiWindowFlags.None)
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(800, 400),
            MaximumSize = new Vector2(1400, float.MaxValue) // No max height limit
        };

        Plugin = plugin;
        InventoryModule = inventoryModule;
        
        // Set initial size - window will adjust based on content
        Size = new Vector2(900, 600);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public void Dispose() { }

    public override void Draw()
    {
        // Draw the inventory module interface
        InventoryModule.Draw();
    }
}
