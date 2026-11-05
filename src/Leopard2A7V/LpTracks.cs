using System;
using System.Collections.Generic;
using MelonLoader;
using NWH.VehiclePhysics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Leopard2A7V;

public static class LpTracks
{
	private sealed class Loop
	{
		public List<ObjGeometry> Parts;

		public Vector3 Min;

		public Vector3 Max;

		public float CentreY;

		public float Rear;

		public float Front;

		public float Radius;

		public float Height;

		public float Length;

		public float Perimeter;

		public float Repeats;

		public float ScrollUnitsPerMetre
		{
			get
			{
				if (!(Perimeter > 0.01f))
				{
					return TreadsPerMetre;
				}
				return Repeats / Perimeter;
			}
		}

		public float Arc(Vector3 p)
		{
			return LoopDistance(p.y, p.z, CentreY, Rear, Front, Radius) / Perimeter;
		}
	}

	private const string Tag = "[L2A7V][track]";

	private const float TreadStripMinU = 0.005f;

	private const float TreadStripMaxU = 0.073f;

	private static readonly Dictionary<string, Mesh> LoopMeshes = new Dictionary<string, Mesh>();

	private static float TreadsPerMetre => 0.746f;

	public static string Status { get; private set; } = "not attempted";


	public static bool DonorFallback { get; private set; }

	public static void Attach(Transform hullPivot, Transform unitRoot)
	{
		DonorFallback = false;
		Status = "not built";
		VehicleController vehicleController = unitRoot.GetComponent<VehicleController>();
		if (vehicleController == null)
		{
			vehicleController = unitRoot.GetComponentInChildren<VehicleController>(includeInactive: true);
		}
		Tracks tracks = ((vehicleController == null) ? null : vehicleController.tracks);
		if (tracks == null || !tracks.trackedVehicle)
		{
			Status = KeepDonorArt("the donor has no track controller to read a speed from");
			return;
		}
		LpTrackDriver lpTrackDriver = unitRoot.gameObject.GetComponent<LpTrackDriver>();
		if (lpTrackDriver == null)
		{
			lpTrackDriver = unitRoot.gameObject.AddComponent<LpTrackDriver>();
		}
		lpTrackDriver.SetNative(tracks);
		MeshRenderer meshRenderer = BuildSide(hullPivot, "track_l", "L2A7V_TrackLeft", tracks.leftTrackRenderer, lpTrackDriver, isLeft: true);
		MeshRenderer meshRenderer2 = BuildSide(hullPivot, "track_r", "L2A7V_TrackRight", tracks.rightTrackRenderer, lpTrackDriver, isLeft: false);
		int num = ((!(meshRenderer == null)) ? 1 : 0) + ((!(meshRenderer2 == null)) ? 1 : 0);
		if (num == 2)
		{
			Status = "both loops rebuilt and scrolled on the donor's sheet";
			return;
		}
		if (meshRenderer != null)
		{
			UnityEngine.Object.DestroyImmediate(meshRenderer.gameObject);
		}
		if (meshRenderer2 != null)
		{
			UnityEngine.Object.DestroyImmediate(meshRenderer2.gameObject);
		}
		lpTrackDriver.SetSide(isLeft: true, null, 0f);
		lpTrackDriver.SetSide(isLeft: false, null, 0f);
		Status = KeepDonorArt($"only {num} of 2 loops could be built");
	}

	private static string KeepDonorArt(string reason)
	{
		DonorFallback = true;
		MelonLogger.Warning("[L2A7V][track] " + reason + ", so UseDonorTrackArt's donor art is kept instead");
		return reason + ", donor art kept";
	}

	private static MeshRenderer BuildSide(Transform hullPivot, string objectName, string nodeName, Renderer donor, LpTrackDriver driver, bool isLeft)
	{
		Loop loop = Measure(objectName);
		if (loop == null)
		{
			MelonLogger.Warning("[L2A7V][track] the export has no '" + objectName + "' object");
			return null;
		}
		Material material = TreadMaterial(donor, nodeName);
		if (material == null)
		{
			return null;
		}
		Mesh mesh = BuildMesh(loop, nodeName);
		if (mesh == null)
		{
			return null;
		}
		MeshRenderer meshRenderer = LpVisual.AttachMesh(hullPivot, nodeName, mesh);
		if (meshRenderer == null)
		{
			return null;
		}
		meshRenderer.sharedMaterials = new Material[1] { material };
		driver.SetSide(isLeft, material, loop.ScrollUnitsPerMetre);
		MelonLogger.Msg("[L2A7V][track]" + $" {objectName} -> {nodeName}: run {loop.Length:0.00} m, loop height " + $"{loop.Height:0.00} m, perimeter {loop.Perimeter:0.00} m, " + $"{loop.Repeats:0} tread repeat(s) ({TreadsPerMetre:0.###}/m), " + "sheet '" + SheetNameOf(donor) + "'");
		return meshRenderer;
	}

	private static Material TreadMaterial(Renderer donor, string nodeName)
	{
		if (donor == null || donor.sharedMaterial == null)
		{
			MelonLogger.Error("[L2A7V][track] the donor's track renderer is gone, so " + nodeName + " has no tread sheet to scroll; turn UseDonorTrackArt on instead");
			return null;
		}
		Material obj = new Material(donor.sharedMaterial)
		{
			name = "L2A7V seamless tread " + nodeName,
			mainTextureScale = Vector2.one,
			mainTextureOffset = Vector2.zero
		};
		SetIfPresent(obj, "_Cutoff", 0f);
		SetIfPresent(obj, "_AlbedoMult", 0.8f);
		SetIfPresent(obj, "_GlossMapScale", 0.1f);
		SetIfPresent(obj, "_SpecularHighlights", 0f);
		SetIfPresent(obj, "_GlossyReflections", 0f);
		return obj;
	}

