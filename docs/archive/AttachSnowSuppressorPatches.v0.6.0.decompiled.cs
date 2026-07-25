using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace NoSnowyBiome;

[HarmonyPatch]
internal static class AttachSnowSuppressorPatches
{
	private static readonly FieldInfo PlacementGhostField = AccessTools.Field(typeof(Player), "m_placementGhost");

	[HarmonyPatch(typeof(MaterialVariation), "UpdateMaterial")]
	[HarmonyPostfix]
	private static void MaterialVariationUpdateMaterial(MaterialVariation __instance)
	{
		NoSnowyBiomePlugin.RefreshObjectSnowSuppression(((Component)(object)__instance).gameObject, "MaterialVariation.UpdateMaterial");
	}

	[HarmonyPatch(typeof(RandomMaterialValues), "CheckMaterial")]
	[HarmonyPostfix]
	private static void RandomMaterialValuesCheckMaterial(RandomMaterialValues __instance)
	{
		NoSnowyBiomePlugin.RefreshObjectSnowSuppression(((Component)(object)__instance).gameObject, "RandomMaterialValues.CheckMaterial");
	}

	[HarmonyPatch(typeof(ItemDrop), "MakePiece")]
	[HarmonyPostfix]
	private static void ItemDropMakePiece(ItemDrop __instance)
	{
		NoSnowyBiomePlugin.RefreshObjectSnowSuppression(((Component)(object)__instance).gameObject, "ItemDrop.MakePiece");
		if (__instance.m_pieceEnableObj != null)
		{
			NoSnowyBiomePlugin.RefreshObjectSnowSuppression(__instance.m_pieceEnableObj, "ItemDrop.MakePieceEnabledObj");
		}
	}

	[HarmonyPatch(typeof(LocationProxy), "SpawnLocation")]
	[HarmonyPostfix]
	private static void LocationProxySpawnLocation(bool __result, GameObject ___m_instance)
	{
		if (__result && ___m_instance != null)
		{
			NoSnowyBiomePlugin.RefreshObjectSnowSuppression(___m_instance, "LocationProxy.SpawnLocation");
		}
	}

	[HarmonyPatch(typeof(ZNetView), "Awake")]
	[HarmonyPostfix]
	private static void ZNetViewAwake(ZNetView __instance)
	{
		NoSnowyBiomePlugin.TryAttach(((Component)(object)__instance).gameObject);
	}

	[HarmonyPatch(typeof(ZoneSystem), "SpawnProxyLocation")]
	[HarmonyPostfix]
	private static void SpawnProxyLocation(GameObject __result)
	{
		NoSnowyBiomePlugin.TryAttach(__result);
	}

	[HarmonyPatch(typeof(StaticPhysics), "Awake")]
	[HarmonyPostfix]
	private static void StaticPhysicsAwake(StaticPhysics __instance)
	{
		NoSnowyBiomePlugin.TryAttach(((Component)(object)__instance).gameObject);
	}

	[HarmonyPatch(typeof(Pickable), "Awake")]
	[HarmonyPostfix]
	private static void PickableAwake(Pickable __instance)
	{
		NoSnowyBiomePlugin.TryAttach(((Component)(object)__instance).gameObject);
	}

	[HarmonyPatch(typeof(PickableItem), "Awake")]
	[HarmonyPostfix]
	private static void PickableItemAwake(PickableItem __instance)
	{
		NoSnowyBiomePlugin.TryAttach(((Component)(object)__instance).gameObject);
	}

	[HarmonyPatch(typeof(Plant), "Awake")]
	[HarmonyPostfix]
	private static void PlantAwake(Plant __instance)
	{
		NoSnowyBiomePlugin.TryAttach(((Component)(object)__instance).gameObject);
	}

	[HarmonyPatch(typeof(TreeBase), "Awake")]
	[HarmonyPostfix]
	private static void TreeBaseAwake(TreeBase __instance)
	{
		NoSnowyBiomePlugin.TryAttach(((Component)(object)__instance).gameObject);
	}

	[HarmonyPatch(typeof(TreeLog), "Awake")]
	[HarmonyPostfix]
	private static void TreeLogAwake(TreeLog __instance)
	{
		NoSnowyBiomePlugin.TryAttach(((Component)(object)__instance).gameObject);
	}

