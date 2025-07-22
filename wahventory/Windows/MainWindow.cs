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
            MinimumSize = new Vector2(800, 600),
            MaximumSize = new Vector2(float.MaxValue, float.MaxValue)
        };

        Plugin = plugin;
        InventoryModule = inventoryModule;
        
        Size = new Vector2(900, 700);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public void Dispose() { }

    public override void Draw()
    {
        // Draw the inventory module interface
        InventoryModule.Draw();
    }
}
