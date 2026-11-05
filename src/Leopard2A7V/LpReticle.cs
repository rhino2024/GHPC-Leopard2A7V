using System;
using System.Collections.Generic;
using GHPC.Equipment.Optics;
using MelonLoader;
using Reticle;
using TMPro;
using UnityEngine;

namespace Leopard2A7V;

public static class LpReticle
{
	private const string DonorPlateName = "EMES-15";

	internal const string PlateName = "LEO2A7V";

	private const string CloneName = "L2A7V_GunnerReticle";

	private const string RoofGunPlateName = "LEO2A7V_AA";

	private const float DonorPlateFov = 10f;

	private const float MinorLine = 0.098f;

	private const float ScaleLine = 0.08f;

	private const float LabelSize = 9.5f;

	private const float TitleSize = 12f;

	private const float WtStroke = 0.10800001f;

	internal const ReticleTree.Light.Type Lit = ReticleTree.Light.Type.NightIllumination;

	private static TMP_FontAsset _font;

	private static bool _fontMissing;

	private static ReticleSO _plate;

	private static ReticleSO _roofGunPlate;

	private static float _roofGunPixelScale = 1f;

	internal static ReticleSO DayPlate => _plate;

	public static void Apply(Transform unitRoot)
	{
		if (!LpConfig.ReplaceDayReticle || unitRoot == null)
		{
			return;
		}
		UsableOptic[] componentsInChildren = unitRoot.GetComponentsInChildren<UsableOptic>(includeInactive: true);
		foreach (UsableOptic usableOptic in componentsInChildren)
		{
			ReticleMesh reticleMesh = usableOptic.reticleMesh;
			if (reticleMesh == null || reticleMesh.reticleSO == null)
			{
				continue;
			}
			bool flag = reticleMesh.reticleSO.name.StartsWith("LEO2A7V", StringComparison.Ordinal);
			if (reticleMesh.reticleSO.name != "EMES-15" && !flag)
			{
				continue;
			}
			if (_plate == null && !BuildPlate(reticleMesh.reticleSO))
			{
				break;
			}
			Transform transform = usableOptic.transform.Find("L2A7V_GunnerReticle");
			if (transform != null)
			{
				ReticleMesh component = transform.GetComponent<ReticleMesh>();
				if (component != null)
				{
					usableOptic.reticleMesh = component;
				}
				break;
			}
			reticleMesh.gameObject.SetActive(value: false);
			GameObject gameObject = UnityEngine.Object.Instantiate(reticleMesh.gameObject, usableOptic.transform, worldPositionStays: false);
			gameObject.name = "L2A7V_GunnerReticle";
			ReticleMesh component2 = gameObject.GetComponent<ReticleMesh>();
			if (component2 == null)
			{
				MelonLogger.Warning("[L2A7V] the reticle copy lost its ReticleMesh; the donor plate stays");
				UnityEngine.Object.Destroy(gameObject);
				reticleMesh.gameObject.SetActive(value: true);
				continue;
			}
			component2.reticleSO = _plate;
			SkinnedMeshRenderer component3 = gameObject.GetComponent<SkinnedMeshRenderer>();
			if (component3 != null)
			{
				component3.sharedMesh = null;
			}
			gameObject.SetActive(value: true);
			component2.Load();
			usableOptic.reticleMesh = component2;
			component2.GetLight(ReticleTree.Light.Type.NightIllumination, out var value);
			component2.SetLight(ReticleTree.Light.Type.NightIllumination, (float.IsNaN(value) || value <= 0f) ? 0f : 1f);
			LpReticleLight.Attach(component2);
			MelonLogger.Msg("[L2A7V] gunner day reticle replaced with the 2A7V plate on " + Describe(usableOptic.transform, unitRoot));
			break;
		}
	}

	private static bool BuildPlate(ReticleSO donor)
	{
		FindFont();
		ReticleTree.FocalPlane focalPlane = new ReticleTree.FocalPlane(ReticleTree.FocalPlane.Type.First)
		{
			elements = new List<ReticleTree.TransformElement>()
		};
		focalPlane.elements.Add(WtSight());
		if (LpSightData.Moving.Length != 0)
		{
			focalPlane.elements.Add(WtBallistic());
		}
		ReticleSO reticleSO = ScriptableObject.CreateInstance<ReticleSO>();
		reticleSO.name = "LEO2A7V";
		reticleSO.hideFlags = HideFlags.DontUnloadUnusedAsset;
		reticleSO.planes = new List<ReticleTree.FocalPlane> { focalPlane };
		reticleSO.lights = ((donor != null && donor.lights != null) ? new List<ReticleTree.Light>(donor.lights) : new List<ReticleTree.Light>());
		if (!reticleSO.lights.Exists((ReticleTree.Light light) => light.type == ReticleTree.Light.Type.NightIllumination))
		{
			reticleSO.lights.Add(new ReticleTree.Light
			{
				type = ReticleTree.Light.Type.NightIllumination,
				color = new RGB(4f, 0.335f)
			});
		}
		_plate = reticleSO;
		return true;
	}

