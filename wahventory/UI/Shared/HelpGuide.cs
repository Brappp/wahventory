using System;
using System.Numerics;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Bindings.ImGui;
using wahventory.Core;

namespace wahventory.Modules.Inventory;

public partial class InventoryManagementModule
{
    private bool _showHelpWindow = false;
    private int _selectedHelpTab = 0;
    
    /// <summary>
    /// Draw the help button in the top bar
    /// </summary>
    private void DrawHelpButton()
    {
        using (var colors = ImRaii.PushColor(ImGuiCol.Button, new Vector4(0.2f, 0.4f, 0.6f, 0.6f))
                                  .Push(ImGuiCol.ButtonHovered, new Vector4(0.3f, 0.5f, 0.7f, 0.7f)))
        {
            using (var font = ImRaii.PushFont(UiBuilder.IconFont))
            {
                if (ImGui.Button(FontAwesomeIcon.QuestionCircle.ToIconString() + "##Help"))
                {
                    _showHelpWindow = !_showHelpWindow;
                }
            }
        }
        
        if (ImGui.IsItemHovered())
        {
            using (var tooltip = ImRaii.Tooltip())
            {
                ImGui.TextColored(ColorSuccess, "Need Help?");
                ImGui.Text("Click to open the comprehensive guide");
                ImGui.Text("Learn about all features and safety systems");
            }
        }
    }
    
    /// <summary>
    /// Draw the comprehensive help window with nice formatting
    /// </summary>
    private void DrawHelpWindow()
    {
        if (!_showHelpWindow)
            return;
        
        var windowSize = new Vector2(850, 600);
        ImGui.SetNextWindowSize(windowSize, ImGuiCond.FirstUseEver);
        
        using var windowColors = ImRaii.PushColor(ImGuiCol.TitleBgActive, new Vector4(0.2f, 0.4f, 0.6f, 0.9f));
        
        if (ImGui.Begin("📚 wahventory User Guide###WahventoryHelp", ref _showHelpWindow, 
            ImGuiWindowFlags.NoCollapse))
        {
            // Stylish header
            DrawHelpHeader();
            
            ImGui.Separator();
            
            // Tab bar for different help sections
            using (var tabBar = ImRaii.TabBar("HelpTabs"))
            {
                if (tabBar)
                {
                    DrawQuickStartTab();
                    DrawFeaturesTab();
                    DrawSafetyTab();
                    DrawTipsTab();
                    DrawFAQTab();
                }
            }
            
            ImGui.End();
        }
    }
    
    private void DrawHelpHeader()
    {
        var windowWidth = ImGui.GetContentRegionAvail().X;
        
        // Center the title
        var title = "wahventory User Guide";
        var titleSize = ImGui.CalcTextSize(title);
        ImGui.SetCursorPosX((windowWidth - titleSize.X) * 0.5f);
        ImGui.TextColored(new Vector4(0.3f, 0.7f, 1.0f, 1f), title);
        
        var subtitle = "Your complete guide to safe and efficient inventory management";
        var subtitleSize = ImGui.CalcTextSize(subtitle);
        ImGui.SetCursorPosX((windowWidth - subtitleSize.X) * 0.5f);
        ImGui.TextColored(ColorInfo, subtitle);
        
        ImGui.Spacing();
    }
    
