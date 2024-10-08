﻿using LauncherGamePlugin;
using LauncherGamePlugin.Commands;
using LauncherGamePlugin.Enums;
using LauncherGamePlugin.Forms;
using LauncherGamePlugin.Interfaces;
using LauncherGamePlugin.Launcher;
using LegendaryIntegration.Extensions;
using LegendaryIntegration.Gui;
using LegendaryIntegration.Model;
using LegendaryIntegration.Service;
using Newtonsoft.Json;

namespace LegendaryIntegration;

// TODO: Add save sync
public class LegendaryGameSource : IGameSource
{
    public string ServiceName => "Epic Games Integration";
    public string Version => "v1.2.5";
    public string SlugServiceName => "epic-games";
    public string ShortServiceName => "EpicGames";
    public PluginType Type => PluginType.GameSource;
    public LegendaryAuth? auth;
    public LegendaryGameManager? manager;
    public static LegendaryGameSource Source { get; private set; }
    public IApp App { get; private set; }

    public async Task<InitResult?> Initialize(IApp app)
    {
        App = app;
        Source = this;

        auth = new();

        if (!await auth.AttemptLogin())
            auth = null;

        return null;
    }

    public async Task<List<IBootProfile>> GetBootProfiles() => new();

    public async Task<List<IGame>> GetGames()
    {
        if (auth == null)
            return new();

        manager ??= new(auth, App);

        return (await manager.GetGames()).Select(x => (IGame) x).ToList();
    }

    public List<Command> GetGameCommands(IGame game)
    {
        LegendaryGame legendaryGame = game as LegendaryGame;
        if (legendaryGame == null)
            throw new InvalidDataException();

        List<Command> commands = new();

        if (!legendaryGame.IsInstalled)
        {
            if (legendaryGame.FromOrigin)
                commands.Add(new("Play on Origin", () => Launch(legendaryGame, false)));
            else
                commands.Add(new("Install", () => Download(legendaryGame)));
            
            
            commands.Add(new("Show in browser", legendaryGame.ShowInBrowser));
            commands.Add(new("Import", () => new ImportFileSelect(legendaryGame).Show(App)));
            
            if (legendaryGame.Size == 0 && !legendaryGame.FromOrigin)
                commands.Add(new("Get game install size", () => GetOfflineGameSize(legendaryGame)));
        }
        else
        {
            if (legendaryGame.UpdateAvailable && !auth!.OfflineLogin)
            {
                commands.Add(new("Update", () => Download(legendaryGame)));
            }
            
            commands.Add(new(game.IsRunning ? "Running" : "Launch", () => Launch(legendaryGame, false)));
            commands.Add(new("Config/Info", () => App.ShowForm(legendaryGame.ToForm()!)));
            commands.Add(new("View in browser", legendaryGame.ShowInBrowser));
            commands.Add(new("Verify", () => Repair(legendaryGame)));
            commands.Add(new("Move", () => new MoveFolderSelect(legendaryGame).Show(App)));
            commands.Add(new("Uninstall", () =>
            {
                App.Show2ButtonTextPrompt($"Are you sure you want to uninstall {legendaryGame.Name}?", "Uninstall", "Back",
                    x =>
                    {
                        LegendaryGame xGame = x.Game as LegendaryGame;
                        Uninstall(xGame);
                    }, x => App.HideForm(), legendaryGame);
            }));
        }

        if (legendaryGame.Download != null)
        {
            commands = new();
            if (legendaryGame.Download.Type != LegendaryStatusType.Move)
            {
                if (legendaryGame.Download.Active)
                    commands.Add(new("Pause", legendaryGame.Download.Pause));
                else
                    commands.Add(new ("Continue", legendaryGame.Download.Start));
            
                commands.Add(new("Stop", legendaryGame.Download.Stop));
            }
        }
        
        return commands;
    }

    private async void GetOfflineGameSize(LegendaryGame game)
    {
        await game.GetInfo();
        game.InvokeOnUpdate();
    }

