using System;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace DialogInterceptorMod.Core
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        internal static string ConfigPath;

        /// <summary>File name of the settings file, inside <see cref="ConfigDir"/>.</summary>
        public const string SettingsFileName = "HLHN_AIChatMod.ini";

        /// <summary>File name of the editable system prompt template, inside <see cref="ConfigDir"/>.</summary>
        public const string SystemPromptFileName = "system_prompt.txt";

        /// <summary>File name of the editable extra rules file, inside <see cref="ConfigDir"/>.</summary>
        public const string CustomPromptFileName = "custom_prompt.txt";

        /// <summary>
        /// Folder holding every user file of the mod (settings, prompts): BepInEx/config/AIChat.
        /// Created on demand so the user always has the files to edit.
        /// </summary>
        public static string ConfigDir { get; private set; } = "";

        /// <summary>Full path of a file that lives inside <see cref="ConfigDir"/>.</summary>
        public static string GetConfigFilePath(string fileName)
        {
            return Path.Combine(ConfigDir, fileName);
        }

        /// <summary>Creates <see cref="ConfigDir"/> when it does not exist yet.</summary>
        public static void EnsureConfigDir()
        {
            if (string.IsNullOrEmpty(ConfigDir)) return;
            if (Directory.Exists(ConfigDir)) return;

            Directory.CreateDirectory(ConfigDir);
            LogInfo($"Created config folder: {ConfigDir}");
        }

        /// <summary>
        /// Resolves BepInEx/config starting from the plugin folder and walking up until the
        /// BepInEx folder is found, so it works whether the dll sits directly in plugins/
        /// or in a subfolder of it.
        /// </summary>
        private static string ResolveBepInExConfigPath(string fromDir)
        {
            try
            {
                var dir = new DirectoryInfo(string.IsNullOrEmpty(fromDir) ? "." : fromDir);
                while (dir != null)
                {
                    if (string.Equals(dir.Name, "BepInEx", StringComparison.OrdinalIgnoreCase))
                        return Path.Combine(dir.FullName, "config");
                    dir = dir.Parent;
                }
            }
            catch (Exception ex)
            {
                LogWarning($"Could not resolve the BepInEx folder ({ex.Message}); using the plugin-relative guess.");
            }

            return Path.GetFullPath(Path.Combine(string.IsNullOrEmpty(fromDir) ? "." : fromDir, "..", "config"));
        }

        /// <summary>
        /// Moves a file from an older location into <see cref="ConfigDir"/> so existing installs
        /// keep their settings and hand-edited prompts. Does nothing when the target already
        /// exists or there is nothing to migrate.
        /// </summary>
        private static void MigrateFile(string legacyPath, string fileName)
        {
            if (string.IsNullOrEmpty(legacyPath) || !File.Exists(legacyPath)) return;

            string target = GetConfigFilePath(fileName);
            if (File.Exists(target)) return;

            try
            {
                File.Move(legacyPath, target);
                LogInfo($"Migrated {legacyPath} -> {target}");
            }
            catch (Exception moveEx)
            {
                try
                {
                    File.Copy(legacyPath, target, false);
                    LogInfo($"Copied (move failed) {legacyPath} -> {target}");
                }
                catch (Exception copyEx)
                {
                    LogWarning($"Could not migrate {legacyPath}: {moveEx.Message} / {copyEx.Message}");
                }
            }
        }

        public void Awake()
        {
            try
            {
                Log = base.Logger;
                ConfigPath = Path.GetDirectoryName(base.Info.Location);
                LogInfo($"Plugin {PluginInfo.PLUGIN_NAME} v{PluginInfo.PLUGIN_VERSION} initializing...");

                // All user files live in BepInEx/config/AIChat.
                string bepInExConfig = ResolveBepInExConfigPath(ConfigPath);
                ConfigDir = Path.Combine(bepInExConfig, "AIChat");
                EnsureConfigDir();
                LogInfo($"Config folder: {ConfigDir}");

                // Import files written by earlier versions:
                //   BepInEx/config/HLHN_AIChatMod.ini
                //   BepInEx/AIchat/custom_prompt.txt
                //   LocalLow/<Game>/AIchat/system_prompt.txt
                MigrateFile(Path.Combine(bepInExConfig, SettingsFileName), SettingsFileName);
                MigrateFile(Path.Combine(Path.GetDirectoryName(ConfigPath ?? ".") ?? ".", "AIchat", CustomPromptFileName), CustomPromptFileName);
                MigrateFile(Path.Combine(UnityEngine.Application.persistentDataPath, "AIchat", SystemPromptFileName), SystemPromptFileName);

                var harmony = new Harmony(PluginInfo.PLUGIN_GUID);
                harmony.PatchAll();
                LogInfo("Harmony patches applied.");

                DialogBehaviour.Initialize();

                LogInfo("Plugin initialized successfully.");
            }
            catch (Exception ex)
            {
                LogError($"Error in Awake: {ex.Message}\n{ex.StackTrace}");
            }
        }

        public static void LogDebug(string msg) { Log?.LogDebug($"[D] {msg}"); }
        public static void LogInfo(string msg) { Log?.LogInfo($"[I] {msg}"); }
        public static void LogWarning(string msg) { Log?.LogWarning($"[W] {msg}"); }
        public static void LogError(string msg) { Log?.LogError($"[E] {msg}"); }
    }
}