	public static ReticleSO BuildRoofGunPlate(ReticleSO donor)
	{
		if (_roofGunPlate != null)
		{
			return _roofGunPlate;
		}
		FindFont();
		_roofGunPixelScale = Mathf.Max(1f, 2f);
		ReticleTree.FocalPlane focalPlane = new ReticleTree.FocalPlane(ReticleTree.FocalPlane.Type.First)
		{
			elements = new List<ReticleTree.TransformElement>()
		};
		focalPlane.elements.Add(RoofGunScales());
		focalPlane.elements.Add(RoofGunAimMark());
		ReticleSO reticleSO = ScriptableObject.CreateInstance<ReticleSO>();
		reticleSO.name = "LEO2A7V_AA";
		reticleSO.hideFlags = HideFlags.DontUnloadUnusedAsset;
		reticleSO.planes = new List<ReticleTree.FocalPlane> { focalPlane };
		reticleSO.lights = ((donor != null && donor.lights != null) ? new List<ReticleTree.Light>(donor.lights) : new List<ReticleTree.Light>());
		if (!reticleSO.lights.Exists((ReticleTree.Light light) => light.type == ReticleTree.Light.Type.NightIllumination))
		{
			reticleSO.lights.Add(new ReticleTree.Light
			{
				type = ReticleTree.Light.Type.NightIllumination,
				color = new RGB(4f, 0.335f)
			});
		}
		_roofGunPlate = reticleSO;
		return reticleSO;
	}

	private static ReticleTree.Angular RoofGunScales()
	{
		ReticleTree.Angular angular = new ReticleTree.Angular(Vector2.zero, null, ReticleTree.GroupBase.Alignment.Boresight);
		angular.name = "Scales";
		angular.elements.Add(AALine(new Vector2(21f, 0f), 0f, 38f, 0.08f));
		angular.elements.Add(AALine(new Vector2(-21f, 0f), 0f, 38f, 0.08f));
		angular.elements.Add(AALine(new Vector2(0f, 21f), 90f, 38f, 0.08f));
		angular.elements.Add(AALine(new Vector2(0f, -21f), 90f, 38f, 0.08f));
		for (int i = 5; i <= 40; i += 5)
		{
			float length = ((i % 10 == 0) ? 2.4f : 1.2f);
			angular.elements.Add(AALine(new Vector2(i, 0f), 90f, length, 0.08f));
			angular.elements.Add(AALine(new Vector2(-i, 0f), 90f, length, 0.08f));
			angular.elements.Add(AALine(new Vector2(0f, -i), 0f, length, 0.08f));
			if (i % 20 == 0)
			{
				AAMark(angular.elements, new Vector2(i, -3.6f), i.ToString());
				AAMark(angular.elements, new Vector2(-i, -3.6f), i.ToString());
			}
		}
		int[] array = new int[4] { 400, 600, 800, 1200 };
		for (int j = 0; j < array.Length; j++)
		{
			float num = 1250f / (float)array[j];
			float y = 0f - (10f + (float)j * 7.5f);
			angular.elements.Add(AALine(new Vector2(0f - num, y), 90f, 2.4f, 0.08f));
			angular.elements.Add(AALine(new Vector2(num, y), 90f, 2.4f, 0.08f));
			AAMark(angular.elements, new Vector2(num + 3.4f, y), ((float)array[j] / 100f).ToString("0"));
		}
		AddMark(angular.elements, new Vector2(-46f, 40f), "MG3", TextAlignmentOptions.Left, LabelSizeFor(12f));
		return angular;
	}

	private static ReticleTree.Angular RoofGunAimMark()
	{
		return new ReticleTree.Angular(Vector2.zero, null, ReticleTree.GroupBase.Alignment.VerticalRange)
		{
			name = "Aim",
			elements = 
			{
				(ReticleTree.TransformElement)AACircle(Vector2.zero, 3.2f, 0.098f, 40),
				(ReticleTree.TransformElement)AALine(new Vector2(0f, 4.6f), 90f, 1.4f, 0.098f)
			}
		};
	}