	private static void SetIfPresent(Material material, string property, float value)
	{
		if (material.HasProperty(property))
		{
			material.SetFloat(property, value);
		}
	}

	private static string SheetNameOf(Renderer donor)
	{
		if (donor == null || donor.sharedMaterial == null)
		{
			return "<none>";
		}
		Texture mainTexture = donor.sharedMaterial.mainTexture;
		if (!(mainTexture == null))
		{
			return mainTexture.name + " " + mainTexture.width + "x" + mainTexture.height;
		}
		return "<no main texture>";
	}

	private static Loop Measure(string objectName)
	{
		Loop loop = new Loop
		{
			Parts = new List<ObjGeometry>(),
			Min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue),
			Max = new Vector3(float.MinValue, float.MinValue, float.MinValue)
		};
		foreach (ObjGeometry geometry in ObjModel.Geometries)
		{
			if (geometry.Group == LpGroup.RunningGear && string.Equals(geometry.ObjectName, objectName, StringComparison.OrdinalIgnoreCase))
			{
				loop.Parts.Add(geometry);
				for (int i = 0; i < geometry.Positions.Length; i++)
				{
					loop.Min = Vector3.Min(loop.Min, geometry.Positions[i]);
					loop.Max = Vector3.Max(loop.Max, geometry.Positions[i]);
				}
			}
		}
		if (loop.Parts.Count == 0)
		{
			return null;
		}
		loop.Height = loop.Max.y - loop.Min.y;
		loop.Length = loop.Max.z - loop.Min.z;
		loop.CentreY = (loop.Min.y + loop.Max.y) * 0.5f;
		loop.Radius = Mathf.Max(0.1f, loop.Height * 0.5f);
		loop.Rear = loop.Min.z + loop.Radius;
		loop.Front = loop.Max.z - loop.Radius;
		loop.Perimeter = 2f * (loop.Front - loop.Rear) + Mathf.PI * 2f * loop.Radius;
		if (loop.Front <= loop.Rear || loop.Perimeter < 1f)
		{
			MelonLogger.Warning("[L2A7V][track]" + $" '{objectName}' is not a loop: run {loop.Length:0.00} m against a " + $"loop height of {loop.Height:0.00} m");
			return null;
		}
		loop.Repeats = Mathf.Max(1f, Mathf.Round(loop.Perimeter * TreadsPerMetre));
		return loop;
	}

	public static float LoopDistance(float y, float z, float centreY, float rear, float front, float radius)
	{
		if (z > front)
		{
			return radius * (Mathf.PI / 2f - Mathf.Atan2(y - centreY, z - front));
		}
		if (z < rear)
		{
			return Mathf.PI * radius + front - rear + radius * (Mathf.Atan2(y - centreY, rear - z) + Mathf.PI / 2f);
		}
		if (y < centreY)
		{
			return Mathf.PI * radius + front - z;
		}
		return Mathf.PI * 2f * radius + front - rear + z - rear;
	}

	private static Mesh BuildMesh(Loop loop, string cacheKey)
	{
		string key = cacheKey + "|" + loop.ScrollUnitsPerMetre.ToString("0.####");
		if (LoopMeshes.TryGetValue(key, out var value) && value != null)
		{
			return value;
		}
		List<Vector3> list = new List<Vector3>();
		List<Vector3> list2 = new List<Vector3>();
		List<Vector4> list3 = new List<Vector4>();
		List<Vector2> list4 = new List<Vector2>();
		List<int> list5 = new List<int>();
		float num = loop.Max.x - loop.Min.x;
		foreach (ObjGeometry part in loop.Parts)
		{
			int count = list.Count;
			for (int i = 0; i < part.Positions.Length; i++)
			{
				Vector3 item = part.Positions[i];
				list.Add(item);
				list2.Add(part.Normals[i]);
				list3.Add(part.Tangents[i]);
			}
			float[] array = new float[part.Positions.Length];
			for (int j = 0; j < array.Length; j++)
			{
				array[j] = loop.Arc(part.Positions[j]);
			}
			for (int k = 0; k + 2 < part.Triangles.Length; k += 3)
			{
				float num2 = 1f;
				float num3 = 0f;
				for (int l = 0; l < 3; l++)
				{
					float num4 = array[part.Triangles[k + l]];
					if (num4 < num2)
					{
						num2 = num4;
					}
					if (num4 > num3)
					{
						num3 = num4;
					}
				}
				for (int m = 0; m < 3; m++)
				{
					int num5 = part.Triangles[k + m];
					float num6 = array[num5];
					if (num3 - num2 > 0.5f && num6 < 0.5f)
					{
						num6 += 1f;
					}
					float t = ((num > 0.0001f) ? Mathf.InverseLerp(loop.Min.x, loop.Max.x, list[count + num5].x) : 0.5f);
					list4.Add(new Vector2(Mathf.Lerp(0.005f, 0.073f, t), num6 * loop.Repeats));
					list5.Add(count + num5);
				}
			}
		}
		if (list5.Count < 3)
		{
			return null;
		}
		Mesh mesh = new Mesh
		{
			name = "L2A7V_" + cacheKey,
			indexFormat = IndexFormat.UInt32
		};
		mesh.SetVertices(list);
		mesh.SetNormals(list2);
		mesh.SetUVs(0, list4);
		mesh.subMeshCount = 1;
		mesh.SetTriangles(list5, 0);
		mesh.RecalculateTangents();
		mesh.RecalculateBounds();
		mesh.UploadMeshData(markNoLongerReadable: true);
		LoopMeshes[key] = mesh;
		return mesh;
	}
}
