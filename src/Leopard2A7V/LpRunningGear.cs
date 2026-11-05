using System;
using System.Collections.Generic;
using System.Text;
using MelonLoader;
using UnityEngine;

namespace Leopard2A7V;

public static class LpRunningGear
{
	private sealed class Part
	{
		public string Name;

		public Bounds Box;

		public bool HasBox;

		public void Include(Bounds other)
		{
			if (!HasBox)
			{
				Box = other;
				HasBox = true;
				return;
			}
			Vector3 vector = Vector3.Min(Box.min, other.min);
			Vector3 vector2 = Vector3.Max(Box.max, other.max);
			Box.center = (vector + vector2) * 0.5f;
			Box.size = vector2 - vector;
		}
	}

	private sealed class Pools
	{
		public List<Transform> RoadLeft;

		public List<Transform> RoadRight;

		public List<Transform> IdlerLeft;

		public List<Transform> IdlerRight;

		public List<Transform> SprocketLeft;

		public List<Transform> SprocketRight;

		public List<Transform> ArmLeft;

		public List<Transform> ArmRight;
	}

	private const string Tag = "[L2A7V][gear]";

	private const float MaxBindDistance = 0.6f;

	internal static bool Ensure(Transform unitRoot)
	{
		if (LpConfig.UseDonorRunningGear)
		{
			return true;
		}
		if (unitRoot == null)
		{
			return true;
		}
		Transform transform = FindDeep(unitRoot, "L2A7V_HullPivot");
		if (transform == null)
		{
			return false;
		}
		if (FindDeep(transform, "L2A7V_wheel_l_01") != null)
		{
			return true;
		}
		List<Transform> list = FindBones(unitRoot);
		if (list == null || list.Count == 0)
		{
			return false;
		}
		Transform transform2 = FindDeep(transform, "L2A7V_RunningGearStatic");
		if (transform2 != null)
		{
			UnityEngine.Object.Destroy(transform2.gameObject);
		}
		MelonLogger.Msg("[L2A7V][gear] the donor's wheel bones have arrived; binding the imported gear to them");
		Attach(transform, unitRoot);
		return true;
	}

