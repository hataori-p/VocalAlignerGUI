using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MoonSharp.Interpreter;

namespace Frontend.Services.Scripting;

/// <summary>
/// Manages loading, caching, and execution of Lua phonemizer profiles.
/// Replaces GetPhonemizerProfilesAsync() and GetModelInventoryAsync()
/// from PythonBridgeService.
/// </summary>
public class PhonemizerScriptingService
{
    private readonly string _profilesPath;
    private readonly string _modelsPath;
    private readonly Dictionary<string, IPhonemizerProfile> _profiles = new();
    private readonly Dictionary<string, IModelProfile> _modelProfiles = new();
    private IModelProfile? _activeModel;

    /// <summary>
    /// Fired when ActiveModel changes, so the ViewModel can refresh
    /// compatibility indicators in the dropdown.
    /// </summary>
    public event Action? ActiveModelChanged;

    public IModelProfile? ActiveModel => _activeModel;
    public IReadOnlyDictionary<string, IModelProfile> ModelProfiles => _modelProfiles;
    public IReadOnlyDictionary<string, IPhonemizerProfile> Profiles => _profiles;

    public PhonemizerScriptingService()
    {
        // Always resolve relative to the actual binary output directory.
        // This is where the build copies lua/ files via CopyToOutputDirectory.
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        _profilesPath = Path.Combine(baseDir, "lua", "phonemizers");
        _modelsPath = Path.Combine(baseDir, "lua", "models");

        Console.WriteLine($"[PhonemizerService] Profiles path: {_profilesPath}");
        Console.WriteLine($"[PhonemizerService] Models path: {_modelsPath}");
        Console.WriteLine($"[PhonemizerService] Path exists: {Directory.Exists(_profilesPath)}");

        // Register all MoonSharp user data types once at startup
        UserData.RegisterAssembly(typeof(VocalApiProxy).Assembly);
    }

    /// <summary>
    /// Scan the lua/phonemizers directory, load all valid .lua profiles,
    /// and cache them. Call once at app startup.
    /// </summary>
    public async Task LoadAllProfilesAsync()
    {
        _profiles.Clear();

        if (!Directory.Exists(_profilesPath))
        {
            Console.WriteLine($"[PhonemizerService] Profile dir not found: {_profilesPath}");
            Console.WriteLine($"[PhonemizerService] Available dirs in base: " +
                string.Join(", ", Directory.GetDirectories(
                    AppDomain.CurrentDomain.BaseDirectory)));
            Directory.CreateDirectory(_profilesPath);
            return;
        }

        var files = Directory.GetFiles(_profilesPath, "*.lua", SearchOption.AllDirectories);
        Console.WriteLine($"[PhonemizerService] Found {files.Length} Lua files in {_profilesPath}");

        foreach (var file in files)
        {
            Console.WriteLine($"[PhonemizerService] Loading: {Path.GetFileName(file)}");
            try
            {
                var profile = await LoadProfileAsync(file);
                if (profile != null)
                {
                    _profiles[profile.Id] = profile;
                    Console.WriteLine(
                        $"[PhonemizerService] Loaded: {profile.DisplayName} ({profile.Id})" +
                        $" — {profile.SupportedSymbols.Count} symbols");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[PhonemizerService] FAILED '{Path.GetFileName(file)}': " +
                    $"[{ex.GetType().Name}] {ex.Message}");
                if (ex.InnerException != null)
                    Console.WriteLine(
                        $"[PhonemizerService]   Inner: {ex.InnerException.Message}");
            }
        }

        Console.WriteLine($"[PhonemizerService] Total loaded: {_profiles.Count}");
    }

    /// <summary>
    /// Scan lua/models directory, load all valid .lua model profiles,
    /// and cache them. Call once at app startup after LoadAllProfilesAsync.
    /// </summary>
    public async Task LoadAllModelProfilesAsync()
    {
        _modelProfiles.Clear();
        _activeModel = null;

        if (!Directory.Exists(_modelsPath))
        {
            Console.WriteLine($"[PhonemizerService] Models dir not found: {_modelsPath}");
            Directory.CreateDirectory(_modelsPath);
            return;
        }

        var files = Directory.GetFiles(_modelsPath, "*.lua", SearchOption.AllDirectories);
        Console.WriteLine($"[PhonemizerService] Found {files.Length} model profiles in {_modelsPath}");

        foreach (var file in files)
        {
            try
            {
                var script = CreateIsolatedScript();
                string code = await File.ReadAllTextAsync(file, System.Text.Encoding.UTF8);
                DynValue result = await Task.Run(() => script.DoString(code));

                if (result.Type != DataType.Table)
                {
                    Console.WriteLine($"[PhonemizerService] Model '{file}' did not return a table.");
                    continue;
                }

                var modelProfile = new LuaModelProfileAdapter(result.Table);
                _modelProfiles[modelProfile.Id] = modelProfile;
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"[PhonemizerService] FAILED model '{Path.GetFileName(file)}': " +
                    $"[{ex.GetType().Name}] {ex.Message}");
            }
        }