	[HarmonyPatch(typeof(MineRock), "Start")]
	[HarmonyPostfix]
	private static void MineRockStart(MineRock __instance)
	{
		NoSnowyBiomePlugin.TryAttach(((Component)(object)__instance).gameObject);
	}

	[HarmonyPatch(typeof(MineRock5), "Awake")]
	[HarmonyPostfix]
	private static void MineRock5Awake(MineRock5 __instance)
	{
		NoSnowyBiomePlugin.TryAttach(((Component)(object)__instance).gameObject);
	}

	[HarmonyPatch(typeof(Destructible), "Awake")]
	[HarmonyPostfix]
	private static void DestructibleAwake(Destructible __instance)
	{
		NoSnowyBiomePlugin.TryAttach(((Component)(object)__instance).gameObject);
		NoSnowyBiomePlugin.RefreshObjectSnowSuppression(((Component)(object)__instance).gameObject, "Destructible.Awake");
	}

	[HarmonyPatch(typeof(Piece), "Awake")]
	[HarmonyPostfix]
	private static void PieceAwake(Piece __instance)
	{
		NoSnowyBiomePlugin.TryAttach(((Component)(object)__instance).gameObject);
		NoSnowyBiomePlugin.RefreshObjectSnowSuppression(((Component)(object)__instance).gameObject, "Piece.Awake");
	}

	[HarmonyPatch(typeof(Piece), "OnPlaced")]
	[HarmonyPostfix]
	private static void PieceOnPlaced(Piece __instance)
	{
		NoSnowyBiomePlugin.RefreshObjectSnowSuppression(((Component)(object)__instance).gameObject, "Piece.OnPlaced");
	}

	[HarmonyPatch(typeof(WearNTear), "Awake")]
	[HarmonyPostfix]
	private static void WearNTearAwake(WearNTear __instance)
	{
		NoSnowyBiomePlugin.TryAttach(((Component)(object)__instance).gameObject);
		NoSnowyBiomePlugin.RefreshObjectSnowSuppression(((Component)(object)__instance).gameObject, "WearNTear.Awake");
	}

	[HarmonyPatch(typeof(WearNTear), "Start")]
	[HarmonyPostfix]
	private static void WearNTearStart(WearNTear __instance)
	{
		NoSnowyBiomePlugin.RefreshObjectSnowSuppression(((Component)(object)__instance).gameObject, "WearNTear.Start");
	}

	[HarmonyPatch(typeof(WearNTear), "OnPlaced")]
	[HarmonyPostfix]
	private static void WearNTearOnPlaced(WearNTear __instance)
	{
		NoSnowyBiomePlugin.RefreshObjectSnowSuppression(((Component)(object)__instance).gameObject, "WearNTear.OnPlaced");
	}

	[HarmonyPatch(typeof(EnvMan), "SetEnv")]
	[HarmonyPostfix]
	private static void EnvManSetEnv()
	{
		NoSnowyBiomePlugin.ApplyPieceWetOverride();
	}

	[HarmonyPatch(typeof(EnvMan), "Update")]
	[HarmonyPostfix]
	private static void EnvManUpdate()
	{
		NoSnowyBiomePlugin.ApplyPieceWetOverride();
	}

	[HarmonyPatch(typeof(Player), "SetupPlacementGhost")]
	[HarmonyPostfix]
	private static void SetupPlacementGhost(Player __instance)
	{
		RefreshPlacementGhost(__instance);
	}

	[HarmonyPatch(typeof(Minimap), "GenerateWorldMap")]
	[HarmonyPostfix]
	private static void GenerateWorldMap(Minimap __instance)
	{
		MinimapSnowHelper.ApplyToCurrentMap(__instance);
	}

	[HarmonyPatch(typeof(Minimap), "TryLoadMinimapTextureData")]
	[HarmonyPostfix]
	private static void TryLoadMinimapTextureData(Minimap __instance, bool __result)
	{
		if (__result)
		{
			MinimapSnowHelper.ApplyToCurrentMap(__instance);
		}
	}

	private static void RefreshPlacementGhost(Player player)
	{
		if (!((Object)(object)player == null) && !(PlacementGhostField == null))
		{
			GameObject gameObject = PlacementGhostField.GetValue(player) as GameObject;
			if (gameObject != null)
			{
				NoSnowyBiomePlugin.RefreshPlacementGhostSnow(gameObject);
			}
		}
	}
}
