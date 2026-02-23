using System;
using System.IO;
using MoonSharp.Interpreter;
using MoonSharp.Interpreter.Loaders;

namespace Frontend.Services.Scripting;

public class LuaScriptLoader : ScriptLoaderBase
{
    private readonly string _basePath;

    public LuaScriptLoader()
    {
        _basePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lua");
    }

    public override object LoadFile(string file, Table globalContext)
    {
        string path = ResolvePath(file);
        return File.ReadAllText(path, System.Text.Encoding.UTF8);
    }

    public override bool ScriptFileExists(string file)
    {
        return File.Exists(ResolvePath(file));
    }

    private string ResolvePath(string file)
    {
        file = file.Replace('/', Path.DirectorySeparatorChar);
        if (!file.EndsWith(".lua", StringComparison.OrdinalIgnoreCase))
            file += ".lua";
        return Path.Combine(_basePath, file);
    }
}