        Console.WriteLine($"[PhonemizerService] Total model profiles loaded: {_modelProfiles.Count}");
    }

    /// <summary>
    /// Given a path to a loaded .onnx model file, scan loaded model profiles
    /// and activate the one whose model_file matches (filename comparison only).
    /// Sets ActiveModel. Returns true if a match was found.
    /// </summary>
    public void ActivateModel(IModelProfile model)
    {
        _activeModel = model;
        Console.WriteLine($"[PhonemizerService] Active model set: {model.DisplayName}");
        ActiveModelChanged?.Invoke();
    }

    public void ClearActiveModel()
    {
        _activeModel = null;
        Console.WriteLine("[PhonemizerService] Active model cleared.");
        ActiveModelChanged?.Invoke();
    }

    public bool TryActivateModelForFile(string onnxFilePath)
    {
        string onnxFileName = Path.GetFileName(onnxFilePath);

        foreach (var model in _modelProfiles.Values)
        {
            string manifestFileName = Path.GetFileName(model.ModelFile);
            if (string.Equals(manifestFileName, onnxFileName,
                    StringComparison.OrdinalIgnoreCase))
            {
                _activeModel = model;
                Console.WriteLine(
                    $"[PhonemizerService] Active model set: {model.DisplayName} ({model.Id})" +
                    $" — {model.PhonemeSet.Count} phonemes");
                ActiveModelChanged?.Invoke();
                return true;
            }
        }

        Console.WriteLine(
            $"[PhonemizerService] No model profile found for: {onnxFileName}");
        return false;
    }

    /// <summary>
    /// Returns the compatibility level of a phonemizer profile against the active model.
    /// Returns Unknown if no active model is set.
    /// </summary>
    public CompatibilityLevel GetCompatibilityLevel(string profileId)
    {
        if (_activeModel == null) return CompatibilityLevel.Unknown;
        if (!_profiles.TryGetValue(profileId, out var profile))
            return CompatibilityLevel.Unknown;
        return _activeModel.ScoreCompatibility(profile);
    }

    /// <summary>
    /// Load a single Lua profile file and wrap it in a LuaPhonemizerAdapter.
    /// </summary>
    private async Task<IPhonemizerProfile?> LoadProfileAsync(string path)
    {
        var script = CreateIsolatedScript();
        string code = await File.ReadAllTextAsync(path, System.Text.Encoding.UTF8);

        DynValue result = await Task.Run(() => script.DoString(code));

        if (result.Type != DataType.Table)
        {
            Console.WriteLine($"[PhonemizerService] '{path}' did not return a table.");
            return null;
        }

        return new LuaPhonemizerAdapter(result.Table, script);
    }

    /// <summary>
    /// Create an isolated MoonSharp Script instance with the vocal API registered.
    /// Each profile gets its own instance — no shared state between profiles.
    /// </summary>
    private Script CreateIsolatedScript()
    {
        var script = new Script();

        // Point the module loader at the binary output directory so that
        // require() and vocal.io paths resolve correctly at runtime.
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        script.Options.ScriptLoader = new LuaScriptLoader
        {
            ModulePaths = new[] 
            { 
                Path.Combine(baseDir, "?.lua"),
                Path.Combine(baseDir, "lua", "?.lua"),
                Path.Combine(baseDir, "lua", "phonemizers", "?.lua"),
            }
        };

        // Register the vocal global API
        script.Globals["vocal"] = new VocalApiProxy(script);

        return script;
    }

    /// <summary>
    /// Convenience: get a profile by ID, or null if not found.
    /// </summary>
    public IPhonemizerProfile? GetProfile(string id)
    {
        return _profiles.TryGetValue(id, out var p) ? p : null;
    }

    /// <summary>
    /// Returns all loaded profile display names, for populating UI dropdowns.
    /// Replaces GetPhonemizerProfilesAsync().
    /// </summary>
    public IReadOnlyList<string> GetProfileIds() =>
        new List<string>(_profiles.Keys);

    public IReadOnlyList<string> GetProfileDisplayNames() =>
        new List<string>(
            System.Linq.Enumerable.Select(_profiles.Values, p => p.DisplayName));

    /// <summary>
    /// Reload a single profile by ID (e.g. after user edits a .lua file).
    /// No-op if profile with that ID isn't currently loaded.
    /// </summary>
    public async Task<bool> ReloadProfileAsync(string id)
    {
        var existing = _profiles.Values
            .FirstOrDefault(p => p.Id == id);
        if (existing == null) return false;

        // Find the file that produced this profile
        if (!Directory.Exists(_profilesPath)) return false;

        foreach (var file in Directory.GetFiles(_profilesPath, "*.lua",
            SearchOption.AllDirectories))
        {
            try
            {
                var profile = await LoadProfileAsync(file);
                if (profile?.Id == id)
                {
                    _profiles[id] = profile;
                    Console.WriteLine($"[PhonemizerService] Reloaded: {profile.DisplayName}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[PhonemizerService] Reload failed for '{id}': {ex.Message}");
                return false;
            }
        }
        return false;
    }

    /// <summary>
    /// Validate a list of phoneme symbols against the active profile's inventory.
    /// Returns list of symbols that failed validation (empty = all valid).
    /// </summary>
    public IReadOnlyList<string> ValidateSymbols(
        string profileId,
        IReadOnlyList<string> symbols)
    {
        if (!_profiles.TryGetValue(profileId, out var profile))
            return symbols; // Unknown profile = can't validate

        return System.Linq.Enumerable.ToList(
            System.Linq.Enumerable.Where(symbols, s => !profile.ValidateSymbol(s)));
    }
}
