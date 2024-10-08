﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media.Imaging;
using Launcher.Extensions;
using Launcher.Forms;
using LauncherGamePlugin;
using LauncherGamePlugin.Commands;
using LauncherGamePlugin.Enums;
using LauncherGamePlugin.Extensions;

namespace Launcher.Views;

public partial class MainView : UserControlExt<MainView>
{
    //[Binding(nameof(PluginMenu), "Items")] public List<TemplatedControl> MenuItems => GenerateMenuItems();

    //[Binding(nameof(ProfileMenu), "Items")]
    public List<TemplatedControl> BootProfileItems =>
        _app.Launcher.BuildCommands().Select(x => x.ToTemplatedControl()).ToList();

    [Binding(nameof(HidePluginSideBar), "Content")]
    public string HidePluginSideBarLabel => PluginSideBar.IsVisible ? "Hide" : "Show";

    [Binding(nameof(DownloadLocationButton), "Content")]
    public string DlText => $"Current download location: {_app.GameDir}";

    //[Binding(nameof(GameCountLabel), "Content")]
    public string GameCountText => (_app.Games != null) ? $"Found {_app.Games.Count} games, {_app.InstalledGames.Count} installed" : "";

    private GameViewSmall _currentSelection;
    private Loader.App _app = Loader.App.GetInstance();
    
    public MainView()
    {
        InitializeComponent();
        SetControls();
        PluginSideBar.IsVisible = _app.SidebarState;
        UpdateView();
        InstalledListBox.SelectionChanged += (_, _) => MonitorListBox(InstalledListBox);
        NotInstalledListBox.SelectionChanged += (_, _) => MonitorListBox(NotInstalledListBox);
        SearchBox.KeyUp += (_, _) => ApplySearch();
        OnUpdateView += GenerateNewMenuItems;
        OnUpdateView += UpdateDriveStats;
    }
    
    public void ApplySearch()
    {
        if (string.IsNullOrWhiteSpace(SearchBox.Text))
            _app.GameViews.ForEach(x => x.SetVisibility(true));
        else
        {
            _app.GameViews.ForEach(x =>
                x.SetVisibility(x.GameName.Contains(SearchBox.Text, StringComparison.OrdinalIgnoreCase) ||
                                x.Game.Source.ShortServiceName.Contains(SearchBox.Text,
                                    StringComparison.OrdinalIgnoreCase)));
            
            if (SearchBox.Text.ToLower() == "tic-tac-toe")
                new TicTacToe(_app).Show();
        }
    }

    public void SetNewSelection(GameViewSmall gameViewSmall)
    {
        if (_currentSelection != null)
        {
            if (Equals(_currentSelection, gameViewSmall))
                return;
                
            _currentSelection.Deselected();
        }

        _currentSelection = gameViewSmall;
        _currentSelection.Selected();

        if (gameViewSmall.Game.InstalledStatus == InstalledStatus.Installed)
        {
            InstalledListBox.SelectedItem = gameViewSmall;
            NotInstalledListBox.SelectedItem = null;
        }
        else
        {
            InstalledListBox.SelectedItem = null;
            NotInstalledListBox.SelectedItem = gameViewSmall;
        }
        
        gameViewSmall.UpdateCoverImage();
        SetBgImage(_currentSelection);
    }
    

    private void MonitorListBox(ListBox box)
    {
        GameViewSmall? gameViewSmall = box.SelectedItem as GameViewSmall;
        if (gameViewSmall == null)
            return;
            
        SetNewSelection(gameViewSmall);
    }

    private async void SetBgImage(GameViewSmall game)
    {
        Background.Source = null;

        byte[]? image = (game.Game.HasImage(ImageType.Background)) ? await game.Game.GetImage(ImageType.Background) : null;
        if (image != null && _currentSelection == game)
        {
            MemoryStream stream = new(image);
            Background.Source = new Bitmap(stream);
        }
    }

    private List<TemplatedControl> GenerateMenuItems()
    {
        Loader.App app = Loader.App.GetInstance();

        List<TemplatedControl> controls = app.GameSources.Select(x =>
        {
            List<Command> pluginCommands = app.Middleware.GetGlobalCommands(x);

            if (pluginCommands.Count > 0)
                return new Command($"{x.ServiceName} - {x.Version}", pluginCommands);
            else
                return new Command($"{x.ServiceName} - {x.Version}");
        }).Select(x => x.ToTemplatedControl()).ToList();

        controls.Add(new Command($"Alfae {Loader.App.Version}", new List<Command>()
        {
            new("Open configuration folder", () => LauncherGamePlugin.Utils.OpenFolder(app.ConfigDir)),
            new("Open games folder", () => LauncherGamePlugin.Utils.OpenFolder(app.GameDir))
        }).ToTemplatedControl());

        return controls;
    }

    private void GenerateNewMenuItems()
    {
        PluginSideBar.Children.Clear();
        var controls = _app.GameSources.Select(x =>
        {
            List<Command> pluginCommands = _app.Middleware.GetGlobalCommands(x);
            return new BoxCommandView($"{x.ServiceName} - {x.Version}", pluginCommands);
        }).ToList();

        controls.Add(new($"Alfae {Loader.App.Version}", new List<Command>()
        {
            new(GameCountText),
            new("Open configuration folder", () => LauncherGamePlugin.Utils.OpenFolder(_app.ConfigDir)),
            new("Open games folder", () => LauncherGamePlugin.Utils.OpenFolder(_app.GameDir)),
            new("Boot Profiles", _app.Launcher.BuildCommands())
        }));
        
        PluginSideBar.Children.AddRange(controls);
    }

    [Command(nameof(DownloadLocationButton))]
    public async void OnDownloadLocationButton()
    {
        OpenFolderDialog dialog = new();
        string? result = await dialog.ShowAsync(Loader.App.GetInstance().MainWindow);
        if (!string.IsNullOrWhiteSpace(result))
        {
            _app.GameDir = result;
            UpdateView();
        }
    }

    [Command(nameof(HidePluginSideBar))]
    public async void OnHidePluginSideBar()
    {
        PluginSideBar.IsVisible = !PluginSideBar.IsVisible;
        _app.SidebarState = PluginSideBar.IsVisible;
        UpdateView();
    }

    private void UpdateDriveStats()
    {
        StorageSpaceStackPanel.Children.Clear();
        try
        {
            var driveInfo = new DriveInfo(_app.GameDir);
            StorageSpaceStackPanel.Children.Add(new Label()
            {
                FontSize = 16,
                Content = $"{driveInfo.Name}: {driveInfo.AvailableFreeSpace.ReadableSize()} free"
            });

            var percentage = ((driveInfo.TotalSize - driveInfo.AvailableFreeSpace) / (double)driveInfo.TotalSize) * 100;
            
            StorageSpaceStackPanel.Children.Add(new ProgressBar()
            {
                Value = percentage,
            });
        }
        catch (Exception e)
        {
            _app.Logger.Log($"Unable to fetch drive statistics: {e.Message}", LogType.Warn);
        }
    }
}