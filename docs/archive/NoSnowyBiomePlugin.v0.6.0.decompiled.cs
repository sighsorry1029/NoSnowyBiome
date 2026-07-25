using System;
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

namespace NoSnowyBiome;

[BepInPlugin("blizz.NoSnowyBiome", "NoSnowyBiome", "0.6.0")]
[BepInDependency("expand_world_data", BepInDependency.DependencyFlags.SoftDependency)]
public sealed class NoSnowyBiomePlugin : BaseUnityPlugin
{
	internal readonly struct BiomeRule
	{
		internal bool HasObjectSnowRule { get; }

		internal bool SuppressObjectSnow { get; }

		internal float ObjectMinimumAltitude { get; }

		internal bool HasMinimapWhiteningRule { get; }

		internal bool SuppressMinimapWhitening { get; }

		internal float MinimapMinimumAltitude { get; }

		internal BiomeRule(bool? suppressObjectSnow, float objectMinimumAltitude, bool? suppressMinimapWhitening, float minimapMinimumAltitude)
		{
			HasObjectSnowRule = suppressObjectSnow.HasValue;
			SuppressObjectSnow = suppressObjectSnow == true;
			ObjectMinimumAltitude = objectMinimumAltitude;
			HasMinimapWhiteningRule = suppressMinimapWhitening.HasValue;
			SuppressMinimapWhitening = suppressMinimapWhitening == true;
			MinimapMinimumAltitude = minimapMinimumAltitude;
		}
	}

	internal const string ModName = "NoSnowyBiome";

	internal const string ModVersion = "0.6.0";

	internal const string Author = "blizz";

	internal const string ModGuid = "blizz.NoSnowyBiome";

	internal static readonly int AddSnowId = Shader.PropertyToID("_AddSnow");

	internal static readonly int AddRainId = Shader.PropertyToID("_AddRain");

	internal static readonly int WetId = Shader.PropertyToID("_Wet");

	internal const string AddSnowKeyword = "_ADDSNOW_ON";

	internal const string AddRainKeyword = "_ADDRAIN_ON";

	private const string ReplacementBundleFileName = "nosnowybiome_replacements";

	private static readonly HashSet<Biome> TargetBiomes = new HashSet<Biome>();

	private static readonly object TargetBiomeLock = new object();

	private static readonly object ShaderDumpLock = new object();

	private static readonly HashSet<string> DumpedShaderReports = new HashSet<string>();

	private static readonly Dictionary<string, string> ReplacementMaterialMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
	{
		["woodwall"] = "woodwall_nosnow",
		["woodwall_worn"] = "woodwall_worn_nosnow",
		["woodwall_lowlod"] = "woodwall_lowlod_nosnow",
		["woodwall_worn_lowlod"] = "woodwall_worn_lowlod_nosnow"
	};

	private static Dictionary<Biome, BiomeRule> _yamlRules = new Dictionary<Biome, BiomeRule>();

