using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dalamud.Plugin;
using Newtonsoft.Json;

namespace wahventory;

public class ConfigurationManager
{
    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly string _configDirectory;
    private Configuration _configuration;
    
    private const string CONFIG_FILE = "config.json";
    private const string BLACKLIST_FILE = "blacklist.json";
    private const string AUTODISCARD_FILE = "autodiscard.json";
    
    public Configuration Configuration => _configuration;
    
    public ConfigurationManager(IDalamudPluginInterface pluginInterface)
    {
        _pluginInterface = pluginInterface;
        _configDirectory = Path.Combine(_pluginInterface.ConfigDirectory.FullName, "wahventory");
        
        // Ensure directory exists
        if (!Directory.Exists(_configDirectory))
        {
            Directory.CreateDirectory(_configDirectory);
        }
        
        // Load configuration
        LoadConfiguration();
        
        // Migrate from old format if needed
        MigrateIfNeeded();
    }
    
    private void LoadConfiguration()
    {
        // Try to load from new location first
        var configPath = Path.Combine(_configDirectory, CONFIG_FILE);
        if (File.Exists(configPath))
        {
            try
            {
                var json = File.ReadAllText(configPath);
                _configuration = JsonConvert.DeserializeObject<Configuration>(json) ?? new Configuration();
            }
            catch (Exception ex)
            {
                _configuration = new Configuration();
            }
        }
        else
        {
            // Try old location
            _configuration = _pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        }
        
        // Hook up the Save method
        _configuration.Save = SaveConfiguration;
    }
    
    private void MigrateIfNeeded()
    {
        var blacklistPath = Path.Combine(_configDirectory, BLACKLIST_FILE);
        var autodiscardPath = Path.Combine(_configDirectory, AUTODISCARD_FILE);
        
        // Check if we need to migrate (files don't exist but we have data in config)
        bool needsMigration = false;
        
        if (!File.Exists(blacklistPath) && _configuration.InventorySettings.BlacklistedItems?.Count > 0)
        {
            SaveBlacklist(_configuration.InventorySettings.BlacklistedItems);
            needsMigration = true;
        }
        
        if (!File.Exists(autodiscardPath) && _configuration.InventorySettings.AutoDiscardItems?.Count > 0)
        {
            SaveAutoDiscard(_configuration.InventorySettings.AutoDiscardItems);
            needsMigration = true;
        }
        
        if (needsMigration)
        {
            // Clear the lists from main config after migration
            _configuration.InventorySettings.BlacklistedItems = new HashSet<uint>();
            _configuration.InventorySettings.AutoDiscardItems = new HashSet<uint>();
            SaveConfiguration();
        }
    }
    
    public void SaveConfiguration()
    {
        try
        {
            var configPath = Path.Combine(_configDirectory, CONFIG_FILE);
            var json = JsonConvert.SerializeObject(_configuration, Formatting.Indented);
            File.WriteAllText(configPath, json);
        }
        catch (Exception ex)
        {
            // Failed to save configuration
        }
    }
    
    public HashSet<uint> LoadBlacklist()
    {
        var path = Path.Combine(_configDirectory, BLACKLIST_FILE);
        if (!File.Exists(path))
            return new HashSet<uint>();
        
        try
        {
            var json = File.ReadAllText(path);
            var data = JsonConvert.DeserializeObject<ListData>(json);
            return data?.Items?.ToHashSet() ?? new HashSet<uint>();
        }
        catch (Exception ex)
        {
            return new HashSet<uint>();
        }
    }
    
    public void SaveBlacklist(HashSet<uint> items)
    {
        try
        {
            var path = Path.Combine(_configDirectory, BLACKLIST_FILE);
            var data = new ListData
            {
                Version = 1,
                Items = items.ToList()
            };
            var json = JsonConvert.SerializeObject(data, Formatting.Indented);
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            // Failed to save blacklist
        }
    }
    
    public HashSet<uint> LoadAutoDiscard()
    {
        var path = Path.Combine(_configDirectory, AUTODISCARD_FILE);
        if (!File.Exists(path))
            return new HashSet<uint>();
        
        try
        {
            var json = File.ReadAllText(path);
            var data = JsonConvert.DeserializeObject<ListData>(json);
            return data?.Items?.ToHashSet() ?? new HashSet<uint>();
        }
        catch (Exception ex)
        {
            return new HashSet<uint>();
        }
    }
    
    public void SaveAutoDiscard(HashSet<uint> items)
    {
        try
        {
            var path = Path.Combine(_configDirectory, AUTODISCARD_FILE);
            var data = new ListData
            {
                Version = 1,
                Items = items.ToList()
            };
            var json = JsonConvert.SerializeObject(data, Formatting.Indented);
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            // Failed to save auto-discard list
        }
    }
    
    private class ListData
    {
        public int Version { get; set; } = 1;
        public List<uint> Items { get; set; } = new();
    }
} 