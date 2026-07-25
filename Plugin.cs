using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace NoSnowyBiome
{
    [BepInPlugin(ModGuid, ModName, ModVersion)]
    [BepInDependency("expand_world_data", "1.68")]
    public sealed class NoSnowyBiomePlugin : BaseUnityPlugin
    {
        internal const string ModName = "NoSnowyBiome";
        internal const string ModVersion = "1.1.0";
        internal const string Author = "sighsorry";
        internal const string ModGuid = $"{Author}.{ModName}";
        internal const float DefaultMinimumAltitude = 50f;
        internal static readonly int AddSnowId = Shader.PropertyToID("_AddSnow");
        internal const string AddSnowKeyword = "_ADDSNOW_ON";

        private static Dictionary<Heightmap.Biome, BiomeRule> _yamlRules = new Dictionary<Heightmap.Biome, BiomeRule>();
        private static int _suppressionCacheVersion;

        private readonly Harmony _harmony = new Harmony(ModGuid);

        internal static ManualLogSource Log = null!;
        internal static ConfigEntry<bool> ModEnabled = null!;
        internal static ConfigEntry<bool> VerboseLogging = null!;

        private void Awake()
        {
            Log = Logger;

            ModEnabled = Config.Bind(
                "General",
                "Enabled",
                true,
                "Enables conditional snow suppression for configured biomes."
            );

            VerboseLogging = Config.Bind(
                "Debug",
                "VerboseLogging",
                false,
                "Logs biome resolution and suppression decisions."
            );

            ModEnabled.SettingChanged += (_, _) =>
            {
                InvalidateSuppressionCache();
            };

            ExpandWorldDataCompat.Initialize(_harmony);
            _harmony.PatchAll(Assembly.GetExecutingAssembly());
            Log.LogInfo($"{ModName} {ModVersion} loaded");
        }

        private void OnDestroy()
        {
            _harmony.UnpatchSelf();
        }

        internal static void TryAttach(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return;
            }

            if (gameObject.GetComponent<ConditionalSnowSuppressor>() != null)
            {
                return;
            }

            if (gameObject.GetComponentInChildren<MeshRenderer>(true) == null &&
                gameObject.GetComponentInChildren<SkinnedMeshRenderer>(true) == null)
            {
                return;
            }

            gameObject.AddComponent<ConditionalSnowSuppressor>();
        }

        internal static bool TryApplySnowSuppression(GameObject gameObject, out Heightmap.Biome biome, out float altitude)
        {
            biome = Heightmap.Biome.None;
            altitude = 0f;

            if (gameObject == null || WorldGenerator.instance == null)
            {
                return false;
            }

            if (!ShouldSuppressSnowForGameObject(gameObject, out biome, out altitude))
            {
                return false;
            }

            ApplyRendererPropertyBlocks(gameObject);
            ApplyMaterialSnowState(gameObject, suppress: true, restoreOriginalValues: false);

            return true;
        }

        internal static void RefreshObjectSnowSuppression(GameObject gameObject, string reason = "refresh")
        {
            if (gameObject == null)
            {
                return;
            }

            HashSet<int> seen = new HashSet<int>();
            RefreshObjectSnowSuppression(gameObject, seen, reason);
        }

        internal static bool ShouldSuppressSnow(Vector3 position, out Heightmap.Biome biome, out float altitude)
        {
            biome = Heightmap.Biome.None;
            altitude = GetAltitude(position);

            if (!ModEnabled.Value)
            {
                return false;
            }

            if (!TryGetBiome(position, out biome))
            {
                return false;
            }

            if (_yamlRules.TryGetValue(biome, out BiomeRule yamlRule) && yamlRule.HasObjectSnowRule)
            {
                return yamlRule.SuppressObjectSnow && altitude >= yamlRule.ObjectMinimumAltitude;
            }

            return false;
        }

        internal static float GetAltitude(Vector3 position)
        {
            return position.y - GetWaterLevel();
        }

        internal static float GetWaterLevel()
        {
            return ZoneSystem.instance != null ? ZoneSystem.instance.m_waterLevel : 30f;
        }

        internal static bool ShouldSuppressSnowForGameObject(GameObject gameObject, out Heightmap.Biome biome, out float altitude)
        {
            biome = Heightmap.Biome.None;
            altitude = 0f;

            if (gameObject == null)
            {
                return false;
            }

            if (WorldGenerator.instance == null)
            {
                return ShouldSuppressSnow(gameObject.transform.position, out biome, out altitude);
            }

            SnowSuppressionState state = gameObject.GetComponent<SnowSuppressionState>() ?? gameObject.AddComponent<SnowSuppressionState>();
            Vector3 position = gameObject.transform.position;
            if (state.TryGet(_suppressionCacheVersion, position, out bool shouldSuppress, out biome, out altitude))
            {
                return shouldSuppress;
            }

            shouldSuppress = ShouldSuppressSnow(position, out biome, out altitude);
            state.Set(_suppressionCacheVersion, position, shouldSuppress, biome, altitude);
            return shouldSuppress;
        }

        private static void ApplyRendererPropertyBlocks(GameObject gameObject)
        {
            ApplyRendererPropertyBlocks(gameObject.GetComponentsInChildren<MeshRenderer>(true));
            ApplyRendererPropertyBlocks(gameObject.GetComponentsInChildren<SkinnedMeshRenderer>(true));
        }

        private static void RefreshObjectSnowSuppression(GameObject? gameObject, ISet<int> seen, string reason)
        {
            if (gameObject == null || !seen.Add(gameObject.GetInstanceID()))
            {
                return;
            }

            TryAttach(gameObject);
            TryApplySnowSuppression(gameObject, out _, out _);
        }

        private static void ApplyRendererPropertyBlocks<T>(T[] renderers) where T : Renderer
        {
            foreach (T renderer in renderers)
            {
                if (renderer == null)
                {
                    continue;
                }

                MaterialPropertyBlock block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block);
                block.SetFloat(AddSnowId, 0f);
                renderer.SetPropertyBlock(block);
            }
        }

        private static void ApplyMaterialSnowState(GameObject gameObject, bool suppress, bool restoreOriginalValues)
        {
            SnowMaterialState state = gameObject.GetComponent<SnowMaterialState>() ?? gameObject.AddComponent<SnowMaterialState>();
            state.Apply(suppress, restoreOriginalValues);
        }

        private static bool TryGetBiome(Vector3 position, out Heightmap.Biome biome)
        {
            biome = Heightmap.Biome.None;

            if (WorldGenerator.instance == null)
            {
                return false;
            }

            biome = WorldGenerator.instance.GetBiome(position);
            return biome != Heightmap.Biome.None;
        }

        internal static void ReplaceYamlRules(Dictionary<Heightmap.Biome, BiomeRule> rules)
        {
            _yamlRules = rules ?? new Dictionary<Heightmap.Biome, BiomeRule>();
            InvalidateSuppressionCache();
        }

        internal static void InvalidateSuppressionCache()
        {
            unchecked
            {
                _suppressionCacheVersion++;
            }
        }

        internal readonly struct BiomeRule
        {
            internal BiomeRule(bool? suppressObjectSnow, float objectMinimumAltitude)
            {
                HasObjectSnowRule = suppressObjectSnow.HasValue;
                SuppressObjectSnow = suppressObjectSnow.GetValueOrDefault();
                ObjectMinimumAltitude = objectMinimumAltitude;
            }

            internal bool HasObjectSnowRule { get; }
            internal bool SuppressObjectSnow { get; }
            internal float ObjectMinimumAltitude { get; }
        }

    }

    internal sealed class ConditionalSnowSuppressor : MonoBehaviour
    {
        private const int MaxAttempts = 12;

        private IEnumerator Start()
        {
            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                ApplyState state = TryApply();
                if (state != ApplyState.Pending)
                {
                    break;
                }

                yield return null;
            }

            Destroy(this);
        }

        private ApplyState TryApply()
        {
            if (!NoSnowyBiomePlugin.TryApplySnowSuppression(gameObject, out Heightmap.Biome biome, out float altitude))
            {
                return WorldGenerator.instance == null ? ApplyState.Pending : ApplyState.Rejected;
            }

            if (NoSnowyBiomePlugin.VerboseLogging.Value)
            {
                NoSnowyBiomePlugin.Log.LogInfo($"Suppressed snow on '{gameObject.name}' in biome {biome} at altitude {altitude:0.##}");
            }

            return ApplyState.Applied;
        }

        private enum ApplyState
        {
            Pending,
            Applied,
            Rejected
        }
    }

    internal sealed class SnowSuppressionState : MonoBehaviour
    {
        private const float PositionToleranceSqr = 0.0001f;

        private int _version = int.MinValue;
        private bool _initialized;
        private Vector3 _position;
        private bool _shouldSuppress;
        private Heightmap.Biome _biome;
        private float _altitude;

        internal bool TryGet(int version, Vector3 position, out bool shouldSuppress, out Heightmap.Biome biome, out float altitude)
        {
            if (_initialized && _version == version && (position - _position).sqrMagnitude <= PositionToleranceSqr)
            {
                shouldSuppress = _shouldSuppress;
                biome = _biome;
                altitude = _altitude;
                return true;
            }

            shouldSuppress = false;
            biome = Heightmap.Biome.None;
            altitude = 0f;
            return false;
        }

        internal void Set(int version, Vector3 position, bool shouldSuppress, Heightmap.Biome biome, float altitude)
        {
            _initialized = true;
            _version = version;
            _position = position;
            _shouldSuppress = shouldSuppress;
            _biome = biome;
            _altitude = altitude;
        }
    }

    internal sealed class SnowMaterialState : MonoBehaviour
    {
        private readonly List<MaterialSnapshot> _materials = new List<MaterialSnapshot>();
        private readonly HashSet<int> _materialIds = new HashSet<int>();
        private bool _lastSuppress;

        internal void Apply(bool suppress, bool restoreOriginalValues)
        {
            CaptureCurrentMaterials();
            if (_materials.Count == 0)
            {
                _lastSuppress = suppress;
                return;
            }

            if (!suppress && !restoreOriginalValues && !_lastSuppress)
            {
                return;
            }

            foreach (MaterialSnapshot snapshot in _materials)
            {
                Material? material = snapshot.Material;
                if (material == null)
                {
                    continue;
                }

                if (snapshot.HasAddSnow)
                {
                    material.SetFloat(NoSnowyBiomePlugin.AddSnowId, suppress ? 0f : snapshot.OriginalAddSnow);
                }

                if (suppress)
                {
                    if (snapshot.AddSnowKeywordEnabled)
                    {
                        material.DisableKeyword(NoSnowyBiomePlugin.AddSnowKeyword);
                    }
                }
                else if (restoreOriginalValues)
                {
                    if (snapshot.AddSnowKeywordEnabled)
                    {
                        material.EnableKeyword(NoSnowyBiomePlugin.AddSnowKeyword);
                    }
                    else
                    {
                        material.DisableKeyword(NoSnowyBiomePlugin.AddSnowKeyword);
                    }
                }
            }

            _lastSuppress = suppress;
        }

        private void CaptureCurrentMaterials()
        {
            CaptureMaterials(GetComponentsInChildren<MeshRenderer>(true));
            CaptureMaterials(GetComponentsInChildren<SkinnedMeshRenderer>(true));
        }

        private void CaptureMaterials<T>(T[] renderers) where T : Renderer
        {
            foreach (T renderer in renderers)
            {
                if (renderer == null)
                {
                    continue;
                }

                Material[] materials = renderer.materials;
                foreach (Material material in materials)
                {
                    if (material == null)
                    {
                        continue;
                    }

                    bool hasAddSnow = material.HasProperty(NoSnowyBiomePlugin.AddSnowId);
                    bool addSnowKeywordEnabled = material.IsKeywordEnabled(NoSnowyBiomePlugin.AddSnowKeyword);
                    if (!hasAddSnow && !addSnowKeywordEnabled)
                    {
                        continue;
                    }

                    int instanceId = material.GetInstanceID();
                    if (!_materialIds.Add(instanceId))
                    {
                        continue;
                    }

                    _materials.Add(
                        new MaterialSnapshot(
                            material,
                            hasAddSnow,
                            hasAddSnow ? material.GetFloat(NoSnowyBiomePlugin.AddSnowId) : 0f,
                            addSnowKeywordEnabled
                        )
                    );
                }
            }
        }

        private readonly struct MaterialSnapshot
        {
            internal MaterialSnapshot(
                Material material,
                bool hasAddSnow,
                float originalAddSnow,
                bool addSnowKeywordEnabled)
            {
                Material = material;
                HasAddSnow = hasAddSnow;
                OriginalAddSnow = originalAddSnow;
                AddSnowKeywordEnabled = addSnowKeywordEnabled;
            }

            internal Material Material { get; }
            internal bool HasAddSnow { get; }
            internal float OriginalAddSnow { get; }
            internal bool AddSnowKeywordEnabled { get; }
        }
    }

    internal static class ExpandWorldDataCompat
    {
        private const string BiomeManagerTypeName = "ExpandWorldData.BiomeManager";
        private const string ConfigurationTypeName = "ExpandWorldData.Configuration";
        private const string HelperTypeName = "ExpandWorldData.Helper";
        private const string BiomeYamlTypeName = "ExpandWorldData.BiomeYaml";
        private const string ServiceYamlTypeName = "Service.Yaml";

        private static bool _initialized;
        private static MethodInfo? _tryGetBiomeMethod;
        private static MethodInfo? _setMethod;
        private static MethodInfo? _deserializeMethod;
        private static MethodInfo? _readConfigsMethod;
        private static MethodInfo? _namesFromFileMethod;
        private static MethodInfo? _loadNamesMethod;
        private static MethodInfo? _isClientMethod;
        private static FieldInfo? _biomeDataValueField;
        private static FieldInfo? _filePathField;
        private static FieldInfo? _patternField;
        private static FieldInfo? _yamlBaseDirectoryField;
        private static PropertyInfo? _dataBiomeProperty;
        private static Type? _biomeYamlType;
        private static bool _setPatched;
        private static bool _readConfigsPatched;
        private static bool _namesFromFilePatched;

        internal static void Initialize(Harmony harmony)
        {
            if (!TryResolve())
            {
                return;
            }

            if (!_setPatched && _setMethod != null)
            {
                HarmonyMethod prefix = new HarmonyMethod(AccessTools.Method(typeof(ExpandWorldDataCompat), nameof(BiomeManagerSetPrefix)));
                HarmonyMethod postfix = new HarmonyMethod(AccessTools.Method(typeof(ExpandWorldDataCompat), nameof(BiomeManagerSetPostfix)));
                harmony.Patch(_setMethod, prefix: prefix, postfix: postfix);
                _setPatched = true;
            }

            if (!_readConfigsPatched && _readConfigsMethod != null)
            {
                HarmonyMethod prefix = new HarmonyMethod(AccessTools.Method(typeof(ExpandWorldDataCompat), nameof(BiomeManagerReadConfigsPrefix)));
                harmony.Patch(_readConfigsMethod, prefix: prefix);
                _readConfigsPatched = true;
            }

            if (!_namesFromFilePatched && _namesFromFileMethod != null)
            {
                HarmonyMethod prefix = new HarmonyMethod(AccessTools.Method(typeof(ExpandWorldDataCompat), nameof(BiomeManagerNamesFromFilePrefix)));
                harmony.Patch(_namesFromFileMethod, prefix: prefix);
                _namesFromFilePatched = true;
            }

            if (_setMethod == null ||
                _readConfigsMethod == null ||
                _namesFromFileMethod == null ||
                _loadNamesMethod == null ||
                _deserializeMethod == null ||
                _biomeYamlType == null ||
                _filePathField == null ||
                _patternField == null ||
                _yamlBaseDirectoryField == null ||
                _biomeDataValueField == null ||
                _dataBiomeProperty == null ||
                _isClientMethod == null)
            {
                NoSnowyBiomePlugin.Log.LogWarning("Expand World Data 1.68 compatibility API is incomplete. NoSnowyBiome YAML rules may not load.");
            }

            TryReloadFromCurrentBiomeData();
        }

        internal static bool TryGetBiome(string biomeName, out Heightmap.Biome biome)
        {
            biome = Heightmap.Biome.None;

            if (!TryResolve())
            {
                return false;
            }

            object[] args = { biomeName, Heightmap.Biome.None };
            bool success = (bool)_tryGetBiomeMethod!.Invoke(null, args);
            if (!success)
            {
                return false;
            }

            biome = (Heightmap.Biome)args[1];
            return true;
        }

        private static bool TryResolve()
        {
            if (_initialized)
            {
                return _tryGetBiomeMethod != null;
            }

            _initialized = true;

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type? biomeManagerType = assembly.GetType(BiomeManagerTypeName, throwOnError: false);
                Type? configurationType = assembly.GetType(ConfigurationTypeName, throwOnError: false);
                Type? helperType = assembly.GetType(HelperTypeName, throwOnError: false);
                Type? biomeYamlType = assembly.GetType(BiomeYamlTypeName, throwOnError: false);
                Type? yamlType = assembly.GetType(ServiceYamlTypeName, throwOnError: false);
                if (biomeManagerType == null)
                {
                    continue;
                }

                _tryGetBiomeMethod = AccessTools.Method(biomeManagerType, "TryGetBiome");
                _setMethod = AccessTools.Method(biomeManagerType, "Set", new[] { typeof(string) });
                _readConfigsMethod = AccessTools.Method(biomeManagerType, "ReadConfigs");
                _namesFromFileMethod = AccessTools.Method(biomeManagerType, "NamesFromFile");
                _loadNamesMethod = AccessTools.Method(biomeManagerType, "LoadNames");
                _filePathField = AccessTools.Field(biomeManagerType, "FilePath");
                _patternField = AccessTools.Field(biomeManagerType, "Pattern");
                _biomeDataValueField = configurationType != null ? AccessTools.Field(configurationType, "valueBiomeData") : null;
                _dataBiomeProperty = configurationType?.GetProperty("DataBiome", BindingFlags.Public | BindingFlags.Static);
                _isClientMethod = helperType != null ? AccessTools.Method(helperType, "IsClient") : null;
                _biomeYamlType = biomeYamlType;
                _deserializeMethod = yamlType != null ? AccessTools.Method(yamlType, "Deserialize", new[] { typeof(string), typeof(string) }) : null;
                _yamlBaseDirectoryField = yamlType != null ? AccessTools.Field(yamlType, "BaseDirectory") : null;
                if (_tryGetBiomeMethod != null)
                {
                    return true;
                }
            }

            return false;
        }

        private static void BiomeManagerSetPrefix(ref string? yaml, out string __state)
        {
            __state = yaml ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(yaml))
            {
                yaml = BiomeRuleYamlParser.StripCustomFields(yaml!);
            }
        }

        private static void BiomeManagerSetPostfix(string __state)
        {
            if (string.IsNullOrWhiteSpace(__state))
            {
                return;
            }

            ReloadRulesFromYaml(__state);
        }

        private static bool BiomeManagerReadConfigsPrefix()
        {
            if (!ShouldOverrideBiomeFileLoading())
            {
                return true;
            }

            string? filePath = _filePathField?.GetValue(null) as string;
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                return true;
            }

            string yaml = ReadBiomeYamlText();
            SetCurrentBiomeData(yaml);
            _setMethod!.Invoke(null, new object[] { yaml });
            return false;
        }

        private static bool BiomeManagerNamesFromFilePrefix()
        {
            if (!ShouldOverrideBiomeFileLoading() || _loadNamesMethod == null)
            {
                return true;
            }

            object data = ReadBiomeYamlData();
            _loadNamesMethod.Invoke(null, new[] { data });
            return false;
        }

        private static void TryReloadFromCurrentBiomeData()
        {
            string? yaml = GetCurrentBiomeData();
            if (!string.IsNullOrWhiteSpace(yaml))
            {
                ReloadRulesFromYaml(yaml!);
            }
        }

        private static string? GetCurrentBiomeData()
        {
            if (_biomeDataValueField == null)
            {
                return null;
            }

            object? syncedValue = _biomeDataValueField.GetValue(null);
            if (syncedValue == null)
            {
                return null;
            }

            PropertyInfo? valueProperty = syncedValue.GetType().GetProperty("Value");
            return valueProperty?.GetValue(syncedValue) as string;
        }

        private static void ReloadRulesFromYaml(string yaml)
        {
            List<BiomeSnowRuleYaml> entries;
            try
            {
                entries = BiomeRuleYamlParser.Parse(yaml);
            }
            catch (Exception ex)
            {
                NoSnowyBiomePlugin.Log.LogWarning($"Failed to parse expand_biomes.yaml noSnow rules: {ex.Message}");
                return;
            }

            Dictionary<Heightmap.Biome, NoSnowyBiomePlugin.BiomeRule> rules = new Dictionary<Heightmap.Biome, NoSnowyBiomePlugin.BiomeRule>();
            int enabledObjectRules = 0;

            foreach (BiomeSnowRuleYaml entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry.biome) || !entry.noSnow.HasValue)
                {
                    continue;
                }

                if (!TryGetBiome(entry.biome, out Heightmap.Biome biome))
                {
                    if (NoSnowyBiomePlugin.VerboseLogging.Value)
                    {
                        NoSnowyBiomePlugin.Log.LogWarning($"NoSnowyBiome could not resolve biome '{entry.biome}' from expand_biomes.yaml.");
                    }

                    continue;
                }

                float objectMinimumAltitude = entry.noSnowMinAltitude ?? NoSnowyBiomePlugin.DefaultMinimumAltitude;
                NoSnowyBiomePlugin.BiomeRule rule = new NoSnowyBiomePlugin.BiomeRule(
                    entry.noSnow,
                    objectMinimumAltitude
                );
                rules[biome] = rule;
                if (rule.HasObjectSnowRule && rule.SuppressObjectSnow)
                {
                    enabledObjectRules++;
                }
            }

            NoSnowyBiomePlugin.ReplaceYamlRules(rules);
            if (NoSnowyBiomePlugin.VerboseLogging.Value)
            {
                NoSnowyBiomePlugin.Log.LogInfo($"Loaded {enabledObjectRules} enabled noSnow rule(s) from expand_biomes.yaml.");
            }
        }

        private static bool ShouldOverrideBiomeFileLoading()
        {
            if (_setMethod == null ||
                _deserializeMethod == null ||
                _biomeYamlType == null ||
                _biomeDataValueField == null)
            {
                return false;
            }

            if (_dataBiomeProperty != null && _dataBiomeProperty.GetValue(null) is bool dataBiomeEnabled && !dataBiomeEnabled)
            {
                return false;
            }

            return !IsClient();
        }

        private static bool IsClient()
        {
            return _isClientMethod != null && _isClientMethod.Invoke(null, null) is bool isClient && isClient;
        }

        private static string ReadBiomeYamlText()
        {
            StringBuilder builder = new StringBuilder();
            foreach (string file in GetBiomeFiles())
            {
                string raw = File.ReadAllText(file);
                IList parsed = DeserializeBiomeYamlList(BiomeRuleYamlParser.StripCustomFields(raw), Path.GetFileNameWithoutExtension(file));
                if (parsed.Count == 0)
                {
                    continue;
                }

                if (builder.Length > 0)
                {
                    builder.Append('\n');
                }

                builder.Append(raw);
            }

            return builder.ToString();
        }

        private static object ReadBiomeYamlData()
        {
            IList combined = CreateBiomeYamlList();
            foreach (string file in GetBiomeFiles())
            {
                IList parsed = DeserializeBiomeYamlList(BiomeRuleYamlParser.StripCustomFields(File.ReadAllText(file)), Path.GetFileNameWithoutExtension(file));
                foreach (object? item in parsed)
                {
                    combined.Add(item);
                }
            }

            return combined;
        }

        private static IList CreateBiomeYamlList()
        {
            return (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(_biomeYamlType!))!;
        }

        private static IList DeserializeBiomeYamlList(string yaml, string fileName)
        {
            MethodInfo genericMethod = _deserializeMethod!.MakeGenericMethod(_biomeYamlType!);
            return genericMethod.Invoke(null, new object[] { yaml, fileName }) as IList ?? CreateBiomeYamlList();
        }

        private static IEnumerable<string> GetBiomeFiles()
        {
            string folder = _yamlBaseDirectoryField?.GetValue(null) as string ?? Path.Combine(Paths.ConfigPath, "expand_world");
            string pattern = _patternField?.GetValue(null) as string ?? "expand_biomes*.yaml";
            if (!Directory.Exists(folder))
            {
                return Array.Empty<string>();
            }

            string[] files = Directory.GetFiles(folder, pattern, SearchOption.AllDirectories);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            Array.Reverse(files);
            return files;
        }

        private static void SetCurrentBiomeData(string yaml)
        {
            if (_biomeDataValueField == null)
            {
                return;
            }

            object? syncedValue = _biomeDataValueField.GetValue(null);
            if (syncedValue == null)
            {
                return;
            }

            PropertyInfo? valueProperty = syncedValue.GetType().GetProperty("Value");
            valueProperty?.SetValue(syncedValue, yaml, null);
        }
    }

    internal sealed class BiomeSnowRuleYaml
    {
        public string biome = string.Empty;
        public bool? noSnow = null;
        public float? noSnowMinAltitude = null;
    }

    internal static class BiomeRuleYamlParser
    {
        private static readonly string[] CustomKeys =
        {
            "noSnow",
            "noSnowMinAltitude"
        };

        internal static string StripCustomFields(string yaml)
        {
            if (string.IsNullOrWhiteSpace(yaml))
            {
                return yaml;
            }

            string[] lines = NormalizeNewlines(yaml).Split('\n');
            StringBuilder builder = new StringBuilder(yaml.Length);
            foreach (string line in lines)
            {
                if (ShouldStripLine(line))
                {
                    continue;
                }

                builder.AppendLine(line);
            }

            return builder.ToString();
        }

        internal static List<BiomeSnowRuleYaml> Parse(string yaml)
        {
            List<BiomeSnowRuleYaml> entries = new List<BiomeSnowRuleYaml>();
            if (string.IsNullOrWhiteSpace(yaml))
            {
                return entries;
            }

            BiomeSnowRuleYaml? current = null;
            int itemIndent = -1;
            int fieldIndent = -1;

            foreach (string rawLine in NormalizeNewlines(yaml).Split('\n'))
            {
                string line = rawLine.TrimEnd();
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                string trimmedStart = line.TrimStart();
                if (trimmedStart.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                int indent = line.Length - trimmedStart.Length;
                if (trimmedStart.StartsWith("- ", StringComparison.Ordinal) && (itemIndent < 0 || indent == itemIndent))
                {
                    AddEntry(entries, current);
                    current = new BiomeSnowRuleYaml();
                    itemIndent = indent;
                    fieldIndent = -1;
                    ParseField(trimmedStart.Substring(2), current);
                    continue;
                }

                if (current == null || indent <= itemIndent || trimmedStart.StartsWith("- ", StringComparison.Ordinal))
                {
                    continue;
                }

                if (fieldIndent < 0)
                {
                    fieldIndent = indent;
                }

                if (indent != fieldIndent)
                {
                    continue;
                }

                ParseField(trimmedStart, current);
            }

            AddEntry(entries, current);
            return entries;
        }

        private static void AddEntry(List<BiomeSnowRuleYaml> entries, BiomeSnowRuleYaml? entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.biome))
            {
                return;
            }

            entries.Add(entry);
        }

        private static bool ShouldStripLine(string line)
        {
            string trimmed = line.TrimStart();
            foreach (string key in CustomKeys)
            {
                if (trimmed.StartsWith(key + ":", StringComparison.Ordinal) ||
                    trimmed.StartsWith("- " + key + ":", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private static void ParseField(string fieldLine, BiomeSnowRuleYaml entry)
        {
            int colonIndex = fieldLine.IndexOf(':');
            if (colonIndex <= 0)
            {
                return;
            }

            string key = fieldLine.Substring(0, colonIndex).Trim();
            string value = Unquote(RemoveInlineComment(fieldLine.Substring(colonIndex + 1).Trim()));
            switch (key)
            {
                case "biome":
                    entry.biome = value;
                    break;
                case "noSnow":
                    if (TryParseBool(value, out bool noSnow))
                    {
                        entry.noSnow = noSnow;
                    }
                    break;
                case "noSnowMinAltitude":
                    if (TryParseFloat(value, out float noSnowMinAltitude))
                    {
                        entry.noSnowMinAltitude = noSnowMinAltitude;
                    }
                    break;
            }
        }

        private static string NormalizeNewlines(string text)
        {
            return text.Replace("\r\n", "\n").Replace('\r', '\n');
        }

        private static string RemoveInlineComment(string value)
        {
            if (value.Length == 0)
            {
                return value;
            }

            char quote = '\0';
            for (int i = 0; i < value.Length; i++)
            {
                char current = value[i];
                if (quote == '\0')
                {
                    if (current == '\'' || current == '"')
                    {
                        quote = current;
                    }
                    else if (current == '#')
                    {
                        return value.Substring(0, i).TrimEnd();
                    }
                }
                else if (current == quote)
                {
                    quote = '\0';
                }
            }

            return value;
        }

        private static string Unquote(string value)
        {
            if (value.Length >= 2 &&
                ((value[0] == '"' && value[value.Length - 1] == '"') || (value[0] == '\'' && value[value.Length - 1] == '\'')))
            {
                return value.Substring(1, value.Length - 2);
            }

            return value;
        }

        private static bool TryParseBool(string value, out bool result)
        {
            switch (value.Trim().ToLowerInvariant())
            {
                case "true":
                case "yes":
                case "on":
                case "1":
                    result = true;
                    return true;
                case "false":
                case "no":
                case "off":
                case "0":
                    result = false;
                    return true;
                default:
                    result = false;
                    return false;
            }
        }

        private static bool TryParseFloat(string value, out float result)
        {
            return float.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out result);
        }
    }

    [HarmonyPatch]
    internal static class AttachSnowSuppressorPatches
    {
        [HarmonyPatch(typeof(StaticPhysics), "Awake")]
        [HarmonyPostfix]
        private static void StaticPhysicsAwake(StaticPhysics __instance)
        {
            NoSnowyBiomePlugin.TryAttach(__instance.gameObject);
        }

        [HarmonyPatch(typeof(Pickable), "Awake")]
        [HarmonyPostfix]
        private static void PickableAwake(Pickable __instance)
        {
            NoSnowyBiomePlugin.TryAttach(__instance.gameObject);
        }

        [HarmonyPatch(typeof(PickableItem), "Awake")]
        [HarmonyPostfix]
        private static void PickableItemAwake(PickableItem __instance)
        {
            NoSnowyBiomePlugin.TryAttach(__instance.gameObject);
        }

        [HarmonyPatch(typeof(Plant), "Awake")]
        [HarmonyPostfix]
        private static void PlantAwake(Plant __instance)
        {
            NoSnowyBiomePlugin.TryAttach(__instance.gameObject);
        }

        [HarmonyPatch(typeof(TreeBase), "Awake")]
        [HarmonyPostfix]
        private static void TreeBaseAwake(TreeBase __instance)
        {
            NoSnowyBiomePlugin.TryAttach(__instance.gameObject);
        }

        [HarmonyPatch(typeof(TreeLog), "Awake")]
        [HarmonyPostfix]
        private static void TreeLogAwake(TreeLog __instance)
        {
            NoSnowyBiomePlugin.TryAttach(__instance.gameObject);
        }

        [HarmonyPatch(typeof(Destructible), "Awake")]
        [HarmonyPostfix]
        private static void DestructibleAwake(Destructible __instance)
        {
            NoSnowyBiomePlugin.TryAttach(__instance.gameObject);
        }

        [HarmonyPatch(typeof(MineRock5), "Awake")]
        [HarmonyPostfix]
        private static void MineRock5Awake(MineRock5 __instance)
        {
            NoSnowyBiomePlugin.TryAttach(__instance.gameObject);
        }
    }
}
