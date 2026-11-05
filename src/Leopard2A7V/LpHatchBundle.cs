using System;
using System.IO;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering;

namespace Leopard2A7V;

public static class LpHatchBundle
{
	private const string Tag = "[L2A7V][hatch-bundle]";

	private const string BundleFile = "l2a7v_hatches.bundle";

	private const string BundleName = "l2a7v_hatches";

	private static AssetBundle _bundle;

	private static bool _tried;

	internal static bool Ready
	{
		get
		{
			Load();
			return _bundle != null;
		}
	}

	private static void Load()
	{
		if (_tried)
		{
			return;
		}
		_tried = true;
		string path = Path.Combine(LpXrayArmor.ModFolder, "l2a7v_hatches.bundle");
		if (!File.Exists(path))
		{
			MelonLogger.Msg("[L2A7V][hatch-bundle] no l2a7v_hatches.bundle; lids stay part of the turret mesh");
			return;
		}
		try
		{
			_bundle = AssetBundle.LoadFromFile(path);
		}
		catch (Exception ex)
		{
			MelonLogger.Warning("[L2A7V][hatch-bundle] could not load l2a7v_hatches.bundle: " + ex.Message);
			return;
		}
		if (_bundle == null)
		{
			MelonLogger.Warning("[L2A7V][hatch-bundle] l2a7v_hatches.bundle did not load as a bundle");
		}
		else
		{
			MelonLogger.Msg("[L2A7V][hatch-bundle] loaded l2a7v_hatches.bundle (" + _bundle.GetAllAssetNames().Length + " asset(s))");
		}
	}

	internal static int Spawn(Transform turretPivot, Vector3 anchor, Material[] artMaterials)
	{
		if (!Ready || turretPivot == null)
		{
			return 0;
		}
		int num = 0;
		string[] allAssetNames = _bundle.GetAllAssetNames();
		foreach (string text in allAssetNames)
		{
			if (!text.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}
			string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(text);
			if (!fileNameWithoutExtension.StartsWith("hatch_", StringComparison.OrdinalIgnoreCase) || turretPivot.Find(fileNameWithoutExtension) != null)
			{
				continue;
			}
			GameObject gameObject = null;
			try
			{
				gameObject = _bundle.LoadAsset<GameObject>(text);
			}
			catch (Exception)
			{
			}
			if (gameObject == null)
			{
				continue;
			}
			GameObject gameObject2 = UnityEngine.Object.Instantiate(gameObject, turretPivot, worldPositionStays: false);
			gameObject2.name = fileNameWithoutExtension;
			gameObject2.transform.localPosition = anchor;
			gameObject2.transform.localRotation = Quaternion.identity;
			gameObject2.transform.localScale = Vector3.one;
			int num2 = 0;
			Renderer[] componentsInChildren = gameObject2.GetComponentsInChildren<Renderer>(includeInactive: true);
			foreach (Renderer renderer in componentsInChildren)
			{
				if (renderer == null)
				{
					continue;
				}
				num2++;
				if (artMaterials != null && artMaterials.Length != 0)
				{
					Material[] array = new Material[Mathf.Max(1, renderer.sharedMaterials.Length)];
					for (int k = 0; k < array.Length; k++)
					{
						array[k] = artMaterials[Mathf.Min(k, artMaterials.Length - 1)];
					}
					renderer.sharedMaterials = array;
				}
				renderer.shadowCastingMode = ShadowCastingMode.Off;
			}
			if (num2 == 0)
			{
				MelonLogger.Warning("[L2A7V][hatch-bundle] lid '" + fileNameWithoutExtension + "' has no renderer");
			}
			else if (num >= 0)
			{
				MelonLogger.Msg("[L2A7V][hatch-bundle] lid '" + fileNameWithoutExtension + "' spawned (" + num2 + " renderer(s))");
			}
			num++;
		}
		MelonLogger.Msg("[L2A7V][hatch-bundle] placed " + num + " lid(s) on the turret");
		return num;
	}
}