    private void DrawQuickStartTab()
    {
        using (var tab = ImRaii.TabItem("🚀 Quick Start"))
        {
            if (tab)
            {
                ImGui.Spacing();
                DrawStyledSection("Welcome to wahventory!", ColorSuccess, () =>
                {
                    ImGui.TextWrapped("wahventory helps you manage your FFXIV inventory safely and efficiently. " +
                        "Here's how to get started in 3 simple steps:");
                });
                
                ImGui.Spacing();
                
                // Step 1
                DrawNumberedStep(1, "Review Your Filters", () =>
                {
                    ImGui.TextWrapped("Check the safety filters at the top of the window. " +
                        "These protect your valuable items. We recommend keeping these ON:");
                    ImGui.Spacing();
                    DrawCheckmark("Gearset Items - Protects equipment in your gear sets");
                    DrawCheckmark("Ultimate Tokens - Protects raid tokens and special items");
                    DrawCheckmark("High Level Gear - Protects gear above specified item level");
                });
                
                // Step 2
                DrawNumberedStep(2, "Search for Items", () =>
                {
                    ImGui.TextWrapped("Use the search bar to find items across ALL your inventories:");
                    ImGui.Spacing();
                    DrawBullet("Type any part of an item name");
                    DrawBullet("Press Enter to search everywhere");
                    DrawBullet("Click 'View Results' to see all matches");
                    DrawBullet("Right-click any item for quick actions");
                });
                
                // Step 3
                DrawNumberedStep(3, "Manage Your Items", () =>
                {
                    ImGui.TextWrapped("Select items and use the action buttons at the bottom:");
                    ImGui.Spacing();
                    DrawBullet("Select items with checkboxes");
                    DrawBullet("Use 'Select All' for entire categories");
                    DrawBullet("Add frequently discarded items to Auto-Discard");
                    DrawBullet("Blacklist items you never want to see");
                });
                
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();
                
                DrawStyledSection("⚠️ Safety First!", ColorWarning, () =>
                {
                    ImGui.TextWrapped("wahventory has multiple layers of protection:");
                    ImGui.Spacing();
                    DrawCheckmark("Gear set detection prevents discarding equipped items", ColorSuccess);
                    DrawCheckmark("Built-in blacklist for ultimate tokens and special items", ColorSuccess);
                    DrawCheckmark("Confirmation required for all discard actions", ColorSuccess);
                    DrawCheckmark("Visual indicators for valuable items", ColorSuccess);
                });
            }
        }
    }
    
    private void DrawFeaturesTab()
    {
        using (var tab = ImRaii.TabItem("✨ Features"))
        {
            if (tab)
            {
                ImGui.Spacing();
                
                DrawFeatureCard("🔍 Universal Search", ColorInfo, () =>
                {
                    ImGui.TextWrapped("Search across all your inventories at once:");
                    ImGui.Spacing();
                    DrawFeaturePoint("Searches main inventory, armory, saddlebags");
                    DrawFeaturePoint("Includes all tracked retainers");
                    DrawFeaturePoint("Shows exact location of each item");
                    DrawFeaturePoint("Filters current tab as you type");
                });
                
                DrawFeatureCard("🖱️ Right-Click Menu", ColorSuccess, () =>
                {
                    ImGui.TextWrapped("Quick actions on any item:");
                    ImGui.Spacing();
                    DrawFeaturePoint("Check current market price");
                    DrawFeaturePoint("Open item on Universalis");
                    DrawFeaturePoint("Add/remove from blacklist");
                    DrawFeaturePoint("Add/remove from auto-discard");
                    DrawFeaturePoint("Find all copies of the item");
                    DrawFeaturePoint("Copy item name or ID");
                });
                
                DrawFeatureCard("💰 Market Integration", ColorPrice, () =>
                {
                    ImGui.TextWrapped("See item values at a glance:");
                    ImGui.Spacing();
                    DrawFeaturePoint("Auto-fetch prices while scrolling");
                    DrawFeaturePoint("Cached for 5 minutes");
                    DrawFeaturePoint("Shows total inventory value");
                    DrawFeaturePoint("Color-coded price indicators");
                });
                
                DrawFeatureCard("🤖 Auto-Discard", ColorWarning, () =>
                {
                    ImGui.TextWrapped("Automatically discard specified items when idle:");
                    ImGui.Spacing();
                    DrawFeaturePoint("Configure idle time threshold");
                    DrawFeaturePoint("Set discard intervals");
                    DrawFeaturePoint("Manage auto-discard list");
                    DrawFeaturePoint("Visual countdown indicator");
                });
                
                DrawFeatureCard("📊 Item Tracker", ColorBlue, () =>
                {
                    ImGui.TextWrapped("Track items across characters and retainers:");
                    ImGui.Spacing();
                    DrawFeaturePoint("Auto-scans when you open retainers");
                    DrawFeaturePoint("Shows last update time");
                    DrawFeaturePoint("Search across all tracked inventories");
                    DrawFeaturePoint("Find items on other characters");
                });
            }
        }
    }
    
