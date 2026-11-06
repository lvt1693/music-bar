using System;
using System.Collections.Generic;

namespace MusicBar.Infrastructure;

/// <summary>
/// Every string the UI shows, in English and Vietnamese, keyed by a name made of letters, digits
/// and underscores (it ends up inside a binding path). Each area of the app adds its own in a
/// Strings.*.cs file; a key added twice fails at startup.
/// </summary>
public static partial class Strings
{
    public static IReadOnlyDictionary<string, (string En, string Vi)> All { get; } = Build();

    static Dictionary<string, (string En, string Vi)> Build()
    {
        var d = new Dictionary<string, (string En, string Vi)>(StringComparer.Ordinal);
        AddApp(d);
        AddSettings(d);
        AddLyrics(d);
        AddFace(d);
        AddRecap(d);
        AddWelcome(d);
        return d;
    }

    static partial void AddApp(Dictionary<string, (string En, string Vi)> d);

    static partial void AddSettings(Dictionary<string, (string En, string Vi)> d);

    static partial void AddLyrics(Dictionary<string, (string En, string Vi)> d);

    static partial void AddFace(Dictionary<string, (string En, string Vi)> d);

    static partial void AddRecap(Dictionary<string, (string En, string Vi)> d);

    static partial void AddWelcome(Dictionary<string, (string En, string Vi)> d);
}
