using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MoonSharp.Interpreter;

namespace Frontend.Services.Scripting;

/// <summary>
/// Wraps a loaded Lua phonemizer module table and exposes it as IPhonemizerProfile.
/// 
/// Expected Lua module contract:
///   profile.id             : string  (required)
///   profile.display_name   : string  (required)
///   profile.encoding       : string  (optional, default "IPA")
///   profile.supported_symbols : table of strings (optional)
///   profile.g2p_batch(lines_table) -> table of strings  (required)
///   profile.try_p2g(phonemes_table) -> string or nil    (optional)
///   profile.validate_symbol(symbol) -> bool             (optional)
/// </summary>
public sealed class LuaPhonemizerAdapter : IPhonemizerProfile
{
    private readonly Table _profileTable;
    private readonly Script _script;
    private readonly HashSet<string> _supportedSymbols;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public string Id { get; }
    public string DisplayName { get; }
    public PhonemeEncoding OutputEncoding { get; }
    public IReadOnlySet<string> SupportedSymbols => _supportedSymbols;

    public LuaPhonemizerAdapter(Table profileTable, Script script)
    {
        _profileTable = profileTable;
        _script = script;

        Id = ReadRequiredString("id");
        DisplayName = ReadRequiredString("display_name");

        string encodingStr = ReadString("encoding") ?? "IPA";
        OutputEncoding = Enum.TryParse<PhonemeEncoding>(encodingStr, true, out var enc)
            ? enc : PhonemeEncoding.IPA;

        _supportedSymbols = LoadSupportedSymbols();
    }

