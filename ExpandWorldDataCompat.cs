using System;
using System.Reflection;
using BepInEx.Bootstrap;

namespace NoSnowyBiome
{
    internal sealed class ExpandWorldDataCompat
    {
        internal const string PluginGuid = "expand_world_data";
        private delegate bool TryGetBiomeDelegate(string name, out Heightmap.Biome biome);
        private TryGetBiomeDelegate? _tryGetBiome;
        internal bool Available => _tryGetBiome != null;

        internal void Initialize()
        {
            if (!Chainloader.PluginInfos.TryGetValue(PluginGuid, out var plugin)) return;
            try
            {
                Type? manager = plugin.Instance.GetType().Assembly.GetType("ExpandWorldData.BiomeManager");
                MethodInfo? method = manager?.GetMethod("TryGetBiome", BindingFlags.Public | BindingFlags.Static,
                    null, new[] { typeof(string), typeof(Heightmap.Biome).MakeByRefType() }, null);
                if (method == null || method.ReturnType != typeof(bool))
                    throw new MissingMethodException("ExpandWorldData.BiomeManager.TryGetBiome");
                _tryGetBiome = (TryGetBiomeDelegate)Delegate.CreateDelegate(typeof(TryGetBiomeDelegate), method);
            }
            catch (Exception ex)
            {
                NoSnowyBiomePlugin.Log.LogWarning($"EWD name lookup unavailable; vanilla rules still work: {ex.Message}");
            }
        }

        internal bool TryGetBiome(string name, out Heightmap.Biome biome)
        {
            biome = Heightmap.Biome.None;
            if (_tryGetBiome == null) return false;
            try
            {
                return _tryGetBiome(name, out biome) && biome != Heightmap.Biome.None;
            }
            catch (Exception ex)
            {
                _tryGetBiome = null;
                NoSnowyBiomePlugin.Log.LogWarning($"EWD name lookup failed; disabling the adapter: {ex.Message}");
                return false;
            }
        }
    }
}
