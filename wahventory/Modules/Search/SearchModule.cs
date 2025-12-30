using System;
using System.Collections.Generic;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin.Services;
using wahventory.Core;
using wahventory.Modules.Search.Filters;
using wahventory.Modules.Search.Helpers;
using wahventory.Modules.Search.Inventories;
using wahventory.Modules.Search.Windows;

namespace wahventory.Modules.Search;

public class SearchModule : IDisposable
{
    private readonly IClientState _clientState;
    private readonly SearchBarSettings _settings;

    private readonly InventoriesManager _inventoriesManager;
    private readonly KeyBind _keybind;
    private readonly List<Filter> _filters;

    private readonly SearchBarWindow _searchBarWindow;
    private readonly SearchBarSettingsWindow _settingsWindow;

    public bool IsKeybindActive { get; private set; }

    private uint? _highlightedItemId;

    public SearchModule(
        IGameGui gameGui,
        IDataManager dataManager,
        IClientState clientState,
        IKeyState keyState,
        SearchBarSettings settings,
        WindowSystem windowSystem)
    {
        _clientState = clientState;
        _settings = settings;

        KeyboardHelper.Initialize();

        _keybind = new KeyBind(settings, keyState);

        _filters = new List<Filter>
        {
            new NameFilter(settings),
            new TypeFilter(settings),
            new JobFilter(settings),
            new LevelFilter(settings)
        };

        _inventoriesManager = new InventoriesManager(gameGui, dataManager, settings);

        _searchBarWindow = new SearchBarWindow(settings, this);
        _settingsWindow = new SearchBarSettingsWindow(settings, _keybind, _filters.ToArray());

        windowSystem.AddWindow(_searchBarWindow);
        windowSystem.AddWindow(_settingsWindow);
    }

    public void Update()
    {
        if (_clientState.LocalPlayer == null) return;

        KeyboardHelper.Instance?.Update();
        _inventoriesManager.Update();

        if (_inventoriesManager.ActiveInventory != null)
        {
            IsKeybindActive = _keybind.IsActive();
        }
        else
        {
            IsKeybindActive = false;
        }
    }

    public void Draw()
    {
        if (_clientState.LocalPlayer == null) return;

        if (_highlightedItemId.HasValue)
        {
            _inventoriesManager.HighlightItemInAll(_highlightedItemId.Value);
        }
        else if (_inventoriesManager.ActiveInventory == null)
        {
            _searchBarWindow.Inventory = null;
            _searchBarWindow.IsOpen = false;
        }
        else
        {
            _searchBarWindow.Inventory = _inventoriesManager.ActiveInventory;
            _searchBarWindow.UpdateCanShow();
            _searchBarWindow.IsOpen = _searchBarWindow.CanShow;

            _inventoriesManager.ActiveInventory.ApplyFilters(_filters, _searchBarWindow.SearchTerm);
            _inventoriesManager.ActiveInventory.UpdateHighlights();
        }
    }

    public void OpenSettings()
    {
        _settingsWindow.IsOpen = true;
    }

    public void ClearHighlights()
    {
        _inventoriesManager.ClearHighlights();
        _highlightedItemId = null;
    }

    public void HighlightItem(uint itemId)
    {
        _highlightedItemId = itemId;
    }

    public void StopHighlighting()
    {
        if (_highlightedItemId.HasValue)
        {
            _highlightedItemId = null;
            _inventoriesManager.ClearHighlights();
        }
    }

    public void Dispose()
    {
        ClearHighlights();
        _inventoriesManager.Dispose();
        KeyboardHelper.Instance?.Dispose();
    }
}
