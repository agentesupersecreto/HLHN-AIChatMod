using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace DialogInterceptorMod.API
{
    /// <summary>
    /// Keeps the audio of the game's own dialogue lines on disk, so a line that is said twice is only
    /// paid for once.
    ///
    /// The game reuses a small, fixed set of lines: the Talk About / Compliment / Critic comments of a
    /// character, her greetings, her kiss replies and her Sex-Talk phrases. Without a cache every one
    /// of them is a fresh request to the endpoint - a second or two of silence before the voice starts,
    /// and a second charge for a sentence the player already heard. With it, the second time takes a
    /// file read.
    /// </summary>
    /// <remarks>
    /// The key covers everything that changes the audio (the line, the voice, the endpoint, the model
    /// and the tuning parameters), so changing a voice or a setting simply misses and the new audio is
    /// stored next to the old one. Entries are evicted least-recently-used once the folder passes
    /// <see cref="DefaultMaxBytes"/>, and only the mod's own <c>.mp3</c> files are ever deleted.
    ///
    /// Only the game's dialogue is cached: an AI reply is a new sentence nearly every time, so caching
    /// it would fill the folder with one-use files and evict the lines that are worth keeping.
    ///
    /// Pure BCL (no Unity types), so the lookup, the eviction and the accounting can be exercised
    /// outside the game.
    /// </remarks>
    public class TtsAudioCache
    {
        /// <summary>Folder size the cache is allowed to reach before the oldest entries go.</summary>
        public const long DefaultMaxBytes = 200L * 1024 * 1024;

        /// <summary>Extension of a cached entry; also what eviction looks at.</summary>
        public const string EntryExtension = ".mp3";

        /// <summary>Separator of the key material's parts; never appears in a line of dialogue.</summary>
        private const char FieldSeparator = '\u001f';

        private readonly string _dir;
        private readonly long _maxBytes;
        private readonly object _gate = new object();

        /// <param name="dir">
        /// Folder to keep the entries in. An empty string disables the cache entirely (the caller then
        /// gets a miss and a no-op store), which is what happens when the config folder could not be
        /// resolved.
        /// </param>
        /// <param name="maxBytes">Folder size to stay under; the shipped default applies when not positive.</param>
        public TtsAudioCache(string dir, long maxBytes = DefaultMaxBytes)
        {
            _dir = dir ?? "";
            _maxBytes = maxBytes > 0 ? maxBytes : DefaultMaxBytes;
        }

        /// <summary>False when there is no folder to work with, in which case nothing is cached.</summary>
        public bool Enabled => !string.IsNullOrEmpty(_dir);

        /// <summary>Folder the entries live in.</summary>
        public string Directory => _dir;

        /// <summary>Size the folder is kept under.</summary>
        public long MaxBytes => _maxBytes;

        /// <summary>What the cache currently holds, for the settings panel.</summary>
        public struct CacheStats
        {
            public long Bytes;
            public int Count;
        }

        /// <summary>
        /// Identifies the audio of one line: same key means the same voice saying the same text with the
        /// same settings, so the stored file can be replayed as is.
        /// </summary>
        public static string Key(string text, string voice, string apiUrl, string model, string latency,
                                 float speed, bool normalizeLoudness, bool normalize)
        {
            // The version tag lets a future change of the request shape invalidate old entries.
            string material = string.Join(FieldSeparator.ToString(), new[]
            {
                "v1",
                text ?? "",
                voice ?? "",
                apiUrl ?? "",
                model ?? "",
                latency ?? "",
                speed.ToString("0.###", CultureInfo.InvariantCulture),
                normalizeLoudness ? "1" : "0",
                normalize ? "1" : "0",
            });

            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(material));
                var sb = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        /// <summary>Full path of the entry for a key ("" when the cache is disabled).</summary>
        public string PathFor(string key)
        {
            if (!Enabled || string.IsNullOrEmpty(key)) return "";
            return Path.Combine(_dir, key + EntryExtension);
        }

        /// <summary>
        /// Reads a stored line. On a hit the entry counts as used, so it is the last one to be evicted.
        /// The bytes are returned as a copy: eviction can delete the file while they are being decoded.
        /// </summary>
        public bool TryRead(string key, out byte[] bytes)
        {
            bytes = null;

            string path = PathFor(key);
            if (path.Length == 0) return false;

            lock (_gate)
            {
                try
                {
                    if (!File.Exists(path)) return false;

                    byte[] data = File.ReadAllBytes(path);
                    if (data == null || data.Length == 0)
                    {
                        // An empty entry is a half-written file: drop it and treat it as a miss.
                        Delete(path);
                        return false;
                    }

                    Touch(path);
                    bytes = data;
                    return true;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Stores the audio of a line and trims the folder back under <see cref="MaxBytes"/>. Never
        /// throws: a cache problem may cost a request, it may not break a line.
        /// </summary>
        public bool Store(string key, byte[] bytes)
        {
            if (!Enabled || string.IsNullOrEmpty(key) || bytes == null || bytes.Length == 0) return false;

            string path = PathFor(key);

            lock (_gate)
            {
                try
                {
                    if (!File.Exists(path)) File.WriteAllBytes(path, bytes);
                    Touch(path);
                    Trim();
                    return true;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        /// <summary>Empties the cache. Files other than the mod's own entries are left alone.</summary>
        public void Clear()
        {
            lock (_gate)
            {
                foreach (var entry in ListEntries())
                    Delete(entry.Path);
            }
        }

        /// <summary>Size and entry count of the cache, for the settings panel.</summary>
        public CacheStats Stats()
        {
            var stats = new CacheStats();

            lock (_gate)
            {
                foreach (var entry in ListEntries())
                {
                    stats.Bytes += entry.Size;
                    stats.Count++;
                }
            }

            return stats;
        }

        /// <summary>
        /// Deletes the least recently used entries until the folder fits. Entries are ordered by their
        /// last write time, which every read and write refreshes - that is what makes it LRU rather
        /// than FIFO.
        /// </summary>
        public void Trim()
        {
            lock (_gate)
            {
                var entries = new List<Entry>(ListEntries());
                if (entries.Count == 0) return;

                long total = 0;
                foreach (var entry in entries) total += entry.Size;
                if (total <= _maxBytes) return;

                entries.Sort((a, b) => a.Used.CompareTo(b.Used));

                foreach (var entry in entries)
                {
                    if (total <= _maxBytes) break;

                    if (Delete(entry.Path)) total -= entry.Size;
                }
            }
        }

        private struct Entry
        {
            public string Path;
            public long Size;
            public DateTime Used;
        }

        /// <summary>The mod's own entries in the folder (a stray file is never touched).</summary>
        private List<Entry> ListEntries()
        {
            var entries = new List<Entry>();
            if (!Enabled) return entries;

            try
            {
                if (!System.IO.Directory.Exists(_dir)) return entries;

                foreach (string path in System.IO.Directory.GetFiles(_dir, "*" + EntryExtension))
                {
                    try
                    {
                        var info = new FileInfo(path);
                        entries.Add(new Entry { Path = path, Size = info.Length, Used = info.LastWriteTimeUtc });
                    }
                    catch (Exception)
                    {
                        // Vanished between listing and reading: nothing to account for.
                    }
                }
            }
            catch (Exception)
            {
                // A folder that cannot be listed behaves like an empty cache.
            }

            return entries;
        }

        private static void Touch(string path)
        {
            try { File.SetLastWriteTimeUtc(path, DateTime.UtcNow); } catch (Exception) { }
        }

        private static bool Delete(string path)
        {
            try
            {
                File.Delete(path);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
