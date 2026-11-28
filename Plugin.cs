using System;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using JetBrains.Annotations;
using LocalizationManager;
using PieceManager;
using ServerSync;
using UnityEngine;

namespace PieceManagerModTemplate
{
    [BepInPlugin(ModGUID, ModName, ModVersion)]
    public class PieceManagerModTemplatePlugin : BaseUnityPlugin
    {
        internal const string ModName = "StoneBrickWalls";
        internal const string ModVersion = "1.2.1";
        internal const string Author = "Hexium";
        private const string ModGUID = Author + "." + ModName;
        private static string ConfigFileName = ModGUID + ".cfg";
        private static string ConfigFileFullPath = Paths.ConfigPath + Path.DirectorySeparatorChar + ConfigFileName;
        internal static string ConnectionError = "";
        private readonly Harmony _harmony = new(ModGUID);
        private static readonly ConfigSync ConfigSync = new(ModGUID) { DisplayName = ModName, CurrentVersion = ModVersion, MinimumRequiredVersion = ModVersion };
        private FileSystemWatcher _watcher;
        private readonly object _reloadLock = new();
        private DateTime _lastConfigReloadTime;
        private const long RELOAD_DELAY = 10000000; // One second
        
        
        public enum Toggle
        {
            On = 1,
            Off = 0
        }

        public void Awake()
        {
            bool saveOnSet = Config.SaveOnConfigSet;
            Config.SaveOnConfigSet = false;
            
            // Uncomment the line below to use the LocalizationManager for localizing your mod.
            //Localizer.Load(); // Use this to initialize the LocalizationManager (for more information on LocalizationManager, see the LocalizationManager documentation https://github.com/blaxxun-boop/LocalizationManager#example-project).

            try
            {
                _serverConfigLocked = config("1 - General", "Lock Configuration", Toggle.On, "If on, the configuration is locked and can be changed by server admins only.");
                _ = ConfigSync.AddLockingConfigEntry(_serverConfigLocked);
            }
            catch (Exception e)
            {
                Log.Unknown("setting up the server config lock", e);
            }

            try
            {
                BrickPieces.Register();
            }
            catch (Exception e)
            {
                Log.Unknown("creating the brick pieces", e);
            }

            try
            {
                _harmony.PatchAll(Assembly.GetExecutingAssembly());
            }
            catch (HarmonyException e)
            {
                Log.Error($"Could not patch the game (another mod or a game update may conflict): {e.Message}");
            }
            catch (Exception e)
            {
                Log.Unknown("patching the game", e);
            }

            SetupWatcher();

            try
            {
                Config.Save();
            }
            catch (IOException e)
            {
                Log.Error($"Could not save the config file {ConfigFileFullPath}: {e.Message}");
            }
            catch (Exception e)
            {
                Log.Unknown("saving the config file", e);
            }
            finally
            {
                if (saveOnSet)
                {
                    Config.SaveOnConfigSet = saveOnSet;
                }
            }
            
            // If you want to do something once localization completes, LocalizationManager has a hook for that.
            /*Localizer.OnLocalizationComplete += () =>
            {
                // Do something
                ItemManagerModTemplateLogger.LogDebug("OnLocalizationComplete called");
            };*/
        }

        private void OnDestroy()
        {
            try
            {
                SaveWithRespectToConfigSet();
                _watcher?.Dispose();
            }
            catch (IOException e)
            {
                Log.Error($"Could not save the config file on exit: {e.Message}");
            }
            catch (Exception e)
            {
                Log.Unknown("shutting down", e);
            }
        }

