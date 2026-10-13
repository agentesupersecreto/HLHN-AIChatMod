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


        /// <summary>Per-character memory folder, inside <see cref="ConfigDir"/>.</summary>
        public const string MemoryDirName = "memory";

        /// <summary>Chat history folder, inside <see cref="ConfigDir"/>.</summary>
        public const string HistoryDirName = "history";

        /// <summary>
        /// Folder holding every user file of the mod (settings, prompts, memory, history):
        /// BepInEx/config/AIChat. Created on demand so the user always has the files to edit.
        /// </summary>
        public static string ConfigDir { get; private set; } = "";

        /// <summary>Full path of a file that lives inside <see cref="ConfigDir"/>.</summary>
        public static string GetConfigFilePath(string fileName)
        {
            return Path.Combine(ConfigDir, fileName);
        }

        /// <summary>
        /// Full path of a subfolder of <see cref="ConfigDir"/>, created on demand. Per-character memory
        /// and chat history live here (not in the Unity save folder) so everything the mod writes sits
        /// together with the settings and can be backed up in one go. Returns "" when the config folder
        /// could not be resolved (the caller then simply does not persist anything).
        /// </summary>
        public static string GetDataDir(string dirName)
        {
            EnsureConfigDir();
            if (string.IsNullOrEmpty(ConfigDir)) return "";

            string path = Path.Combine(ConfigDir, dirName);
            try
            {
                if (!Directory.Exists(path)) Directory.CreateDirectory(path);
            }
            catch (Exception ex)
            {
                LogWarning($"Could not create folder {path}: {ex.Message}");
            }
            return path;
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

        /// <summary>
        /// Moves the per-character files written by earlier versions from the Unity save folder
        /// (&lt;persistentDataPath&gt;/AIchat/&lt;folder&gt;) into BepInEx/config/AIChat/&lt;folder&gt;.
        /// A file that already exists in the new location always wins, so nothing is overwritten.
        /// </summary>
        private static void MigrateDataFolder(string legacyFolderName, string targetFolderName)
        {
            string legacyDir = Path.Combine(UnityEngine.Application.persistentDataPath, "AIchat", legacyFolderName);
            if (!Directory.Exists(legacyDir)) return;

            string targetDir = GetDataDir(targetFolderName);
            if (string.IsNullOrEmpty(targetDir)) return;

            try
            {
                int moved = 0;
                foreach (string file in Directory.GetFiles(legacyDir))
                {
                    string target = Path.Combine(targetDir, Path.GetFileName(file));
                    if (File.Exists(target)) continue;

                    try
                    {
                        File.Move(file, target);
                        moved++;
                    }
                    catch (Exception ex)
                    {
                        LogWarning($"Could not move {file}: {ex.Message}");
                    }
                }

                if (moved > 0) LogInfo($"Migrated {moved} file(s): {legacyDir} -> {targetDir}");

                // Drop the old folder when it is empty; keep it otherwise (something may be in use).
                try
                {
                    if (Directory.GetFiles(legacyDir).Length == 0 && Directory.GetDirectories(legacyDir).Length == 0)
                        Directory.Delete(legacyDir, false);
                }
                catch (Exception) { /* leaving an empty folder behind is harmless */ }
            }
            catch (Exception ex)
            {
                LogWarning($"Could not migrate {legacyDir}: {ex.Message}");
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

                // Create the per-character data folders up front so the user sees them right away;
                // the files themselves are written on demand.
                GetDataDir(MemoryDirName);
                GetDataDir(HistoryDirName);

                // Import files written by earlier versions:
                //   BepInEx/config/HLHN_AIChatMod.ini
                //   BepInEx/AIchat/custom_prompt.txt
                //   LocalLow/<Game>/AIchat/system_prompt.txt
                MigrateFile(Path.Combine(bepInExConfig, SettingsFileName), SettingsFileName);
                MigrateFile(Path.Combine(Path.GetDirectoryName(ConfigPath ?? ".") ?? ".", "AIchat", CustomPromptFileName), CustomPromptFileName);
                MigrateFile(Path.Combine(UnityEngine.Application.persistentDataPath, "AIchat", SystemPromptFileName), SystemPromptFileName);

                // Memory and history used to live under Application.persistentDataPath/AIchat; bring them
                // next to the settings so the whole mod folder can be moved or backed up in one piece.
                MigrateDataFolder("Memory", MemoryDirName);
                MigrateDataFolder("History", HistoryDirName);

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