    private void DrawSafetyTab()
    {
        using (var tab = ImRaii.TabItem("🛡️ Safety"))
        {
            if (tab)
            {
                ImGui.Spacing();
                
                DrawStyledSection("Protection Layers", ColorSuccess, () =>
                {
                    ImGui.TextWrapped("wahventory uses multiple safety systems to protect your items:");
                });
                
                ImGui.Spacing();
                
                // Visual safety pyramid
                DrawSafetyLayer(1, "Always Protected", ColorError, () =>
                {
                    ImGui.TextWrapped("These items can NEVER be shown for discard:");
                    DrawProtectedItem("Ultimate raid tokens (UCOB, UWU, TEA, DSR, TOP)");
                    DrawProtectedItem("Currency items (IDs 1-99)");
                    DrawProtectedItem("Pre-order and special edition items");
                    DrawProtectedItem("Achievement rewards");
                });
                
                DrawSafetyLayer(2, "Filter Protected", ColorWarning, () =>
                {
                    ImGui.TextWrapped("Protected when filters are enabled:");
                    DrawProtectedItem("Items in gear sets", true);
                    DrawProtectedItem("High-level gear (configurable threshold)");
                    DrawProtectedItem("Unique & untradeable items");
                    DrawProtectedItem("High Quality items");
                    DrawProtectedItem("Collectables");
                });
                
                DrawSafetyLayer(3, "User Protected", ColorInfo, () =>
                {
                    ImGui.TextWrapped("Your personal protection lists:");
                    DrawProtectedItem("Blacklisted items (never shown)");
                    DrawProtectedItem("Items you manually exclude");
                    DrawProtectedItem("Custom filter rules");
                });
                
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();
                
                DrawStyledSection("Visual Indicators", ColorInfo, () =>
                {
                    ImGui.Text("Learn to recognize protected items:");
                    ImGui.Spacing();
                    
                    // Show actual colored examples
                    DrawColorExample(new Vector4(0.5f, 0.4f, 0.6f, 0.2f), "[Gear Set]", 
                        "Purple background - Item is in a gear set");
                    DrawColorExample(new Vector4(0.3f, 0.1f, 0.1f, 0.3f), "[Blacklisted]", 
                        "Red background - Item is blacklisted");
                    DrawColorExample(new Vector4(0.3f, 0.5f, 0.7f, 0.3f), "[Selected]", 
                        "Blue background - Item is selected");
                });
            }
        }
    }
    
    private void DrawTipsTab()
    {
        using (var tab = ImRaii.TabItem("💡 Pro Tips"))
        {
            if (tab)
            {
                ImGui.Spacing();
                
                DrawTipSection("🎯 Efficiency Tips", ColorSuccess, new[]
                {
                    "Right-click items instead of navigating through tabs - it's much faster!",
                    "Use 'Select All' on safe categories like Dyes or Miscellany",
                    "Enable market prices to spot valuable items instantly",
                    "Add your common trash items to Auto-Discard for hands-free cleaning",
                    "Search for partial names - 'pot' finds all potions",
                    "Check Item Tracker before buying - you might have it on another character!"
                });
                
                DrawTipSection("⚡ Power User Tips", ColorInfo, new[]
                {
                    "The search bar filters the current tab in real-time as you type",
                    "Press Enter in search to find items everywhere",
                    "Market prices auto-refresh every 5 minutes",
                    "Use the Protected Items tab to audit your filters",
                    "Export your blacklist to share with friends",
                    "Categories remember their expanded/collapsed state"
                });
                
                DrawTipSection("🔒 Safety Tips", ColorWarning, new[]
                {
                    "ALWAYS keep 'Gearset Items' filter enabled",
                    "Review Protected Items tab after changing filters",
                    "Use Blacklist for one-of-a-kind items you treasure",
                    "Be extra careful with 'Select All' in equipment categories",
                    "Check item tags before discarding ([Gear Set], [HQ], etc.)",
                    "When in doubt, search for the item to see all copies"
                });
                
                DrawTipSection("🎨 Visual Cues", ColorBlue, new[]
                {
                    "Purple = Gear Set item (equipped in a saved set)",
                    "Red = Blacklisted (will never appear in Available Items)",
                    "Blue = Selected (ready for action)",
                    "Gold text = Market price available",
                    "Gray text = Untradeable or no market data",
                    "Warning icon = Potentially dangerous category"
                });
            }
        }
    }
    