	private static readonly Dictionary<string, Material> _replacementMaterials = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);

	private static int _suppressionCacheVersion;

	private static bool _lastWetOverrideState;

	private static Biome _lastWetOverrideBiome = (Biome)0;

	private static AssetBundle? _replacementBundle;

	private static bool _replacementBundleLoadAttempted;

	private static bool _warnedAboutMissingReplacementBundle;

	private readonly Harmony _harmony = new Harmony("blizz.NoSnowyBiome");

	private static string _lastBiomeConfig = string.Empty;

	private static bool _warnedAboutEmptyBiomeList;

	internal static ManualLogSource Log = null;

	internal static ConfigEntry<bool> ModEnabled = null;

	internal static ConfigEntry<string> TargetBiomeNames = null;

	internal static ConfigEntry<float> MinimumAltitude = null;

	internal static ConfigEntry<bool> VerboseLogging = null;

	internal static ConfigEntry<bool> DumpPieceShaderInfo = null;

	internal static ConfigEntry<string> DumpPieceShaderFilter = null;

	private void Awake()
	{
		Log = base.Logger;
		ModEnabled = base.Config.Bind("General", "Enabled", defaultValue: true, "Enables conditional snow suppression for configured biomes.");
		TargetBiomeNames = base.Config.Bind("General", "BiomeNames", string.Empty, "Fallback comma separated biome names to suppress snow visuals in when no expand_biomes.yaml noSnow rule exists.");
		MinimumAltitude = base.Config.Bind("General", "MinimumAltitude", 50f, "Fallback altitude threshold. Also used when expand_biomes.yaml sets noSnow: true without noSnowMinAltitude.");
		VerboseLogging = base.Config.Bind("Debug", "VerboseLogging", defaultValue: false, "Logs biome resolution and suppression decisions.");
		DumpPieceShaderInfo = base.Config.Bind("Debug", "DumpPieceShaderInfo", defaultValue: false, "Logs runtime shader and material info for matching piece/location objects to help identify snow visuals.");
		DumpPieceShaderFilter = base.Config.Bind("Debug", "DumpPieceShaderFilter", "wood_floor", "Case-insensitive name filter used by DumpPieceShaderInfo. Leave empty to dump all matching objects.");
		ModEnabled.SettingChanged += delegate
		{
			InvalidateSuppressionCache();
			MinimapSnowHelper.RefreshMinimap();
		};
		TargetBiomeNames.SettingChanged += delegate
		{
			RebuildTargetBiomeCache(force: true);
		};
		MinimumAltitude.SettingChanged += delegate
		{
			ExpandWorldDataCompat.RefreshRulesFromCurrentBiomeData();
			InvalidateSuppressionCache();
			MinimapSnowHelper.RefreshMinimap();
		};
		DumpPieceShaderInfo.SettingChanged += delegate
		{
			ClearShaderDumpCache();
		};
		DumpPieceShaderFilter.SettingChanged += delegate
		{
			ClearShaderDumpCache();
		};
		RebuildTargetBiomeCache(force: true);
		LoadReplacementMaterials();
		ExpandWorldDataCompat.Initialize(_harmony);
		_harmony.PatchAll(Assembly.GetExecutingAssembly());
		Log.LogInfo("NoSnowyBiome 0.6.0 loaded");
	}

	private void OnDestroy()
	{
		_replacementBundle?.Unload(unloadAllLoadedObjects: false);
		_replacementBundle = null;
		_harmony.UnpatchSelf();
	}

	internal static void TryAttach(GameObject gameObject)
	{
		if (!(gameObject == null) && !(gameObject.GetComponent<ConditionalSnowSuppressor>() != null) && (!(gameObject.GetComponentInChildren<MeshRenderer>(includeInactive: true) == null) || !(gameObject.GetComponentInChildren<SkinnedMeshRenderer>(includeInactive: true) == null)))
		{
			gameObject.AddComponent<ConditionalSnowSuppressor>();
		}
	}

	internal static bool TryApplySnowSuppression(GameObject gameObject, out Biome biome, out float altitude)
	{
		biome = (Biome)0;
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
		ApplyReplacementMaterials(gameObject);
		if (ShouldUseMaterialSnowState(gameObject))
		{
			ApplyMaterialSnowState(gameObject, suppress: true, restoreOriginalValues: false);
		}
		return true;
	}

	internal static void RefreshObjectSnowSuppression(GameObject gameObject, string reason = "refresh")
	{
		if (!(gameObject == null))
		{
			HashSet<int> seen = new HashSet<int>();
			RefreshObjectSnowSuppression(gameObject, seen, reason);
			RefreshObjectSnowSuppression(((Component)(object)gameObject.GetComponentInParent<Piece>())?.gameObject, seen, reason);
			RefreshObjectSnowSuppression(((Component)(object)gameObject.GetComponentInParent<WearNTear>())?.gameObject, seen, reason);
			RefreshObjectSnowSuppression(((Component)(object)gameObject.GetComponentInParent<ItemDrop>())?.gameObject, seen, reason);
			RefreshObjectSnowSuppression(((Component)(object)gameObject.GetComponentInParent<ZNetView>())?.gameObject, seen, reason);
		}
	}

	internal static void RefreshPlacementGhostSnow(GameObject gameObject)
	{
		if (!(gameObject == null))
		{
			ApplyRendererPropertyBlocks(gameObject);
			ApplyReplacementMaterials(gameObject);
			ApplyMaterialSnowState(gameObject, suppress: true, restoreOriginalValues: false);
		}
	}

	internal static void ApplyPieceWetOverride()
	{
		//IL_0041: Unknown result type (might be due to invalid IL or missing references)
		//IL_0042: Unknown result type (might be due to invalid IL or missing references)
		//IL_0055: Unknown result type (might be due to invalid IL or missing references)
		//IL_0033: Unknown result type (might be due to invalid IL or missing references)
		//IL_0034: Unknown result type (might be due to invalid IL or missing references)
		//IL_0082: Unknown result type (might be due to invalid IL or missing references)
		//IL_0083: Unknown result type (might be due to invalid IL or missing references)
		Biome biome;
		float altitude;
		bool flag = TryShouldSuppressPieceWet(out biome, out altitude);
		if (flag)
		{
			Shader.SetGlobalFloat(WetId, 0f);
		}
		if (!VerboseLogging.Value || (flag == _lastWetOverrideState && (!flag || biome == _lastWetOverrideBiome)))
		{
			_lastWetOverrideState = flag;
			_lastWetOverrideBiome = biome;
			return;
		}
		if (flag)
		{
			Log.LogInfo($"Applied global _Wet override in biome {biome} at altitude {altitude:0.##}");
		}
		else
		{
			Log.LogInfo("Released global _Wet override");
		}
		_lastWetOverrideState = flag;
		_lastWetOverrideBiome = biome;
	}

	internal static bool ShouldSuppressSnow(Vector3 position, out Biome biome, out float altitude)
	{
		biome = (Biome)0;
		altitude = GetAltitude(position);
		if (!ModEnabled.Value)
		{
			return false;
		}
		if (!TryGetBiome(position, out biome))
		{
			return false;
		}
		if (_yamlRules.TryGetValue(biome, out var value) && value.HasObjectSnowRule)
		{
			if (value.SuppressObjectSnow)
			{
				return altitude >= value.ObjectMinimumAltitude;
			}
			return false;
		}
		if (TargetBiomes.Count == 0 && !string.IsNullOrWhiteSpace(TargetBiomeNames.Value))
		{
			RebuildTargetBiomeCache(force: true);
		}
		if (TargetBiomes.Count == 0)
		{
			if (!_warnedAboutEmptyBiomeList && !string.IsNullOrWhiteSpace(TargetBiomeNames.Value))
			{
				_warnedAboutEmptyBiomeList = true;
				Log.LogWarning("NoSnowyBiome could not resolve any configured biome names yet.");
			}
			return false;
		}
		if (!TargetBiomes.Contains(biome))
		{
			return false;
		}
		return altitude >= MinimumAltitude.Value;
	}

	internal static float GetAltitude(Vector3 position)
	{
		return position.y - GetWaterLevel();
	}

	internal static float GetWaterLevel()
	{
		if (!((UnityEngine.Object)(object)ZoneSystem.instance != null))
		{
			return 30f;
		}
		return ZoneSystem.instance.m_waterLevel;
	}

	internal static bool ShouldSuppressSnowForGameObject(GameObject gameObject, out Biome biome, out float altitude)
	{
		biome = (Biome)0;
		altitude = 0f;
		if (gameObject == null)
		{
			return false;
		}
		if (WorldGenerator.instance == null)
		{
			return ShouldSuppressSnow(gameObject.transform.position, out biome, out altitude);
		}
		SnowSuppressionState snowSuppressionState = gameObject.GetComponent<SnowSuppressionState>() ?? gameObject.AddComponent<SnowSuppressionState>();
		Vector3 position = gameObject.transform.position;
		if (snowSuppressionState.TryGet(_suppressionCacheVersion, position, out var shouldSuppress, out biome, out altitude))
		{
			return shouldSuppress;
		}
		shouldSuppress = ShouldSuppressSnow(position, out biome, out altitude);
		snowSuppressionState.Set(_suppressionCacheVersion, position, shouldSuppress, biome, altitude);
		return shouldSuppress;
	}

	private static void ApplyRendererPropertyBlocks(GameObject gameObject)
	{
		ApplyRendererPropertyBlocks(gameObject.GetComponentsInChildren<MeshRenderer>(includeInactive: true));
		ApplyRendererPropertyBlocks(gameObject.GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive: true));
	}

	private static void ApplyReplacementMaterials(GameObject gameObject)
	{
		if (!(gameObject == null) && EnsureReplacementMaterialsLoaded())
		{
			ApplyReplacementMaterials(gameObject.GetComponentsInChildren<MeshRenderer>(includeInactive: true));
			ApplyReplacementMaterials(gameObject.GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive: true));
		}
	}

	private static void RefreshObjectSnowSuppression(GameObject? gameObject, ISet<int> seen, string reason)
	{
		if (!(gameObject == null) && seen.Add(gameObject.GetInstanceID()))
		{
			TryAttach(gameObject);
			TryApplySnowSuppression(gameObject, out var _, out var _);
			MaybeDumpPieceShaderInfo(gameObject, reason);
		}
	}

	private static void ApplyRendererPropertyBlocks<T>(T[] renderers) where T : Renderer
	{
		foreach (T val in renderers)
		{
			if (!(val == null))
			{
				MaterialPropertyBlock materialPropertyBlock = new MaterialPropertyBlock();
				val.GetPropertyBlock(materialPropertyBlock);
				materialPropertyBlock.SetFloat(AddSnowId, 0f);
				materialPropertyBlock.SetFloat(AddRainId, 0f);
				materialPropertyBlock.SetFloat(WetId, 0f);
				val.SetPropertyBlock(materialPropertyBlock);
			}
		}
	}

	private static void ApplyReplacementMaterials<T>(T[] renderers) where T : Renderer
	{
		foreach (T val in renderers)
		{
			if (val == null)
			{
				continue;
			}
			Material[] sharedMaterials = val.sharedMaterials;
			bool flag = false;
			for (int j = 0; j < sharedMaterials.Length; j++)
			{
				Material material = sharedMaterials[j];
				if (TryGetReplacementMaterial(material, out Material replacement) && !(replacement == null) && !(material == replacement))
				{
					sharedMaterials[j] = replacement;
					flag = true;
				}
			}
			if (flag)
			{
				val.sharedMaterials = sharedMaterials;
			}
		}
	}

	private static bool EnsureReplacementMaterialsLoaded()
	{
		if (_replacementMaterials.Count > 0)
		{
			return true;
		}
		if (!_replacementBundleLoadAttempted)
		{
			LoadReplacementMaterials();
		}
		return _replacementMaterials.Count > 0;
	}

	private static void LoadReplacementMaterials()
	{
		if (_replacementBundleLoadAttempted)
		{
			return;
		}
		_replacementBundleLoadAttempted = true;
		string text = FindReplacementBundlePath();
		if (string.IsNullOrEmpty(text))
		{
			WarnAboutMissingReplacementBundle();
			return;
		}
		AssetBundle assetBundle = AssetBundle.LoadFromFile(text);
		if (assetBundle == null)
		{
			Log.LogWarning("Failed to load replacement bundle at '" + text + "'. Piece no-snow material swap will be unavailable.");
			return;
		}
		_replacementBundle = assetBundle;
		Material[] array = assetBundle.LoadAllAssets<Material>();
		foreach (Material material in array)
		{
			if (!(material == null))
			{
				_replacementMaterials[NormalizeMaterialName(material.name)] = material;
			}
		}
		if (_replacementMaterials.Count == 0)
		{
			Log.LogWarning("Replacement bundle '" + text + "' loaded but no materials were found.");
		}
		else if (VerboseLogging.Value)
		{
			Log.LogInfo($"Loaded {_replacementMaterials.Count} replacement materials from '{text}'.");
		}
	}

	private static string? FindReplacementBundlePath()
	{
		string path = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
		string[] array = new string[8]
		{
			Path.Combine(path, "nosnowybiome_replacements"),
			Path.Combine(path, "RuntimeAssets", "nosnowybiome_replacements"),
			Path.Combine(Paths.PluginPath, "nosnowybiome_replacements"),
			Path.Combine(Paths.PluginPath, "RuntimeAssets", "nosnowybiome_replacements"),
			Path.Combine(Paths.PluginPath, "NoSnowyBiome", "nosnowybiome_replacements"),
			Path.Combine(Paths.PluginPath, "NoSnowyBiome", "RuntimeAssets", "nosnowybiome_replacements"),
			Path.Combine(Paths.PluginPath, "blizz.NoSnowyBiome", "nosnowybiome_replacements"),
			Path.Combine(Paths.PluginPath, "blizz.NoSnowyBiome", "RuntimeAssets", "nosnowybiome_replacements")
		};
		foreach (string text in array)
		{
			if (!string.IsNullOrWhiteSpace(text) && File.Exists(text))
			{
				return text;
			}
		}
		return null;
	}

	private static void WarnAboutMissingReplacementBundle()
	{
		if (!_warnedAboutMissingReplacementBundle)
		{
			_warnedAboutMissingReplacementBundle = true;
			Log.LogWarning("Replacement bundle 'nosnowybiome_replacements' was not found. Wood piece no-snow replacements will be unavailable.");
		}
	}

	private static bool TryGetReplacementMaterial(Material? material, out Material? replacement)
	{
		replacement = null;
		if (material == null)
		{
			return false;
		}
		string text = NormalizeMaterialName(material.name);
		if (text.EndsWith("_nosnow", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		if (!ReplacementMaterialMap.TryGetValue(text, out string value))
		{
			return false;
		}
		return _replacementMaterials.TryGetValue(value, out replacement);
	}

	private static string NormalizeMaterialName(string? materialName)
	{
		string text = materialName?.Trim() ?? string.Empty;
		if (text.Length == 0)
		{
			return string.Empty;
		}
		int num = text.IndexOf(" (", StringComparison.Ordinal);
		if (num < 0)
		{
			return text;
		}
		return text.Substring(0, num);
	}

	private static bool ShouldUseMaterialSnowState(GameObject gameObject)
	{
		if (gameObject == null)
		{
			return false;
		}
		if (Player.IsPlacementGhost(gameObject))
		{
			return true;
		}
		if (!((UnityEngine.Object)(object)gameObject.GetComponent<Piece>() != null) && !((UnityEngine.Object)(object)gameObject.GetComponent<WearNTear>() != null) && !((UnityEngine.Object)(object)gameObject.GetComponentInParent<Piece>() != null) && !((UnityEngine.Object)(object)gameObject.GetComponentInParent<WearNTear>() != null) && !((UnityEngine.Object)(object)gameObject.GetComponentInChildren<Piece>(includeInactive: true) != null))
		{
			return (UnityEngine.Object)(object)gameObject.GetComponentInChildren<WearNTear>(includeInactive: true) != null;
		}
		return true;
	}

	private static bool TryShouldSuppressPieceWet(out Biome biome, out float altitude)
	{
		biome = (Biome)0;
		altitude = 0f;
		Player localPlayer = Player.m_localPlayer;
		if ((UnityEngine.Object)(object)localPlayer == null)
		{
			return false;
		}
		return ShouldSuppressSnow(((Component)(object)localPlayer).transform.position, out biome, out altitude);
	}

	private static void ApplyMaterialSnowState(GameObject gameObject, bool suppress, bool restoreOriginalValues)
	{
		(gameObject.GetComponent<SnowMaterialState>() ?? gameObject.AddComponent<SnowMaterialState>()).Apply(suppress, restoreOriginalValues);
	}

	private static void ClearShaderDumpCache()
	{
		lock (ShaderDumpLock)
		{
			DumpedShaderReports.Clear();
		}
	}

	private static void MaybeDumpPieceShaderInfo(GameObject gameObject, string reason)
	{
		//IL_01cf: Unknown result type (might be due to invalid IL or missing references)
		if (!DumpPieceShaderInfo.Value || gameObject == null)
		{
			return;
		}
		string text = DumpPieceShaderFilter.Value?.Trim() ?? string.Empty;
		string transformPath = GetTransformPath(gameObject.transform);
		if (text.Length > 0 && transformPath.IndexOf(text, StringComparison.OrdinalIgnoreCase) < 0)
		{
			return;
		}
		Biome biome;
		float altitude;
		bool flag = ShouldSuppressSnowForGameObject(gameObject, out biome, out altitude);
		Renderer[] componentsInChildren = gameObject.GetComponentsInChildren<Renderer>(includeInactive: true);
		if (componentsInChildren.Length == 0)
		{
			return;
		}
		Renderer[] array = componentsInChildren;
		foreach (Renderer renderer in array)
		{
			if (renderer == null)
			{
				continue;
			}
			string transformPath2 = GetTransformPath(renderer.transform);
			Material[] materials = renderer.materials;
			for (int j = 0; j < materials.Length; j++)
			{
				Material material = materials[j];
				if (material == null || material.shader == null)
				{
					continue;
				}
				string text2 = BuildInterestingPropertyFlags(material);
				string text3 = BuildInterestingKeywordFlags(material);
				string text4 = BuildInterestingPropertyValues(material);
				string item = string.Join("|", reason, transformPath2, j, material.shader.name, text2, text3, text4);
				lock (ShaderDumpLock)
				{
					if (!DumpedShaderReports.Add(item))
					{
						continue;
					}
				}
				Log.LogInfo($"PieceShaderDump reason={reason} root='{transformPath}' renderer='{transformPath2}' materialIndex={j} " + $"material='{material.name}' shader='{material.shader.name}' shouldSuppress={flag} biome={biome} altitude={altitude:0.##} " + "props=[" + text2 + "] keywords=[" + text3 + "] values=[" + text4 + "]");
			}
		}
	}

	private static string BuildInterestingPropertyFlags(Material material)
	{
		string[] array = new string[11]
		{
			"_AddSnow", "_AddRain", "_Color", "_EmissionColor", "_ValueNoise", "_ValueNoiseVertex", "_TriplanarLocalPos", "_TriplanarScale", "_MoveableObject", "_TakingAshlandsDamage",
			"_Wet"
		};
		StringBuilder stringBuilder = new StringBuilder();
		for (int i = 0; i < array.Length; i++)
		{
			if (i > 0)
			{
				stringBuilder.Append(", ");
			}
			string value = array[i];
			stringBuilder.Append(value);
			stringBuilder.Append('=');
			stringBuilder.Append(material.HasProperty(value) ? "yes" : "no");
		}
		return stringBuilder.ToString();
	}

	private static string BuildInterestingKeywordFlags(Material material)
	{
		string[] array = new string[2] { "_ADDSNOW_ON", "_ADDRAIN_ON" };
		StringBuilder stringBuilder = new StringBuilder();
		for (int i = 0; i < array.Length; i++)
		{
			if (i > 0)
			{
				stringBuilder.Append(", ");
			}
			string text = array[i];
			stringBuilder.Append(text);
			stringBuilder.Append('=');
			stringBuilder.Append(material.IsKeywordEnabled(text) ? "on" : "off");
		}
		return stringBuilder.ToString();
	}

	private static string BuildInterestingPropertyValues(Material material)
	{
		StringBuilder stringBuilder = new StringBuilder();
		AppendFloatValue(stringBuilder, material, "_AddSnow");
		AppendFloatValue(stringBuilder, material, "_AddRain");
		AppendFloatValue(stringBuilder, material, "_Wet");
		AppendColorValue(stringBuilder, material, "_Color");
		AppendColorValue(stringBuilder, material, "_EmissionColor");
		return stringBuilder.ToString();
	}

	private static void AppendFloatValue(StringBuilder builder, Material material, string propertyName)
	{
		if (builder.Length > 0)
		{
			builder.Append(", ");
		}
		builder.Append(propertyName);
		builder.Append('=');
		builder.Append(material.HasProperty(propertyName) ? material.GetFloat(propertyName).ToString("0.###", CultureInfo.InvariantCulture) : "n/a");
	}

	private static void AppendColorValue(StringBuilder builder, Material material, string propertyName)
	{
		if (builder.Length > 0)
		{
			builder.Append(", ");
		}
		builder.Append(propertyName);
		builder.Append('=');
		if (!material.HasProperty(propertyName))
		{
			builder.Append("n/a");
			return;
		}
		Color color = material.GetColor(propertyName);
		builder.Append('(');
		builder.Append(color.r.ToString("0.###", CultureInfo.InvariantCulture));
		builder.Append(',');
		builder.Append(color.g.ToString("0.###", CultureInfo.InvariantCulture));
		builder.Append(',');
		builder.Append(color.b.ToString("0.###", CultureInfo.InvariantCulture));
		builder.Append(',');
		builder.Append(color.a.ToString("0.###", CultureInfo.InvariantCulture));
		builder.Append(')');
	}

	private static string GetTransformPath(Transform transform)
	{
		if (transform == null)
		{
			return string.Empty;
		}
		StringBuilder stringBuilder = new StringBuilder(transform.name);
		Transform parent = transform.parent;
		while (parent != null)
		{
			stringBuilder.Insert(0, '/');
			stringBuilder.Insert(0, parent.name);
			parent = parent.parent;
		}
		return stringBuilder.ToString();
	}

	private static bool TryGetBiome(Vector3 position, out Biome biome)
	{
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		//IL_0019: Expected I4, but got Unknown
		biome = (Biome)0;
		if (WorldGenerator.instance == null)
		{
			return false;
		}
		biome = (Biome)(int)WorldGenerator.instance.GetBiome(position);
		return (int)biome != 0;
	}

	private static void RebuildTargetBiomeCache(bool force = false)
	{
		//IL_00b0: Unknown result type (might be due to invalid IL or missing references)
		string text = TargetBiomeNames.Value ?? string.Empty;
		if (!force && string.Equals(text, _lastBiomeConfig, StringComparison.Ordinal))
		{
			return;
		}
		lock (TargetBiomeLock)
		{
			if (!force && string.Equals(text, _lastBiomeConfig, StringComparison.Ordinal))
			{
				return;
			}
			TargetBiomes.Clear();
			_lastBiomeConfig = text;
			_warnedAboutEmptyBiomeList = false;
			string[] array = text.Split(new char[4] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
			for (int i = 0; i < array.Length; i++)
			{
				string text2 = array[i].Trim();
				if (text2.Length != 0)
				{
					if (ExpandWorldDataCompat.TryGetBiome(text2, out var biome) || Enum.TryParse<Biome>(text2, ignoreCase: true, out biome))
					{
						TargetBiomes.Add(biome);
					}
					else
					{
						Log.LogWarning("Unknown biome name in config: '" + text2 + "'");
					}
				}
			}
			if (VerboseLogging.Value)
			{
				Log.LogInfo($"Resolved {TargetBiomes.Count} target biome(s) from config.");
			}
			InvalidateSuppressionCache();
		}
	}

	internal static void ReplaceYamlRules(Dictionary<Biome, BiomeRule> rules)
	{
		_yamlRules = rules ?? new Dictionary<Biome, BiomeRule>();
		InvalidateSuppressionCache();
		MinimapSnowHelper.RefreshMinimap();
	}

	internal static bool HasMinimapWhiteningRules()
	{
		if (!ModEnabled.Value)
		{
			return false;
		}
		foreach (BiomeRule value in _yamlRules.Values)
		{
			if (value.HasMinimapWhiteningRule && value.SuppressMinimapWhitening)
			{
				return true;
			}
		}
		return false;
	}

	internal static bool TryGetMinimapWhiteningHeight(Biome biome, out float maxWorldHeight)
	{
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		maxWorldHeight = 0f;
		if (!ModEnabled.Value)
		{
			return false;
		}
		if (!_yamlRules.TryGetValue(biome, out var value) || !value.HasMinimapWhiteningRule || !value.SuppressMinimapWhitening)
		{
			return false;
		}
		maxWorldHeight = GetWaterLevel() + value.MinimapMinimumAltitude;
		return true;
	}

	internal static void InvalidateSuppressionCache()
	{
		_suppressionCacheVersion++;
	}
}
