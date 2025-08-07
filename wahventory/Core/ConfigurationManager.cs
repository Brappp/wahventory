using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Newtonsoft.Json;

namespace wahventory.Core;

public class ConfigurationManager
{
    private readonly IDalamudPluginInterface _pluginInterface;
    private readonly string _configDirectory;
    private Configuration _configuration = null!;
    private readonly IPluginLog _log;
    
    private const string CONFIG_FILE = "config.json";
    private const string BLACKLIST_FILE = "blacklist.json";
    private const string AUTODISCARD_FILE = "autodiscard.json";
    
    public Configuration Configuration => _configuration;
    
    public ConfigurationManager(IDalamudPluginInterface pluginInterface)
    {
        _pluginInterface = pluginInterface;
        _configDirectory = Path.Combine(_pluginInterface.ConfigDirectory.FullName, "wahventory");
        _log = Plugin.Log;
        
        if (!Directory.Exists(_configDirectory))
        {
            Directory.CreateDirectory(_configDirectory);
        }
        
        LoadConfiguration();
    }
    
    private void LoadConfiguration()
    {
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
                _log.Error(ex, "Failed to load configuration");
                _configuration = new Configuration();
            }
        }
        else
        {
            _configuration = _pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        }
        
        _configuration.Save = SaveConfiguration;
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
            _log.Error(ex, "Failed to save configuration");
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
            _log.Error(ex, "Failed to load blacklist");
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
            _log.Error(ex, "Failed to save blacklist");
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
            _log.Error(ex, "Failed to load auto-discard list");
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
            _log.Error(ex, "Failed to save auto-discard list");
        }
    }
    
    private class ListData
    {
        public int Version { get; set; } = 1;
        public List<uint> Items { get; set; } = new();
    }
}
