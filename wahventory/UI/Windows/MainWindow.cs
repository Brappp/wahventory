using System.Numerics;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using wahventory.Modules.Inventory;
using wahventory.Core;

namespace wahventory.UI.Windows;

public class MainWindow : Window
{
    private Plugin Plugin;
    private InventoryManagementModule InventoryModule;

    public MainWindow(Plugin plugin, InventoryManagementModule inventoryModule)
        : base("wahventory - Inventory Manager", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(800, 400),
            MaximumSize = new Vector2(1400, float.MaxValue)
        };

        Plugin = plugin;
        InventoryModule = inventoryModule;

        Size = new Vector2(900, 600);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public override void Draw()
    {
        InventoryModule.Draw();
    }
}
