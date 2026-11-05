using System.Diagnostics;
using System.Reflection;
using GHPC;
using GHPC.Mission;
using HarmonyLib;
using MelonLoader;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace Leopard2A7V;

public static class LpTemplate
{
	private static readonly FieldInfo UniqueNameField = AccessTools.Field(typeof(Unit), "_uniqueName");

	private static readonly FieldInfo FriendlyNameField = AccessTools.Field(typeof(Unit), "_friendlyName");

	private static GameObject _host;

	private static bool _gaveUp;

	private static bool _loggedWaitingMaterial;

	private static bool _loggedWaitingPrefab;

	private static GameObject _donorPrefab;

	private static AssetReference _donorReference;

	public static GameObject Current { get; private set; }

	public static bool Ensure(UnitSpawner spawner)
	{
		if (!LpConfig.UseTemplate)
		{
			return false;
		}
		if (Current != null)
		{
			return true;
		}
		if (_gaveUp || spawner == null)
		{
			return false;
		}
		if (!ObjModel.Loaded)
		{
			if (ObjModel.Failed)
			{
				MelonLogger.Error("[L2A7V] model parse failed: " + ObjModel.Error);
				_gaveUp = true;
			}
			return false;
		}
		if (LpVisual.TemplateMaterial == null)
		{
			if (!_loggedWaitingMaterial)
			{
				_loggedWaitingMaterial = true;
				MelonLogger.Msg("[L2A7V] no shader template yet; this spawn converts in place, later ones will use the converted clone");
			}
			return false;
		}
		GameObject gameObject = LoadDonorPrefab(spawner);
		if (gameObject == null)
		{
			return false;
		}
		_host = new GameObject("L2A7V_TemplateHost");
		Object.DontDestroyOnLoad(_host);
		GameObject gameObject2 = Object.Instantiate(gameObject, _host.transform);
		gameObject2.name = "LEO2A7V [template]";
		if (UniqueNameField != null)
		{
			UniqueNameField.SetValue(gameObject2.GetComponent<Unit>(), "LEO2A7V");
		}
		if (FriendlyNameField != null)
		{
			FriendlyNameField.SetValue(gameObject2.GetComponent<Unit>(), "Leopard 2A7V");
		}
		Stopwatch stopwatch = Stopwatch.StartNew();
		LpVisual.Assembly assembly = LpVisual.Convert(gameObject2);
		stopwatch.Stop();
		if (assembly == null)
		{
			MelonLogger.Error("[L2A7V] template conversion failed, spawning stays on the fallback path");
			DestroyHost();
			_gaveUp = true;
			return false;
		}
		gameObject2.SetActive(value: false);
		Current = gameObject2;
		MelonLogger.Msg($"[L2A7V] unit template ready in {stopwatch.ElapsedMilliseconds} ms: own object graph, " + $"hull {assembly.Hull}, turret {assembly.Turret}, gun {assembly.Gun}");
		return true;
	}

	private static GameObject LoadDonorPrefab(UnitSpawner spawner)
	{
		if (_donorPrefab != null)
		{
			return _donorPrefab;
		}
		string text = "LEO2A3";
		AssetReference prefabByUniqueName = spawner.GetPrefabByUniqueName(text);
		if (prefabByUniqueName == null)
		{
			MelonLogger.Error("[L2A7V] the donor unit '" + text + "' has no prefab reference in this spawner");
			_gaveUp = true;
			return null;
		}
		GameObject gameObject = prefabByUniqueName.Asset as GameObject;
		if (gameObject != null)
		{
			_donorPrefab = gameObject;
			return gameObject;
		}
		if (_donorReference != prefabByUniqueName)
		{
			_donorReference = prefabByUniqueName;
			if (!_loggedWaitingPrefab)
			{
				_loggedWaitingPrefab = true;
				MelonLogger.Msg("[L2A7V] the donor prefab is not resident; asked for it and this spawn continues on the in-place path instead of blocking the load");
			}
			AsyncOperationHandle<GameObject> asyncOperationHandle = prefabByUniqueName.LoadAssetAsync<GameObject>();
			asyncOperationHandle.Completed += delegate(AsyncOperationHandle<GameObject> handle)
			{
				if (handle.Result == null)
				{
					MelonLogger.Error("[L2A7V] the donor prefab could not be loaded, so no template can be built");
					_gaveUp = true;
				}
				else
				{
					_donorPrefab = handle.Result;
					MelonLogger.Msg("[L2A7V] donor prefab resident; the next spawn builds from the template");
				}
			};
		}
		return null;
	}

	private static void DestroyHost()
	{
		if (!(_host == null))
		{
			Object.Destroy(_host);
			_host = null;
		}
	}
}