        private void SetupWatcher()
        {
            try
            {
                FileSystemWatcher watcher = new(Paths.ConfigPath, ConfigFileName);
                watcher.Changed += ReadConfigValues;
                watcher.Created += ReadConfigValues;
                watcher.Renamed += ReadConfigValues;
                watcher.IncludeSubdirectories = true;
                watcher.SynchronizingObject = ThreadingHelper.SynchronizingObject;
                watcher.EnableRaisingEvents = true;
                _watcher = watcher;
            }
            catch (Exception e) when (e is ArgumentException or IOException or PlatformNotSupportedException)
            {
                Log.Error($"Config changes will not reload live, could not watch {Paths.ConfigPath}: {e.Message}");
            }
            catch (Exception e)
            {
                Log.Unknown("watching the config file", e);
            }
        }

        private void ReadConfigValues(object sender, FileSystemEventArgs e)
        {
            DateTime now = DateTime.Now;
            long time = now.Ticks - _lastConfigReloadTime.Ticks;
            if (time < RELOAD_DELAY)
            {
                return;
            }

            lock (_reloadLock)
            {
                if (!File.Exists(ConfigFileFullPath))
                {
                    Log.Warning("Config file does not exist. Skipping reload.");
                    return;
                }

                try
                {
                    SaveWithRespectToConfigSet(true);
                    Log.Info("Configuration reload complete.");
                }
                catch (IOException ex)
                {
                    Log.Error($"Could not reload the config file: {ex.Message}");
                }
                catch (Exception ex)
                {
                    Log.Unknown("reloading the config file", ex);
                }
            }

            _lastConfigReloadTime = now;
        }
        
        private void SaveWithRespectToConfigSet(bool reload = false)
        {
            bool originalSaveOnSet = Config.SaveOnConfigSet;
            Config.SaveOnConfigSet = false;
            if (reload)
                Config.Reload();
            Config.Save();
            if (originalSaveOnSet)
            {
                Config.SaveOnConfigSet = originalSaveOnSet;
            }
        }
        
        #region ConfigOptions

        private static ConfigEntry<Toggle> _serverConfigLocked = null!;

        private ConfigEntry<T> config<T>(string group, string name, T value, ConfigDescription description, bool synchronizedSetting = true)
        {
            ConfigDescription extendedDescription = new(description.Description + (synchronizedSetting ? " [Synced with Server]" : " [Not Synced with Server]"), description.AcceptableValues, description.Tags);
            ConfigEntry<T> configEntry = Config.Bind(group, name, value, extendedDescription);
            //var configEntry = Config.Bind(group, name, value, description);

            SyncedConfigEntry<T> syncedConfigEntry = ConfigSync.AddConfigEntry(configEntry);
            syncedConfigEntry.SynchronizedConfig = synchronizedSetting;

            return configEntry;
        }

        private ConfigEntry<T> config<T>(string group, string name, T value, string description, bool synchronizedSetting = true)
        {
            return config(group, name, value, new ConfigDescription(description), synchronizedSetting);
        }

        private class ConfigurationManagerAttributes
        {
            [UsedImplicitly] public int? Order = null!;
            [UsedImplicitly] public bool? Browsable = null!;
            [UsedImplicitly] public string? Category = null!;
            [UsedImplicitly] public Action<ConfigEntryBase>? CustomDrawer = null!;
        }

        class AcceptableShortcuts : AcceptableValueBase
        {
            public AcceptableShortcuts() : base(typeof(KeyboardShortcut))
            {
            }

            public override object Clamp(object value) => value;
            public override bool IsValid(object value) => true;

            public override string ToDescriptionString() => "# Acceptable values: " + string.Join(", ", UnityInput.Current.SupportedKeyCodes);
        }

        #endregion
    }
    
    public static class KeyboardExtensions
    {
        public static bool IsKeyDown(this KeyboardShortcut shortcut)
        {
            return shortcut.MainKey != KeyCode.None && Input.GetKeyDown(shortcut.MainKey) && shortcut.Modifiers.All(Input.GetKey);
        }

        public static bool IsKeyHeld(this KeyboardShortcut shortcut)
        {
            return shortcut.MainKey != KeyCode.None && Input.GetKey(shortcut.MainKey) && shortcut.Modifiers.All(Input.GetKey);
        }
    }
}