using System;
using System.Collections.Generic;
using System.IO;
using MoonSharp.Interpreter;

namespace Frontend.Services.Scripting;

/// <summary>
/// Root API object exposed to all Lua scripts as the global "vocal" variable.
/// Mirrors Utan's UtanApiProxy pattern.
/// Only exposes what phonemizer scripts need. Expand sub-proxies as needed.
/// </summary>
[MoonSharpUserData]
public class VocalApiProxy
{
    public VocalIoProxy io { get; }
    public VocalTextProxy text { get; }

    public VocalApiProxy(Script script)
    {
        io = new VocalIoProxy();
        text = new VocalTextProxy(script);
    }

    public void log(string msg) => Console.WriteLine($"[Lua] {msg}");

    // -------------------------------------------------------------------------

    [MoonSharpUserData]
    public class VocalIoProxy
    {
        private static string ResolvePath(string path)
        {
            if (Path.IsPathRooted(path)) return path;
            return Path.GetFullPath(
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path));
        }

        /// <summary>Read a UTF-8 text file. Path is absolute or relative to app base.</summary>
        public string read_text(string path)
        {
            var resolved = ResolvePath(path);
            if (!File.Exists(resolved))
                throw new FileNotFoundException(
                    $"[vocal.io] File not found: '{resolved}' (raw: '{path}')", resolved);
            return File.ReadAllText(resolved, System.Text.Encoding.UTF8);
        }

        /// <summary>
        /// Load a flat key=value (or key[delim]value) text file into a Lua-accessible
        /// dictionary object. Backed by a C# Dictionary for O(1) lookups.
        /// This is the primary mechanism for phonemizer data files (e.g. romaji_to_ipa.txt).
        /// </summary>
        public DictionaryProxy load_dictionary(string path, string delimiter = "=")
        {
            var dict = new Dictionary<string, string>(StringComparer.Ordinal);
            var resolved = ResolvePath(path);

            if (!File.Exists(resolved))
            {
                Console.WriteLine(
                    $"[VocalIO] Warning: dictionary not found at '{resolved}' (raw: '{path}')");
                return new DictionaryProxy(dict);
            }

            foreach (var line in File.ReadLines(resolved, System.Text.Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line) || line.StartsWith("#"))
                    continue;

                var idx = line.IndexOf(delimiter, StringComparison.Ordinal);
                if (idx < 0) continue;

                var key = line[..idx].Trim();
                var val = line[(idx + delimiter.Length)..].Trim();

                if (!string.IsNullOrEmpty(key))
                    dict[key] = val;
            }

            Console.WriteLine($"[VocalIO] Loaded dictionary '{Path.GetFileName(resolved)}'" +
                $" — {dict.Count} entries");
            return new DictionaryProxy(dict);
        }

        /// <summary>Check if a file exists at the given path.</summary>
        public bool file_exists(string path) => File.Exists(ResolvePath(path));
    }

    // -------------------------------------------------------------------------

    [MoonSharpUserData]
    public class VocalTextProxy
    {
        private readonly Script _script;

        public VocalTextProxy(Script script)
        {
            _script = script;
        }

        /// <summary>
        /// Split a string into a Lua table of individual characters.
        /// Uses StringInfo for correct Unicode grapheme cluster handling
        /// (important for Japanese kana, combining diacritics, etc.)
        /// </summary>
        public Table grapheme_split(string text)
        {
            var table = new Table(_script);
            var enumerator = System.Globalization.StringInfo.GetTextElementEnumerator(text);
            int i = 1;
            while (enumerator.MoveNext())
            {
                table.Set(i++, DynValue.NewString(enumerator.GetTextElement()));
            }
            return table;
        }

        /// <summary>Split a string by a delimiter, returning a Lua table of strings.</summary>
        public Table split(string text, string delimiter)
        {
            var table = new Table(_script);
            var parts = text.Split(new[] { delimiter },
                System.StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
                table.Set(i + 1, DynValue.NewString(parts[i].Trim()));
            return table;
        }

        /// <summary>Trim whitespace from both ends of a string.</summary>
        public string trim(string text) => text.Trim();

        /// <summary>Convert string to lowercase.</summary>
        public string lower(string text) => text.ToLowerInvariant();
    }
}

// -----------------------------------------------------------------------------

/// <summary>
/// C#-backed dictionary exposed to Lua for O(1) lookups.
/// Avoids the overhead of Lua table iteration for large phoneme maps.
/// </summary>
[MoonSharpUserData]
public class DictionaryProxy
{
    private readonly Dictionary<string, string> _dict;

    public DictionaryProxy(Dictionary<string, string> dict)
    {
        _dict = dict;
    }

    /// <summary>Get a value by key. Returns nil (empty string) if not found.</summary>
    public string? get(string key)
    {
        return _dict.TryGetValue(key, out var val) ? val : null;
    }

    /// <summary>Check if key exists.</summary>
    public bool has(string key) => _dict.ContainsKey(key);

    /// <summary>Number of entries.</summary>
    public int count() => _dict.Count;
}