    public List<Command> GetGlobalCommands()
    {
        List<Command> commands = new List<Command>()
        {
            new("Open Wiki", () => Utils.OpenUrl("https://github.com/suchmememanyskill/Alfae/wiki/Epic-Games-Integration")),
            new(),
        };
        
        if (auth == null)
            commands.Add(new("Not logged in"));
        else
        {
            commands.Add(new($"Logged in as {auth.StatusResponse.AccountName}"));
            if (auth.OfflineLogin)
                commands.Add(new("Started in offline mode"));
            commands.Add(new($"Loaded {manager?.LastGameCount ?? 0} games"));
        }
        
        commands.Add(new());
        
        if (auth == null)
            commands.Add(new("Login", () => LoginForm()));
        else
            commands.Add(new("Logout", () => Logout()));
        
        commands.Add(new());
        
        commands.Add(new("Open free games page", () => Utils.OpenUrl("https://www.epicgames.com/store/en-US/free-games")));
        commands.Add(new("Reload games", ReloadGames));
        
        commands.Add(new());
        
        commands.Add(new("Open legendary config dir", () => Utils.OpenFolder(Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "legendary"))));
        if (File.Exists(Path.Join(App.ConfigDir, "legendary.json")))
            commands.Add(new("Open legendary integration config", () => Utils.OpenFolder(Path.Join(App.ConfigDir, "legendary.json"))));
        commands.Add(new("Open legendary config", () => Utils.OpenFolder(Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "legendary", "config.ini"))));

        if (auth != null)
        {
            commands.Add(new());
            commands.Add(new("EOS Overlay", () => new LegendaryEOSOverlay(App).OpenGUI()));
        }

        return commands;
    }

    public async void ReloadGames()
    {
        App.ShowTextPrompt("Reloading epic games...");
        Terminal t = new(App);
        await t.ExecLegendary("list-games");
        App.ReloadGames();
        App.HideForm();
    }

    public async void Launch(LegendaryGame game, bool ignoreUpdate = false)
    {
        try
        {
            LaunchParams? launch = await game.Launch(ignoreUpdate);

            if (launch == null)
                throw new Exception("Legendary exited unexpectedly");
            
            App.Launch(launch);
        }
        catch (Exception e)
        {
            List<ButtonEntry> buttons = new()
            {
                new("Back", _ => App.HideForm())
            };

            if (e.Message == "Game has an update available")
                buttons.Add(new("Launch anyway", x =>
                {
                    Launch(game, true);
                    App.HideForm();
                }));
            
            App.ShowForm(new(new()
            {
                Form.TextBox($"Game failed to launch: {e.Message}", FormAlignment.Center),
                Form.ButtonList(buttons)
            }));
            
            Log($"Something went wrong while launching {game.Name}: {e.Message}");
        }
    }

    public async void Uninstall(LegendaryGame game)
    {
        App.ShowTextPrompt($"Uninstalling {game.Name}...");
        await game.Uninstall();
        App.ReloadGames();
        App.HideForm();
    }

    public async Task<string> UrlGet(string url)
    {
        using (HttpClient client = new())
        {
            return await client.GetStringAsync(url);
        }
    }

    private Dictionary<string, string> _sdl_games = new()
    {
        { "Fortnite", "https://legendary.gl/v1/sdl/Fortnite.json" },
        { "Ginger", "https://legendary.gl/v1/sdl/Ginger.json" }
    };

    public async void Download(LegendaryGame game)
    {
        if (_sdl_games.ContainsKey(game.InternalName) && !game.IsInstalled)
        {
            string data = await UrlGet(_sdl_games[game.InternalName]);
            Dictionary<string, LegendaryTags> tags =
                JsonConvert.DeserializeObject<Dictionary<string, LegendaryTags>>(data)!;

            List<FormEntry> entries = new()
            {
                Form.TextBox($"{game.Name} Optional Content", FormAlignment.Center, "Bold")
            };

            foreach (var x in tags)
            {
                entries.Add(Form.Toggle(x.Value.Name, x.Key == "__required", enabled: x.Key != "__required"));
            }
            
            entries.Add(Form.Button("Back", _ => App.HideForm(), "Install", x =>
            {
                App.HideForm();
                DownloadSDL(x, tags, game);
            }));
            App.ShowForm(new(entries));
            return;
        }
        
        await game.StartDownload();
        game.Download!.OnCompletionOrCancel += _ => App.ReloadGames();
    }

    public async void DownloadSDL(Form form, Dictionary<string, LegendaryTags> tags, LegendaryGame game)
    {
        List<string> installTags = new();

        foreach (var x in tags)
        {
            string value = form.GetValue(x.Value.Name)!;
            if (value == "1")
            {
                installTags.AddRange(x.Value.Tags);
            }
        }
        
        await game.StartDownload(tags: installTags);
        game.Download!.OnCompletionOrCancel += _ => App.ReloadGames();
    }

    public async void Repair(LegendaryGame game)
    {
        await game.Repair();
    }

    public async void Login(string? authCode = null)
    {
        App.ShowTextPrompt("Logging in...");

        auth = new();

        try
        {
            if (authCode != null)
                await auth.Authenticate(authCode);
            else
                await auth.AuthenticateUsingWebview();
        }
        catch (Exception e)
        {
            auth = null;
            LoginForm(e.Message);
            return;
        }

        if (!await auth.AttemptLogin())
        {
            LoginForm("Login failed");
            auth = null;
            return;
        }
        
        App.ReloadGames();
        App.HideForm();
    }

    private void LoginForm(string warningMessage = "") => new LoginForm(this).Show(warningMessage);

    public async Task Logout()
    {
        if (auth == null)
            return;
        
        App.ShowTextPrompt("Logging out...");

        if (manager != null)
        {
            manager.StopAllDownloads();
        }
        
        await auth.Logout();
        auth = null;
        manager = null;
        App.ReloadGames();
        App.HideForm();
    }
    
    public void Log(string message, LogType type = LogType.Info) => App.Logger.Log(message, type, "EpicGames");
}