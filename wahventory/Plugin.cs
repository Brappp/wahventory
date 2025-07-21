using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using System.IO;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using ECommons;
using WahVentory.Windows;
using WahVentory.Modules.Inventory;

namespace WahVentory;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;

    private const string CommandName = "/wahventory";

    public Configuration Configuration { get; init; }

    public readonly WindowSystem WindowSystem = new("WahVentory");
    private ConfigWindow ConfigWindow { get; init; }
    private MainWindow MainWindow { get; init; }
    private InventoryManagementModule InventoryModule { get; init; }

    public Plugin()
    {
        // Initialize ECommons
        ECommonsMain.Init(PluginInterface, this);
        
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        // Initialize modules
        InventoryModule = new InventoryManagementModule(this);
        InventoryModule.Initialize();

        ConfigWindow = new ConfigWindow(this);
        MainWindow = new MainWindow(this, InventoryModule);

        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(MainWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Opens the WahVentory inventory management window"
        });

        PluginInterface.UiBuilder.Draw += DrawUI;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUI;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUI;
        
        // Register framework update for inventory module
        Framework.Update += OnFrameworkUpdate;

        Log.Information($"WahVentory initialized successfully!");
    }

    public void Dispose()
    {
        Framework.Update -= OnFrameworkUpdate;
        
        WindowSystem.RemoveAllWindows();

        ConfigWindow.Dispose();
        MainWindow.Dispose();
        InventoryModule.Dispose();

        CommandManager.RemoveHandler(CommandName);
        
        // Uninitialize ECommons
        ECommonsMain.Dispose();
    }

    private void OnCommand(string command, string args)
    {
        ToggleMainUI();
    }

    private void DrawUI() => WindowSystem.Draw();

    public void ToggleConfigUI() => ConfigWindow.Toggle();
    public void ToggleMainUI() => MainWindow.Toggle();
    
    private void OnFrameworkUpdate(IFramework framework)
    {
        InventoryModule.Update();
    }
}