    private void DrawFAQTab()
    {
        using (var tab = ImRaii.TabItem("❓ FAQ"))
        {
            if (tab)
            {
                ImGui.Spacing();
                
                DrawFAQItem(
                    "Why don't I see some of my items?",
                    "Items may be hidden by safety filters or in your blacklist. " +
                    "Check the 'Protected Items' tab to see what's being filtered. " +
                    "Also make sure 'Armory' is checked if you want to see equipped items.",
                    ColorInfo
                );
                
                DrawFAQItem(
                    "What's the difference between Blacklist and Auto-Discard?",
                    "Blacklist: Items NEVER appear in Available Items - they're completely hidden.\n" +
                    "Auto-Discard: Items appear normally but are automatically discarded when you're idle.",
                    ColorWarning
                );
                
                DrawFAQItem(
                    "How do I see items on my retainers?",
                    "Go to the 'Item Tracker' tab and open each retainer in-game. " +
                    "The tracker automatically scans when you open retainer inventories. " +
                    "Then you can search across all tracked inventories!",
                    ColorSuccess
                );
                
                DrawFAQItem(
                    "Is it safe to use 'Select All'?",
                    "Yes, but be careful in equipment categories! " +
                    "It's very safe in categories like Dyes, Miscellany, or Materials. " +
                    "Always review what's selected before clicking Discard.",
                    ColorWarning
                );
                
                DrawFAQItem(
                    "How do market prices work?",
                    "Enable 'Show Prices' to see market values. " +
                    "Prices auto-fetch as you scroll (when enabled). " +
                    "Click the $ button to manually check. " +
                    "Prices are cached for 5 minutes to reduce server load.",
                    ColorPrice
                );
                
                DrawFAQItem(
                    "Can I undo a discard?",
                    "NO! Discarding is permanent in FFXIV. " +
                    "There is no way to recover discarded items. " +
                    "That's why wahventory has so many safety features - use them!",
                    ColorError
                );
                
                DrawFAQItem(
                    "What are the purple items with '=' signs?",
                    "These are items in your gear sets! " +
                    "The purple background and [Gear Set] tag indicate the item is part of a saved gear set. " +
                    "These are protected by default when the Gearset filter is on.",
                    new Vector4(0.7f, 0.5f, 0.9f, 1f)
                );
                
                DrawFAQItem(
                    "How do I report a bug or request a feature?",
                    "Visit the wahventory GitHub repository to report issues or request features. " +
                    "Include as much detail as possible about what happened. " +
                    "Check if someone else already reported the same issue first!",
                    ColorInfo
                );
            }
        }
    }
    
