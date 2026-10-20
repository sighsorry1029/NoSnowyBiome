using BepInEx.Bootstrap;
using NoSnowyBiome;
using Manager = ExpandWorldData.Biome.BiomeManager;

internal static class EwdCompatRegression
{
    internal static void Run(Action<bool, string> check)
    {
        Chainloader.PluginInfos.Clear();
        NoSnowyBiomePlugin.Log.Warnings.Clear();
        try
        {
            var absent = new ExpandWorldDataCompat();
            absent.Initialize();
            check(!absent.Available && !absent.TryGetBiome("Mountain", out _), "EWD absent leaves adapter inactive");
            check(NoSnowyBiomePlugin.Log.Warnings.Count == 0, "optional EWD absence does not warn");

            Chainloader.PluginInfos[ExpandWorldDataCompat.PluginGuid] = new TestPluginInfo(new object());
            var incompatible = new ExpandWorldDataCompat();
            incompatible.Initialize();
            check(!incompatible.Available, "missing public manager leaves adapter inactive");
            check(NoSnowyBiomePlugin.Log.Warnings.Count == 1, "missing EWD API emits one diagnostic");
            NoSnowyBiomePlugin.Log.Warnings.Clear();

            Chainloader.PluginInfos[ExpandWorldDataCompat.PluginGuid] = new TestPluginInfo(new Manager());
            Manager.Names = new Dictionary<string, Heightmap.Biome> { ["mountain"] = Heightmap.Biome.Mountain };
            var adapter = new ExpandWorldDataCompat();
            adapter.Initialize();
            check(adapter.Available && NoSnowyBiomePlugin.Log.Warnings.Count == 0, "EWD 1.79 manager namespace binds without warning");
            check(adapter.TryGetBiome("MoUnTaIn", out var biome) && biome == Heightmap.Biome.Mountain, "public lookup preserves EWD case handling");
            check(!adapter.TryGetBiome("CustomOne", out _), "custom name unresolved before EWD sync");

            var rules = SnowRules.Parse("- biome: CustomOne\n  noSnow: true");
            int? Resolve(string name) => adapter.TryGetBiome(name, out var id) ? (int)id : null;
            check(SnowRules.Resolve(rules, Resolve).Count == 0, "unresolved custom rules stay inactive");
            Manager.Names["customone"] = (Heightmap.Biome)1024;
            var initial = SnowRules.Resolve(rules, Resolve);
            check(initial.ContainsKey(1024) && SnowRules.ShouldSuppress(initial, 1024, 50), "late EWD mapping enables custom rule");
            Manager.Names = new Dictionary<string, Heightmap.Biome> { ["customone"] = (Heightmap.Biome)2048 };
            var remapped = SnowRules.Resolve(rules, Resolve);
            check(remapped.ContainsKey(2048) && !remapped.ContainsKey(1024), "cached delegate reads replaced EWD mapping");
            check(!SnowRules.Equivalent(initial, remapped), "EWD ID remap invalidates resolved rules");
            Manager.Names["customone"] = (Heightmap.Biome)int.MinValue;
            check(adapter.TryGetBiome("CustomOne", out biome) && (int)biome == int.MinValue, "signed custom biome ID preserved");
            Manager.Names["customone"] = Heightmap.Biome.None;
            check(!adapter.TryGetBiome("CustomOne", out _), "None is not a usable biome");
            Manager.Names.Clear();
            check(SnowRules.Resolve(rules, Resolve).Count == 0, "removed EWD mapping clears custom rule");

            Manager.ThrowOnLookup = true;
            check(!adapter.TryGetBiome("CustomOne", out _) && !adapter.Available, "lookup exception disables optional adapter");
            check(NoSnowyBiomePlugin.Log.Warnings.Count == 1, "lookup failure emits one diagnostic");
            adapter.TryGetBiome("CustomOne", out _);
            check(NoSnowyBiomePlugin.Log.Warnings.Count == 1, "disabled adapter does not spam warnings");
        }
        finally
        {
            Chainloader.PluginInfos.Clear();
            Manager.Names.Clear();
            Manager.ThrowOnLookup = false;
            NoSnowyBiomePlugin.Log.Warnings.Clear();
        }
    }
}

// Managed fixtures exercise the production reflection adapter, not EWD's Unity initialization.
namespace BepInEx.Bootstrap
{
    internal static class Chainloader
    {
        internal static readonly Dictionary<string, TestPluginInfo> PluginInfos = new();
    }

    internal sealed class TestPluginInfo
    {
        internal object Instance { get; }
        internal TestPluginInfo(object instance) => Instance = instance;
    }
}

namespace ExpandWorldData.Biome
{
    public sealed class BiomeManager
    {
        internal static Dictionary<string, Heightmap.Biome> Names = new();
        internal static bool ThrowOnLookup;

        public static bool TryGetBiome(string name, out Heightmap.Biome biome)
        {
            if (ThrowOnLookup) throw new InvalidOperationException("Simulated EWD lookup failure");
            return Names.TryGetValue(name.ToLowerInvariant(), out biome);
        }
    }
}
