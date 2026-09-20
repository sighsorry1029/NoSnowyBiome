using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using ServerSync;
using UnityEngine;
using UnityEngine.Rendering;

namespace NoSnowyBiome
{
    [BepInPlugin(ModGuid, ModName, ModVersion)]
    [BepInDependency(ExpandWorldDataCompat.PluginGuid, BepInDependency.DependencyFlags.SoftDependency)]
    public sealed class NoSnowyBiomePlugin : BaseUnityPlugin
    {
        internal const string ModName = "NoSnowyBiome";
        internal const string ModVersion = "1.1.1";
        internal const string Author = "sighsorry";
        internal const string ModGuid = $"{Author}.{ModName}";

        private readonly Harmony _harmony = new Harmony(ModGuid);
        private readonly ExpandWorldDataCompat _ewd = new ExpandWorldDataCompat();
        private ConfigSync _configSync = null!;
        private CustomSyncedValue<string> _syncedRules = null!;
        private SnowRuleFile _ruleFile = null!;
        private List<BiomeSnowRule> _entries = new List<BiomeSnowRule>();
        private Dictionary<int, BiomeSnowRule> _rules = new Dictionary<int, BiomeSnowRule>();
        private Dictionary<int, BiomeSnowRule> _ruleBuffer = new Dictionary<int, BiomeSnowRule>();
        private Func<string, int?> _resolveBiome = null!;
        private WorldGenerator? _world;
        private float _nextPoll;
        private bool _renderingEnabled;

        internal static NoSnowyBiomePlugin? Instance { get; private set; }
        internal static ManualLogSource Log = null!;
        internal static ConfigEntry<bool> ModEnabled = null!;
        internal static ConfigEntry<bool> VerboseLogging = null!;
        internal int RulesRevision { get; private set; }

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            _renderingEnabled = SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;
            _configSync = new ConfigSync(ModGuid)
            {
                DisplayName = ModName,
                CurrentVersion = ModVersion,
                ModRequired = false,
                IsLocked = true
            };
            ModEnabled = Config.Bind("General", "Enabled", true,
                "Enables snow suppression. Synchronized from the server when it has NoSnowyBiome installed.");
            VerboseLogging = Config.Bind("Debug", "VerboseLogging", false,
                "Logs local biome resolution and suppression decisions. Not synchronized.");
            _configSync.AddConfigEntry(ModEnabled);
            ModEnabled.SettingChanged += EnabledChanged;

            _ewd.Initialize();
            _resolveBiome = ResolveBiome;
            _syncedRules = new CustomSyncedValue<string>(_configSync, "SnowRules", "[]");
            _syncedRules.ValueChanged += SyncedRulesChanged;
            _ruleFile = new SnowRuleFile(Path.Combine(Paths.ConfigPath, "NoSnowyBiome.yaml"),
                message => Log.LogWarning(message));
            _ruleFile.EnsureExists();
            PollRules();

            // ServerSync installs its own patches. Do not patch the entire merged assembly again.
            if (_renderingEnabled)
                _harmony.PatchAll(typeof(AttachSnowSuppressorPatches));
            Log.LogInfo($"{ModName} {ModVersion} loaded (EWD integration: {_ewd.Available})");
        }

        private void Update()
        {
            if (Time.realtimeSinceStartup >= _nextPoll)
            {
                _nextPoll = Time.realtimeSinceStartup + 1f;
                PollRules();
            }
            if (_renderingEnabled) ConditionalSnowSuppressor.RefreshFrame();
        }

        private void PollRules()
        {
            if (_ruleFile.TryRead(out string yaml))
            {
                // On a connected client this only updates the local value restored on disconnect.
                _syncedRules.AssignLocalValue(yaml);
            }

            RefreshResolvedRules();
            if (!ReferenceEquals(_world, WorldGenerator.instance))
            {
                _world = WorldGenerator.instance;
                InvalidateRules();
            }
        }

        private void SyncedRulesChanged()
        {
            try
            {
                _entries = SnowRules.Parse(_syncedRules.Value);
                RefreshResolvedRules();
                Log.LogInfo($"Loaded {_entries.Count} snow rules ({(_configSync.IsSourceOfTruth ? "local" : "server")}).");
            }
            catch (Exception ex)
            {
                Log.LogWarning($"Rejected invalid synchronized snow rules; keeping the previous rules: {ex.Message}");
            }
        }

        private void RefreshResolvedRules()
        {
            // Re-resolve names after EWD sync/reloads without intercepting its YAML loaders.
            Dictionary<int, BiomeSnowRule> rules = SnowRules.Resolve(_entries, _resolveBiome, _ruleBuffer);
            if (SnowRules.Equivalent(_rules, rules)) return;
            _ruleBuffer = _rules;
            _rules = rules;
            InvalidateRules();
        }

        private int? ResolveBiome(string name)
        {
            if (_ewd.TryGetBiome(name, out Heightmap.Biome custom)) return (int)custom;
            if (Enum.TryParse(name, true, out Heightmap.Biome biome) &&
                Enum.IsDefined(typeof(Heightmap.Biome), biome) && biome != Heightmap.Biome.None)
                return (int)biome;
            return null;
        }

        private void EnabledChanged(object sender, EventArgs args) => InvalidateRules();

        private void InvalidateRules()
        {
            unchecked { RulesRevision++; }
            if (_renderingEnabled) ConditionalSnowSuppressor.RequestRefresh();
        }

        internal bool ShouldSuppress(Vector3 position, out Heightmap.Biome biome, out float altitude)
        {
            altitude = position.y - (ZoneSystem.instance != null ? ZoneSystem.instance.m_waterLevel : 30f);
            biome = Heightmap.Biome.None;
            if (!ModEnabled.Value || WorldGenerator.instance == null) return false;
            biome = WorldGenerator.instance.GetBiome(position);
            return SnowRules.ShouldSuppress(_rules, (int)biome, altitude);
        }

        internal static void TryAttach(GameObject gameObject)
        {
            if (Instance == null || !Instance._renderingEnabled || gameObject == null ||
                gameObject.GetComponentInParent<ConditionalSnowSuppressor>(true) != null) return;

            if (SnowMaterialState.HasSnowMaterials(gameObject))
                gameObject.AddComponent<ConditionalSnowSuppressor>();
        }

        private void OnDestroy()
        {
            ModEnabled.SettingChanged -= EnabledChanged;
            _syncedRules.ValueChanged -= SyncedRulesChanged;
            ConditionalSnowSuppressor.RestoreAll();
            _harmony.UnpatchSelf();
            Instance = null;
        }
    }

    [HarmonyPatch]
    internal static class AttachSnowSuppressorPatches
    {
        [HarmonyTargetMethods]
        private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
        {
            foreach (Type type in new[] { typeof(StaticPhysics), typeof(Pickable), typeof(PickableItem),
                typeof(Plant), typeof(TreeBase), typeof(TreeLog), typeof(Destructible), typeof(MineRock5), typeof(Piece) })
                yield return AccessTools.DeclaredMethod(type, "Awake");
        }

        [HarmonyPostfix]
        private static void AwakePostfix(MonoBehaviour __instance)
        {
            NoSnowyBiomePlugin.TryAttach(__instance.gameObject);
        }
    }
}