    public async Task<IReadOnlyList<string>> PhonemizeListAsync(IReadOnlyList<string> inputs)
    {
        var func = _profileTable.Get("g2p_batch");
        if (func.Type != DataType.Function)
            throw new InvalidOperationException(
                $"[{Id}] Lua profile missing required 'g2p_batch' function.");

        await _lock.WaitAsync();
        try
        {
            var inputTable = BuildStringTable(inputs);
            var result = await Task.Run(() => _script.Call(func, inputTable));
            return ParseStringList(result, "g2p_batch");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[LuaPhonemizerAdapter] '{Id}' PhonemizeListAsync failed: {ex.Message}");
            return inputs.ToList();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<string?> TryDePhonemizeAsync(IReadOnlyList<string> phonemes)
    {
        var func = _profileTable.Get("try_p2g");
        if (func.Type != DataType.Function)
            return null;

        await _lock.WaitAsync();
        try
        {
            var inputTable = BuildStringTable(phonemes);
            var result = await Task.Run(() => _script.Call(func, inputTable));
            return result.Type == DataType.String ? result.String : null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[LuaPhonemizerAdapter] '{Id}' TryDePhonemizeAsync failed: {ex.Message}");
            return null;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<IReadOnlyList<string>?> TranscodeSequenceAsync(IReadOnlyList<string> phonemes)
    {
        var func = _profileTable.Get("transcode_sequence");
        if (func.IsNil() || func.Type != DataType.Function)
            return null;

        await _lock.WaitAsync();
        try
        {
            var inputTable = BuildStringTable(phonemes);
            var result = await Task.Run(() => _script.Call(func, inputTable));

            var output = ParseStringList(result, "transcode_sequence");

            // Enforce count preservation
            if (output.Count != phonemes.Count)
                throw new InvalidOperationException(
                    $"[{Id}] transcode_sequence count mismatch: " +
                    $"input={phonemes.Count}, output={output.Count}");

            return output;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[LuaPhonemizerAdapter] '{Id}' TranscodeSequenceAsync failed: {ex.Message}");
            return phonemes.ToList();
        }
        finally
        {
            _lock.Release();
        }
    }

    public bool ValidateSymbol(string symbol)
    {
        // Fast path: use C# set if populated
        if (_supportedSymbols.Count > 0)
            return _supportedSymbols.Contains(symbol);

        // Optional: delegate to Lua validate_symbol
        var func = _profileTable.Get("validate_symbol");
        if (func.Type != DataType.Function)
            return true; // No validation = pass-through

        try
        {
            var result = _script.Call(func, symbol);
            return result.Type == DataType.Boolean && result.Boolean;
        }
        catch
        {
            return true; // Fail open for validation
        }
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private string ReadRequiredString(string key)
    {
        var val = _profileTable.Get(key);
        if (val.Type != DataType.String || string.IsNullOrWhiteSpace(val.String))
            throw new InvalidOperationException(
                $"[LuaPhonemizerAdapter] Lua profile missing required string field '{key}'.");
        return val.String;
    }

    private string? ReadString(string key)
    {
        var val = _profileTable.Get(key);
        return val.Type == DataType.String ? val.String : null;
    }

    private HashSet<string> LoadSupportedSymbols()
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        var val = _profileTable.Get("supported_symbols");
        if (val.Type != DataType.Table) return set;

        foreach (var pair in val.Table.Pairs)
            if (pair.Value.Type == DataType.String)
                set.Add(pair.Value.String);

        return set;
    }

    private Table BuildStringTable(IReadOnlyList<string> items)
    {
        var table = new Table(_script);
        for (int i = 0; i < items.Count; i++)
            table.Set(i + 1, DynValue.NewString(items[i]));
        return table;
    }

    private IReadOnlyList<string> ParseStringList(DynValue result, string context)
    {
        if (result.Type != DataType.Table)
            throw new InvalidOperationException(
                $"[{Id}] '{context}' must return a table of strings, got {result.Type}.");

        var list = new List<string>();
        // Iterate by integer keys (1-indexed array) to preserve order
        int i = 1;
        while (true)
        {
            var val = result.Table.Get(i);
            if (val.IsNil()) break;
            list.Add(val.Type == DataType.String ? val.String : string.Empty);
            i++;
        }
        return list;
    }
}

/// <summary>
/// Wraps a loaded Lua model profile table and exposes it as IModelProfile.
///
/// Expected Lua module contract:
///   model.id           : string  (required)
///   model.display_name : string  (required)
///   model.model_file   : string  (required)
///   model.encoding     : string  (optional, default "IPA")
///   model.phoneme_set  : table of strings (required)
/// </summary>
public sealed class LuaModelProfileAdapter : IModelProfile
{
    private readonly Table _modelTable;
    private readonly HashSet<string> _phonemeSet;
    private readonly string? _refinerFile;

    public bool IsManualMode =>
        _modelTable.Get("mode").Type == DataType.String &&
        string.Equals(
            _modelTable.Get("mode").String,
            "manual",
            StringComparison.OrdinalIgnoreCase);

    public string Id { get; }
    public string DisplayName { get; }
    public string ModelFile { get; }
    public string? RefinerFile => _refinerFile;
    public PhonemeEncoding Encoding { get; }
    public IReadOnlySet<string> PhonemeSet => _phonemeSet;

    public LuaModelProfileAdapter(Table modelTable)
    {
        _modelTable = modelTable;

        Id          = ReadRequiredString("id");
        DisplayName = ReadRequiredString("display_name");
        ModelFile   = ReadString("model_file") ?? string.Empty;
        _refinerFile = ReadString("refiner_file"); // nullable — null if not declared in Lua

        string encodingStr = ReadString("encoding") ?? "IPA";
        Encoding = Enum.TryParse<PhonemeEncoding>(encodingStr, true, out var enc)
            ? enc : PhonemeEncoding.IPA;

        _phonemeSet = LoadPhonemeSet();

        Console.WriteLine(
            $"[ModelProfile] Loaded: {DisplayName} ({Id})" +
            $" — {_phonemeSet.Count} phonemes, file='{ModelFile}'");
    }

    private string ReadRequiredString(string key)
    {
        var val = _modelTable.Get(key);
        if (val.Type != DataType.String || string.IsNullOrWhiteSpace(val.String))
            throw new InvalidOperationException(
                $"[LuaModelProfileAdapter] Lua model profile missing required field '{key}'.");
        return val.String;
    }

    private string? ReadString(string key)
    {
        var val = _modelTable.Get(key);
        return val.Type == DataType.String ? val.String : null;
    }

    private HashSet<string> LoadPhonemeSet()
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        var val = _modelTable.Get("phoneme_set");
        if (val.Type != DataType.Table) return set;

        foreach (var pair in val.Table.Pairs)
            if (pair.Value.Type == DataType.String)
                set.Add(pair.Value.String);

        return set;
    }
}