	private static Transform FindDeep(Transform root, string name)
	{
		if (root == null)
		{
			return null;
		}
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

	public static void Attach(Transform hullPivot, Transform unitRoot)
	{
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		Dictionary<string, Part> dictionary = CollectParts();
		List<Transform> list = FindBones(unitRoot);
		if (dictionary == null || dictionary.Count == 0)
		{
			MelonLogger.Warning("[L2A7V][gear] the model has no running gear objects");
			return;
		}
		if (list == null || list.Count == 0)
		{
			MelonLogger.Error("[L2A7V][gear] the donor's wheel bones are gone, so the imported gear stays still");
			BuildStatic(hullPivot, null);
			AttachTracks(hullPivot, unitRoot, CountTrackLoops());
			return;
		}
		Pools pools = new Pools
		{
			RoadLeft = Take(list, "wheel_l_", "top"),
			RoadRight = Take(list, "wheel_r_", "top"),
			IdlerLeft = Take(list, "idler_l", null),
			IdlerRight = Take(list, "idler_r", null),
			SprocketLeft = Take(list, "sprocket_l", null),
			SprocketRight = Take(list, "sprocket_r", null),
			ArmLeft = Take(list, "arm_l", null),
			ArmRight = Take(list, "arm_r", null)
		};
		MelonLogger.Msg("[L2A7V][gear]" + $" donor bones: {pools.RoadLeft.Count}+{pools.RoadRight.Count} road wheels, " + $"{pools.IdlerLeft.Count}+{pools.IdlerRight.Count} idlers, " + $"{pools.SprocketLeft.Count}+{pools.SprocketRight.Count} sprockets, " + $"{pools.ArmLeft.Count}+{pools.ArmRight.Count} arms");
		int num = 0;
		num += BindArms(hullPivot, dictionary, "suspension_l_", pools.ArmLeft, hashSet);
		num += BindArms(hullPivot, dictionary, "suspension_r_", pools.ArmRight, hashSet);
		foreach (Part part in dictionary.Values)
		{
			if (IsArm(part.Name))
			{
				continue;
			}
			Vector3 vector = hullPivot.TransformPoint(part.Box.center);
			Transform transform = Nearest(PoolFor(part.Name, pools), vector);
			if (transform == null)
			{
				hashSet.Add(part.Name);
				continue;
			}
			float num2 = Vector3.Distance(vector, transform.position);
			if (num2 > 0.6f)
			{
				MelonLogger.Warning("[L2A7V][gear]" + $" {part.Name} is {num2:0.00} m from {transform.name}, left still");
				hashSet.Add(part.Name);
				continue;
			}
			Transform transform2 = LpVisual.MakePivot(transform, "L2A7V_" + part.Name, Vector3.zero);
			transform2.rotation = hullPivot.rotation;
			MeshRenderer meshRenderer = LpVisual.AttachMesh(transform2, "L2A7V_" + part.Name, LpAssets.BuildWhere((ObjGeometry p) => p.Group == LpGroup.RunningGear && string.Equals(p.ObjectName, part.Name, StringComparison.OrdinalIgnoreCase), "L2A7V_" + part.Name, part.Box.center));
			if (meshRenderer != null)
			{
				meshRenderer.receiveShadows = false;
			}
			num++;
			MelonLogger.Msg("[L2A7V][gear]" + $" {part.Name} -> {PathOf(transform)} offset {num2 * 1000f:0} mm");
		}
		BuildStatic(hullPivot, hashSet);
		MelonLogger.Msg("[L2A7V][gear]" + $" {num} part(s) ride a bone, {hashSet.Count} object(s) have none");
		AttachTracks(hullPivot, unitRoot, CountTrackLoops());
	}

	private static int BindArms(Transform hullPivot, Dictionary<string, Part> parts, string prefix, List<Transform> bones, HashSet<string> stillNames)
	{
		Dictionary<int, Transform> dictionary = new Dictionary<int, Transform>();
		foreach (Transform bone in bones)
		{
			if (!(bone == null))
			{
				int num = FirstNumber(bone.name);
				if (num > 0 && !dictionary.ContainsKey(num))
				{
					dictionary[num] = bone;
				}
			}
		}
		int num2 = 0;
		foreach (Part value2 in parts.Values)
		{
			if (value2.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
			{
				if (!dictionary.TryGetValue(FirstNumber(value2.Name), out var value))
				{
					MelonLogger.Warning("[L2A7V][gear] " + value2.Name + " has no donor arm bone numbered with it, so it stays on the hull");
					stillNames.Add(value2.Name);
				}
				else
				{
					BindRigid(hullPivot, value2, value);
					num2++;
				}
			}
		}
		return num2;
	}

	private static void BindRigid(Transform hullPivot, Part part, Transform bone)
	{
		string text = "L2A7V_" + part.Name;
		Transform transform = LpVisual.MakePivot(bone, text, Vector3.zero);
		transform.position = hullPivot.TransformPoint(part.Box.center);
		transform.rotation = hullPivot.rotation;
		LpVisual.AttachMesh(transform, text, LpAssets.BuildWhere((ObjGeometry p) => p.Group == LpGroup.RunningGear && string.Equals(p.ObjectName, part.Name, StringComparison.OrdinalIgnoreCase), text, part.Box.center));
		float num = Vector3.Distance(transform.position, bone.position);
		MelonLogger.Msg("[L2A7V][gear] " + part.Name + " -> " + PathOf(bone) + ", swings with it from its drawn place " + $"{num * 1000f:0} mm off that bone's pivot");
	}

	private static int FirstNumber(string name)
	{
		int num = 0;
		bool flag = false;
		foreach (char c in name)
		{
			if (c < '0' || c > '9')
			{
				if (flag)
				{
					break;
				}
			}
			else
			{
				num = num * 10 + (c - 48);
				flag = true;
			}
		}
		if (!flag)
		{
			return 0;
		}
		return num;
	}

	private static bool IsArm(string objectName)
	{
		return objectName.StartsWith("suspension_", StringComparison.OrdinalIgnoreCase);
	}

	private static int CountTrackLoops()
	{
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (ObjGeometry geometry in ObjModel.Geometries)
		{
			if (geometry.Group == LpGroup.RunningGear && IsTrackLoop(geometry.ObjectName))
			{
				hashSet.Add(geometry.ObjectName);
			}
		}
		return hashSet.Count;
	}

	private static bool IsTrackLoop(string objectName)
	{
		return objectName?.StartsWith("track_", StringComparison.OrdinalIgnoreCase) ?? false;
	}

	private static List<Transform> PoolFor(string objectName, Pools pools)
	{
		string text = objectName.ToLowerInvariant();
		if (!text.StartsWith("wheel_", StringComparison.Ordinal) || text.Contains("top"))
		{
			return null;
		}
		bool flag = text.Contains("_l");
		if (text.Contains("drive"))
		{
			if (!flag)
			{
				return pools.SprocketRight;
			}
			return pools.SprocketLeft;
		}
		if (text.Contains("front"))
		{
			if (!flag)
			{
				return pools.IdlerRight;
			}
			return pools.IdlerLeft;
		}
		if (!flag)
		{
			return pools.RoadRight;
		}
		return pools.RoadLeft;
	}

	private static Transform Nearest(List<Transform> pool, Vector3 home)
	{
		if (pool == null || pool.Count == 0)
		{
			return null;
		}
		Transform transform = null;
		float num = float.MaxValue;
		for (int num2 = pool.Count - 1; num2 >= 0; num2--)
		{
			Transform transform2 = pool[num2];
			if (transform2 == null)
			{
				pool.RemoveAt(num2);
			}
			else
			{
				float num3 = Vector3.Distance(home, transform2.position);
				if (!(num3 >= num))
				{
					num = num3;
					transform = transform2;
				}
			}
		}
		if (transform != null)
		{
			pool.Remove(transform);
		}
		return transform;
	}

	private static void BuildStatic(Transform hullPivot, HashSet<string> stillNames)
	{
		LpVisual.AttachMesh(hullPivot, "L2A7V_RunningGearStatic", LpAssets.BuildWhere((ObjGeometry p) => p.Group == LpGroup.RunningGear && !IsTrackLoop(p.ObjectName) && (stillNames == null || stillNames.Contains(p.ObjectName)), "L2A7V_RunningGearStatic", Vector3.zero));
	}

	private static void AttachTracks(Transform hullPivot, Transform unitRoot, int loops)
	{
		if (!LpConfig.UseDonorTrackArt)
		{
			LpTracks.Attach(hullPivot, unitRoot);
		}
		MelonLogger.Msg("[L2A7V][gear]" + $" {loops} track loop(s) " + (LpConfig.UseDonorTrackArt ? "left to the donor's animated art" : ("rebuilt to scroll, " + LpTracks.Status)));
	}

	private static Dictionary<string, Part> CollectParts()
	{
		if (ObjModel.Geometries.Count == 0)
		{
			return null;
		}
		Dictionary<string, Part> dictionary = new Dictionary<string, Part>(StringComparer.OrdinalIgnoreCase);
		foreach (ObjGeometry geometry in ObjModel.Geometries)
		{
			if (geometry.Group == LpGroup.RunningGear && (!LpConfig.UseDonorTrackArt || !IsTrackLoop(geometry.ObjectName)))
			{
				if (!dictionary.TryGetValue(geometry.ObjectName, out var value))
				{
					value = new Part
					{
						Name = geometry.ObjectName
					};
					dictionary[geometry.ObjectName] = value;
				}
				value.Include(geometry.Bounds);
			}
		}
		return dictionary;
	}

	private static List<Transform> Take(List<Transform> bones, string prefix, string exclude)
	{
		List<Transform> list = new List<Transform>();
		foreach (Transform bone in bones)
		{
			if (!(bone == null))
			{
				string text = bone.name.ToLowerInvariant();
				if (text.StartsWith(prefix, StringComparison.Ordinal) && (exclude == null || !text.Contains(exclude)))
				{
					list.Add(bone);
				}
			}
		}
		return list;
	}

	private static List<Transform> FindBones(Transform unitRoot)
	{
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "wheel", "idler", "sprocket", "arm" };
		List<Transform> list = null;
		SkinnedMeshRenderer[] componentsInChildren = unitRoot.GetComponentsInChildren<SkinnedMeshRenderer>(includeInactive: true);
		foreach (SkinnedMeshRenderer skinnedMeshRenderer in componentsInChildren)
		{
			if (LpVisual.IsImportedArt(skinnedMeshRenderer.transform))
			{
				continue;
			}
			Transform[] bones = skinnedMeshRenderer.bones;
			if (bones == null)
			{
				continue;
			}
			List<Transform> list2 = new List<Transform>();
			Transform[] array = bones;
			foreach (Transform transform in array)
			{
				if (transform == null)
				{
					continue;
				}
				string text = transform.name.ToLowerInvariant();
				foreach (string item in hashSet)
				{
					if (text.StartsWith(item, StringComparison.Ordinal))
					{
						list2.Add(transform);
						break;
					}
				}
			}
			if (list2.Count >= 4)
			{
				if (PathOf(skinnedMeshRenderer.transform).IndexOf("running gear", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					MelonLogger.Msg("[L2A7V][gear]" + $" wheel bones from {PathOf(skinnedMeshRenderer.transform)} ({list2.Count})");
					return list2;
				}
				if (list == null || list2.Count > list.Count)
				{
					list = list2;
				}
			}
		}
		if (list != null)
		{
			MelonLogger.Warning("[L2A7V][gear]" + $" no running gear skin, using {list.Count} wheel bones from another mesh");
		}
		return list;
	}

	private static string PathOf(Transform node)
	{
		StringBuilder stringBuilder = new StringBuilder(node.name);
		Transform parent = node.parent;
		int num = 0;
		while (parent != null && num++ < 12)
		{
			stringBuilder.Insert(0, parent.name + "/");
			parent = parent.parent;
		}
		return stringBuilder.ToString();
	}
}