    // Helper methods for nice formatting
    private void DrawStyledSection(string title, Vector4 color, Action content)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(10, 5));
        ImGui.PushStyleColor(ImGuiCol.Header, color with { W = 0.3f });
        
        if (ImGui.CollapsingHeader(title, ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Indent();
            ImGui.Spacing();
            content();
            ImGui.Spacing();
            ImGui.Unindent();
        }
        
        ImGui.PopStyleColor();
        ImGui.PopStyleVar();
    }
    
    private void DrawNumberedStep(int number, string title, Action content)
    {
        ImGui.Spacing();
        
        // Draw circle with number
        var drawList = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var center = pos + new Vector2(15, 15);
        drawList.AddCircleFilled(center, 15, ImGui.GetColorU32(ColorSuccess));
        drawList.AddText(center - new Vector2(5, 8), ImGui.GetColorU32(Vector4.One), number.ToString());
        
        ImGui.Dummy(new Vector2(35, 30));
        ImGui.SameLine();
        
        using (var group = ImRaii.Group())
        {
            ImGui.TextColored(ColorSuccess, title);
            ImGui.Spacing();
            content();
        }
        
        ImGui.Spacing();
    }
    
    private void DrawFeatureCard(string title, Vector4 color, Action content)
    {
        ImGui.Spacing();
        
        using (var style = ImRaii.PushStyle(ImGuiStyleVar.ChildBorderSize, 1f))
        using (var colors = ImRaii.PushColor(ImGuiCol.Border, color with { W = 0.5f }))
        using (var child = ImRaii.Child($"##{title}", new Vector2(-1, 0), true, 
            ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoScrollbar))
        {
            ImGui.TextColored(color, title);
            ImGui.Separator();
            ImGui.Spacing();
            content();
            ImGui.Spacing();
        }
        
        ImGui.Spacing();
    }
    
    private void DrawSafetyLayer(int level, string title, Vector4 color, Action content)
    {
        ImGui.Spacing();
        
        // Draw pyramid-like indentation
        var indent = (level - 1) * 20f;
        ImGui.Indent(indent);
        
        using (var colors = ImRaii.PushColor(ImGuiCol.Text, color))
        {
            ImGui.Text($"Level {level}: {title}");
        }
        
        ImGui.Separator();
        ImGui.Spacing();
        content();
        
        ImGui.Unindent(indent);
        ImGui.Spacing();
    }
    
    private void DrawTipSection(string title, Vector4 color, string[] tips)
    {
        ImGui.Spacing();
        ImGui.TextColored(color, title);
        ImGui.Separator();
        ImGui.Spacing();
        
        foreach (var tip in tips)
        {
            DrawBullet(tip);
        }
        
        ImGui.Spacing();
    }
    
    private void DrawFAQItem(string question, string answer, Vector4 color)
    {
        ImGui.Spacing();
        
        using (var colors = ImRaii.PushColor(ImGuiCol.Text, color))
        {
            ImGui.Text($"Q: {question}");
        }
        
        ImGui.Indent();
        ImGui.TextWrapped($"A: {answer}");
        ImGui.Unindent();
        
        ImGui.Spacing();
        ImGui.Separator();
    }
    
    private void DrawCheckmark(string text, Vector4? color = null)
    {
        using (var font = ImRaii.PushFont(UiBuilder.IconFont))
        {
            ImGui.TextColored(color ?? ColorSuccess, FontAwesomeIcon.CheckCircle.ToIconString());
        }
        ImGui.SameLine();
        ImGui.Text(text);
    }
    
    private void DrawBullet(string text)
    {
        ImGui.Bullet();
        ImGui.SameLine();
        ImGui.TextWrapped(text);
    }
    
    private void DrawFeaturePoint(string text)
    {
        using (var font = ImRaii.PushFont(UiBuilder.IconFont))
        {
            ImGui.TextColored(ColorSuccess, FontAwesomeIcon.ChevronRight.ToIconString());
        }
        ImGui.SameLine();
        ImGui.Text(text);
    }
    
    private void DrawProtectedItem(string text, bool recommended = false)
    {
        using (var font = ImRaii.PushFont(UiBuilder.IconFont))
        {
            ImGui.TextColored(ColorWarning, FontAwesomeIcon.ShieldAlt.ToIconString());
        }
        ImGui.SameLine();
        ImGui.Text(text);
        if (recommended)
        {
            ImGui.SameLine();
            ImGui.TextColored(ColorSuccess, "[RECOMMENDED ON]");
        }
    }
    
    private void DrawColorExample(Vector4 bgColor, string tag, string description)
    {
        var drawList = ImGui.GetWindowDrawList();
        var pos = ImGui.GetCursorScreenPos();
        var size = new Vector2(100, 25);
        
        // Draw background
        drawList.AddRectFilled(pos, pos + size, ImGui.GetColorU32(bgColor));
        drawList.AddRect(pos, pos + size, ImGui.GetColorU32(ImGuiCol.Border));
        
        // Draw text in center
        var textSize = ImGui.CalcTextSize(tag);
        var textPos = pos + (size - textSize) * 0.5f;
        drawList.AddText(textPos, ImGui.GetColorU32(new Vector4(0.7f, 0.5f, 0.9f, 1f)), tag);
        
        ImGui.Dummy(size);
        ImGui.SameLine();
        ImGui.Text($"- {description}");
    }
}
