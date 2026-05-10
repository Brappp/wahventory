using Dalamud.Game.Command;
using Dalamud.IoC;
using Dalamud.Plugin;
using System.IO;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using wahventory.Services.Helpers;
using wahventory.UI.Windows;
using wahventory.Modules.Inventory;
using wahventory.Modules.Search;
using Dalamud.Game;
using ECommons;

namespace wahventory.Core;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] private static IDalamudPluginInterface PluginInterface { get; set; } = null!;
    [PluginService] private static ICommandManager CommandManager { get; set; } = null!;
    [PluginService] private static IDataManager DataManager { get; set; } = null!;
    [PluginService] private static IFramework Framework { get; set; } = null!;
    [PluginService] private static IClientState ClientState { get; set; } = null!;
    [PluginService] private static ITextureProvider TextureProvider { get; set; } = null!;
    [PluginService] private static IPluginLog Log { get; set; } = null!;
    [PluginService] private static IGameInteropProvider GameInteropProvider { get; set; } = null!;
    [PluginService] private static IChatGui ChatGui { get; set; } = null!;
    [PluginService] private static IGameGui GameGui { get; set; } = null!;
    [PluginService] private static ICondition Condition { get; set; } = null!;
    [PluginService] private static IObjectTable ObjectTable { get; set; } = null!;
    [PluginService] private static IKeyState KeyState { get; set; } = null!;
    [PluginService] private static IPlayerState PlayerState { get; set; } = null!;

    private const string CommandName = "/wahventory";

    public IGameServices Services { get; }
    public ConfigurationManager ConfigManager { get; }
    public Configuration Configuration => ConfigManager.Configuration;
    public readonly WindowSystem WindowSystem = new("wahventory");
    private ConfigWindow ConfigWindow { get; init; }
    private MainWindow MainWindow { get; init; }
    private DiscardConfirmationWindow DiscardConfirmationWindow { get; init; }
    
    private InventoryManagementModule InventoryModule { get; init; }
    private SearchModule SearchModule { get; init; }

    public Plugin()
    {
        ECommonsMain.Init(PluginInterface, this);

        Services = new GameServices(
            PluginInterface,
            CommandManager,
            DataManager,
            Framework,
            ClientState,
            TextureProvider,
            Log,
            GameInteropProvider,
            ChatGui,
            GameGui,
            Condition,
            ObjectTable,
            KeyState,
            PlayerState);

        ConfigManager = new ConfigurationManager(Services);

        ConfigWindow = new ConfigWindow(this);
        InventoryModule = new InventoryManagementModule(this, Services);
        SearchModule = new SearchModule(
            Services.GameGui,
            Services.DataManager,
            Services.ObjectTable,
            Services.KeyState,
            Configuration.SearchBarSettings,
            WindowSystem);
        MainWindow = new MainWindow(this, InventoryModule, SearchModule);

        // Create discard confirmation window with icon cache from module
        var iconCache = new IconCache(Services.TextureProvider);
        DiscardConfirmationWindow = new DiscardConfirmationWindow(
            InventoryModule.DiscardService,
            iconCache);
        
        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(MainWindow);
        WindowSystem.AddWindow(DiscardConfirmationWindow);

        Services.CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open the wahventory window\n/wahventory auto - Execute auto-discard for configured items\n/wahventory search - Open search bar settings"
        });

        Services.PluginInterface.UiBuilder.Draw += DrawUI;
        Services.PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUI;
        Services.PluginInterface.UiBuilder.OpenMainUi += ToggleMainUI;
        Services.Framework.Update += OnFrameworkUpdate;
    }

    public void Dispose()
    {
        Services.Framework.Update -= OnFrameworkUpdate;

        WindowSystem.RemoveAllWindows();

        InventoryModule.Dispose();
        SearchModule.Dispose();

        Services.CommandManager.RemoveHandler(CommandName);
        ECommonsMain.Dispose();
    }
    
    private void OnFrameworkUpdate(IFramework framework)
    {
        InventoryModule.Update();
        SearchModule.Update();
    }

    private void OnCommand(string command, string args)
    {
        var trimmedArgs = args?.Trim().ToLower() ?? "";

        if (trimmedArgs == "auto")
        {
            InventoryModule.ExecuteAutoDiscard();
        }
        else if (trimmedArgs == "search")
        {
            SearchModule.OpenSettings();
        }
        else
        {
            ToggleMainUI();
        }
    }

    private void DrawUI()
    {
        WindowSystem.Draw();
        SearchModule.Draw();
    }

    public void ToggleConfigUI() => ConfigWindow.Toggle();
    public void ToggleMainUI() => MainWindow.Toggle();
}