	private static ReticleTree.Line AALine(Vector2 position, float degrees, float length, float thickness)
	{
		return Line(position, degrees, length, thickness * _roofGunPixelScale);
	}

	private static ReticleTree.Circle AACircle(Vector2 position, float radius, float thickness, int segments)
	{
		return Circle(position, radius, thickness * _roofGunPixelScale, segments);
	}

	private static void AAMark(List<ReticleTree.TransformElement> elements, Vector2 position, string label)
	{
		AddMark(elements, position, label, TextAlignmentOptions.Center, LabelSizeFor(9.5f));
	}

	private static float LabelSizeFor(float points)
	{
		return points * _roofGunPixelScale;
	}

	private static ReticleTree.Angular WtSight()
	{
		ReticleTree.Angular obj = new ReticleTree.Angular(Vector2.zero, null, ReticleTree.GroupBase.Alignment.Boresight)
		{
			name = "WtSight"
		};
		AddSegments(obj.elements, LpSightData.Fixed);
		MelonLogger.Msg($"[L2A7V] reticle plate: {LpSightData.Fixed.Length / 4} fixed and " + $"{LpSightData.Moving.Length / 4} moving segment(s) from the War Thunder EMES-15 sight");
		return obj;
	}

	private static ReticleTree.Angular WtBallistic()
	{
		ReticleTree.Angular obj = new ReticleTree.Angular(Vector2.zero, null, ReticleTree.GroupBase.Alignment.VerticalRange)
		{
			name = "WtBallistic"
		};
		AddSegments(obj.elements, LpSightData.Moving);
		return obj;
	}

	private static void AddSegments(List<ReticleTree.TransformElement> elements, float[] segments)
	{
		for (int i = 0; i + 3 < segments.Length; i += 4)
		{
			float num = segments[i + 2] - segments[i];
			float num2 = segments[i + 3] - segments[i + 1];
			float num3 = (float)Math.Sqrt(num * num + num2 * num2);
			if (!(num3 < 0.01f))
			{
				elements.Add(Line(new Vector2((segments[i] + segments[i + 2]) * 0.5f, (segments[i + 1] + segments[i + 3]) * 0.5f), (float)(Math.Atan2(num2, num) * (180.0 / Math.PI)), num3, 0.10800001f));
			}
		}
	}

	internal static ReticleTree.Line Line(Vector2 position, float degrees, float length, float thickness)
	{
		return new ReticleTree.Line(position, degrees, length, thickness)
		{
			illumination = ReticleTree.Light.Type.NightIllumination
		};
	}

	internal static ReticleTree.Circle Circle(Vector2 position, float radius, float thickness, int segments)
	{
		return new ReticleTree.Circle(position, 0f, radius, thickness, segments)
		{
			illumination = ReticleTree.Light.Type.NightIllumination
		};
	}

	internal static void AddMark(List<ReticleTree.TransformElement> elements, Vector2 position, string label, TextAlignmentOptions alignment = TextAlignmentOptions.Center, float size = 9.5f)
	{
		if (!(_font == null))
		{
			elements.Add(new ReticleTree.Text(position, 0f, label, size, alignment)
			{
				font = _font,
				illumination = ReticleTree.Light.Type.NightIllumination
			});
		}
	}

	internal static void FindFont()
	{
		if (_font != null || _fontMissing)
		{
			return;
		}
		TMP_FontAsset[] array = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
		foreach (TMP_FontAsset tMP_FontAsset in array)
		{
			if (!(tMP_FontAsset == null) && tMP_FontAsset.name.IndexOf("Reticle", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				tMP_FontAsset.hideFlags |= HideFlags.DontUnloadUnusedAsset;
				_font = tMP_FontAsset;
				MelonLogger.Msg("[L2A7V] reticle font '" + tMP_FontAsset.name + "'");
				return;
			}
		}
		_fontMissing = true;
		MelonLogger.Warning("[L2A7V] reticle font asset not found, the 2A7V plate will have no numbers");
	}

	private static string Describe(Transform node, Transform unitRoot)
	{
		List<string> list = new List<string>();
		while (node != null && node != unitRoot)
		{
			list.Add(node.name);
			node = node.parent;
		}
		list.Reverse();
		return string.Join("/", list);
	}
}
