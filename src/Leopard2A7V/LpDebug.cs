using System.Collections.Generic;
using System.Text;
using GHPC;
using MelonLoader;
using UnityEngine;

namespace Leopard2A7V;

public class LpDebug : MonoBehaviour
{
	private static LpDebug _instance;

	public static void Install()
	{
		if (!(_instance != null))
		{
			GameObject obj = new GameObject("[Leopard2A7V-Debug]");
			_instance = obj.AddComponent<LpDebug>();
			Object.DontDestroyOnLoad(obj);
		}
	}

	private void Update()
	{
		if (Input.GetKeyDown(KeyCode.F9))
		{
			ApplyToLiveUnits();
		}
	}

	private static void ApplyToLiveUnits()
	{
		if (!ObjModel.Loaded)
		{
			MelonLogger.Warning("[L2A7V] model not parsed yet" + (ObjModel.Failed ? (": " + ObjModel.Error) : ""));
			return;
		}
		List<Unit> list = FindLiveUnits();
		if (list.Count == 0)
		{
			MelonLogger.Msg("[L2A7V] no live units in the scene");
			return;
		}
		StringBuilder stringBuilder = new StringBuilder("[L2A7V] F9 on ");
		foreach (Unit item in list)
		{
			stringBuilder.Append(item.name).Append(' ');
		}
		MelonLogger.Msg(stringBuilder.ToString());
		int num = 0;
		int num2 = 0;
		foreach (Unit item2 in list)
		{
			GameObject gameObject = item2.gameObject;
			if (!(gameObject.GetComponent<LpConvertBehaviour>() != null))
			{
				if (FindDeep(gameObject.transform, "leo2_rig") == null)
				{
					num2++;
				}
				else if (!(FindDeep(gameObject.transform, "Leopard2A7V_Visual") != null))
				{
					LpArmorLog.Apply(gameObject);
					gameObject.AddComponent<LpConvertBehaviour>();
					num++;
				}
			}
		}
		MelonLogger.Msg($"[L2A7V] queued the Leopard 2A7V look on {num} unit(s), " + $"{num2} were not Leopards");
	}

	private static List<Unit> FindLiveUnits()
	{
		List<Unit> list = new List<Unit>();
		Unit[] array = Resources.FindObjectsOfTypeAll<Unit>();
		foreach (Unit unit in array)
		{
			if (!(unit == null) && unit.isActiveAndEnabled && unit.gameObject.scene.isLoaded)
			{
				list.Add(unit);
			}
		}
		return list;
	}

	private static Transform FindDeep(Transform root, string name)
	{
		if (root.name == name)
		{
			return root;
		}
		foreach (Transform item in root)
		{
			Transform transform = FindDeep(item, name);
			if (transform != null)
			{
				return transform;
			}
		}
		return null;
	}
}
