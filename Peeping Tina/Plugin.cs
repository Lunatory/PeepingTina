using Dalamud.Game.Command;
using Dalamud.Plugin;
using System.Collections.Generic;
using System.Globalization;
using Lumina.Excel.Sheets;
using PeepingTina.Resources;

namespace PeepingTina {
    // ReSharper disable once ClassNeverInstantiated.Global
    public class Plugin : IDalamudPlugin {
        internal static string Name => "Peeping Tina";

        internal Configuration Config { get; }
        internal PluginUi Ui { get; }
        internal TargetWatcher Watcher { get; }
        internal IpcManager IpcManager { get; }

        // Disable PvP Check
        // internal bool InPvp { get; private set; }

        public Plugin(IDalamudPluginInterface pluginInterface) {
            pluginInterface.Create<Service>();
            
            Config = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            Config.Initialize(Service.Interface);
            Watcher = new TargetWatcher(this);
            Ui = new PluginUi(this);
            IpcManager = new IpcManager(this);

            OnLanguageChange(Service.Interface.UiLanguage);
            Service.Interface.LanguageChanged += OnLanguageChange;

            Service.CommandManager.AddHandler("/ppeepingtina", new CommandInfo(OnCommand) {
                HelpMessage = "Use with no arguments to show the list. Use with \"c\" or \"config\" to show the config",
            });
            Service.CommandManager.AddHandler("/ptina", new CommandInfo(OnCommand) {
                HelpMessage = "Alias for /ppeepingtina",
            });
            Service.CommandManager.AddHandler("/ppeep", new CommandInfo(OnCommand) {
                HelpMessage = "Alias for /ppeepingtina",
            });

            Service.ClientState.Login += OnLogin;
            Service.ClientState.Logout += OnLogout;
            Service.ClientState.TerritoryChanged += OnTerritoryChange;
            Service.Interface.UiBuilder.Draw += Ui.Draw;
            Service.Interface.UiBuilder.OpenConfigUi += Ui.SettingsWindow.Toggle;
            Service.Interface.UiBuilder.OpenMainUi += Ui.MainWindow.Toggle;
        }

        public void Dispose() {
            Service.Interface.UiBuilder.OpenConfigUi -= Ui.SettingsWindow.Toggle;
            Service.Interface.UiBuilder.OpenMainUi -= Ui.MainWindow.Toggle;
            Service.Interface.UiBuilder.Draw -= Ui.Draw;
            Service.ClientState.TerritoryChanged -= OnTerritoryChange;
            Service.ClientState.Logout -= OnLogout;
            Service.ClientState.Login -= OnLogin;
            Service.CommandManager.RemoveHandler("/ppeep");
            Service.CommandManager.RemoveHandler("/ptina");
            Service.CommandManager.RemoveHandler("/ppeepingtina");
            Service.Interface.LanguageChanged -= OnLanguageChange;
            IpcManager.Dispose();
            Ui.Dispose();
            Watcher.Dispose();
        }

        private static void OnLanguageChange(string langCode) {
            Language.Culture = new CultureInfo(langCode);
        }

        private void OnTerritoryChange(ushort e) {
            try {
                var territory = Service.DataManager.GetExcelSheet<TerritoryType>().GetRow(e);
                // Disable PvP Check
                // InPvp = territory.IsPvpZone;
            } catch (KeyNotFoundException) {
                Service.Log.Warning("Could not get territory for current zone");
            }
        }

        private void OnCommand(string command, string args) {
            if (args is "config" or "c") {
                Ui.SettingsWindow.IsOpen = true;
            } else {
                Ui.MainWindow.IsOpen = true;
            }
        }

        private void OnLogin() {
            if (!Config.OpenOnLogin) return;
            Ui.MainWindow.IsOpen = true;
        }

        private void OnLogout(int type, int code) {
            Ui.MainWindow.IsOpen = false;
            Watcher.ClearPrevious();
        }
    }
}
