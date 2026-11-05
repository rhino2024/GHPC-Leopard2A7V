using System;
using System.Collections.Generic;
using System.Text;
using MelonLoader;
using NWH.VehiclePhysics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Leopard2A7V;

public static class LpDiagnostics
{
	private const string Tag = "[L2A7V][diag]";

	private static readonly string[] FloatProps = new string[29]
	{
		"_Mode", "_Surface", "_Cull", "_ZTest", "_ZWrite", "_SrcBlend", "_DstBlend", "_AlphaToMask", "_Cutoff", "_XrayAlpha",
		"_Glossiness", "_Metallic", "_GlossMapScale", "_SmoothnessTextureChannel", "_SpecularHighlights", "_ParallaxMapping", "_OcclusionStrength", "_DetailMaskChannel", "_DetailScale", "_BumpScale",
		"_CamoAmount", "_PaintMaskPower", "_paintmaskcontrast", "_snow", "_dirt", "_ScorchAmount", "_heat", "_FLIR_Emission", "_hackcamoUVs"
	};

	private static readonly string[] TextureProps = new string[9] { "_Albedo", "_Normal", "_Occlusion", "_PaintMask", "_Smoothness", "_CamoLayer", "_noise1", "_ScorchAlbedo", "_DetailAlbedoMOTexel" };

	private static bool _dumped;

	public static void DumpOnce(GameObject unitRoot)
	{
		if (!_dumped)
		{
			_dumped = true;
			Dump(unitRoot);
		}
	}

	public static void Dump(GameObject unitRoot)
	{
		if (unitRoot == null)
		{
			return;
		}
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("---- materials ----");
		Describe(stringBuilder, "template", LpVisual.TemplateMaterial);
		foreach (KeyValuePair<string, Material> createdMaterial in LpAssets.CreatedMaterials)
		{
			Describe(stringBuilder, "clone[" + createdMaterial.Key + "]", createdMaterial.Value);
		}
		stringBuilder.AppendLine("---- imported renderers ----");
		int num = 0;
		Renderer[] componentsInChildren = unitRoot.GetComponentsInChildren<Renderer>(includeInactive: true);
		foreach (Renderer renderer in componentsInChildren)
		{
			if (LpVisual.IsImportedArt(renderer.transform))
			{
				num++;
				Describe(stringBuilder, renderer);
			}
		}
		stringBuilder.AppendLine("---- donor track drive ----");
		DescribeTrackDrive(stringBuilder, unitRoot);
		stringBuilder.AppendLine("---- donor renderers the hide rule covers but did not strip ----");
		int num2 = 0;
		int num3 = 0;
		componentsInChildren = unitRoot.GetComponentsInChildren<Renderer>(includeInactive: true);
		foreach (Renderer renderer2 in componentsInChildren)
		{
			if (!LpVisual.IsImportedArt(renderer2.transform) && !(renderer2 is ParticleSystemRenderer))
			{
				if (!LpBaseRig.ShouldHide(renderer2.transform, renderer2.name))
				{
					num3++;
				}
				else if (HasGeometry(renderer2) && num2++ < 60)
				{
					stringBuilder.AppendLine("[L2A7V][diag] still modelled " + PathOf(renderer2.transform) + " mat " + MaterialSummary(renderer2) + " enabled=" + renderer2.enabled + " active=" + renderer2.gameObject.activeInHierarchy);
				}
			}
		}
		stringBuilder.AppendLine("[L2A7V][diag] " + num + " imported renderer(s), " + num3 + " donor renderer(s) kept on purpose, " + num2 + " donor renderer(s) that still own a mesh");
		MelonLogger.Msg(stringBuilder.ToString());
	}

	private static void Describe(StringBuilder sb, string label, Material material)
	{
		if (material == null)
		{
			sb.AppendLine("[L2A7V][diag] " + label + ": <null>");
			return;
		}
		sb.AppendLine("[L2A7V][diag] " + label + " name='" + material.name + "' shader='" + ((material.shader == null) ? "<null>" : material.shader.name) + "' queue=" + material.renderQueue + " pass=" + material.passCount + " kw='" + string.Join(",", material.shaderKeywords) + "'");
		sb.AppendLine("[L2A7V][diag]   floats: " + FloatSummary(material));
		sb.AppendLine("[L2A7V][diag]   color: " + (material.HasProperty("_Color") ? ColorToString(material.GetColor("_Color")) : "-"));
		sb.AppendLine("[L2A7V][diag]   texes: " + TextureSummary(material));
	}

	private static void Describe(StringBuilder sb, Renderer renderer)
	{
		Mesh mesh = ((renderer.GetComponent<MeshFilter>() == null) ? null : renderer.GetComponent<MeshFilter>().sharedMesh);
		sb.Append("[L2A7V][diag]").Append(" OURS ").Append(renderer.name)
			.Append(" enabled=")
			.Append(renderer.enabled)
			.Append(" active=")
			.Append(renderer.gameObject.activeInHierarchy)
			.Append(" layer=")
			.Append(renderer.gameObject.layer)
			.Append(" shadows=")
			.Append(renderer.shadowCastingMode)
			.Append(" receiveShadows=")
			.Append(renderer.receiveShadows)
			.Append(" probe=")
			.Append(renderer.lightProbeUsage)
			.Append(" scale=")
			.Append(renderer.transform.lossyScale)
			.Append(" bounds=")
			.Append(BoundsToString(renderer.bounds))
			.AppendLine();
		if (mesh != null)
		{
			sb.AppendLine("[L2A7V][diag]   mesh " + mesh.name + " verts=" + mesh.vertexCount + " submeshes=" + mesh.subMeshCount + " mats=" + renderer.sharedMaterials.Length + " attrs=" + AttributeSummary(mesh));
		}
		sb.AppendLine("[L2A7V][diag]   mats " + MaterialSummary(renderer));
		MaterialPropertyBlock materialPropertyBlock = new MaterialPropertyBlock();
		renderer.GetPropertyBlock(materialPropertyBlock);
		string text = PropertyBlockSummary(materialPropertyBlock);
		if (text.Length > 0)
		{
			sb.AppendLine("[L2A7V][diag]   propblock " + text);
		}
	}

	private static void DescribeTrackDrive(StringBuilder sb, GameObject unitRoot)
	{
		VehicleController vehicleController = unitRoot.GetComponent<VehicleController>() ?? unitRoot.GetComponentInChildren<VehicleController>(includeInactive: true);
		Tracks tracks = ((vehicleController == null) ? null : vehicleController.tracks);
		if (tracks == null)
		{
			sb.AppendLine("[L2A7V][diag] no NWH track controller, so nothing in the game scrolls a track here");
			return;
		}
		sb.AppendLine("[L2A7V][diag]" + $" trackedVehicle={tracks.trackedVehicle} uvDirection={tracks.uvDirection} " + $"wheelSize={tracks.wheelEnlargementCoefficient:0.##} ourLoops='{LpTracks.Status}'");
		DescribeTrackSide(sb, "left", tracks.leftTrackRenderer);
		DescribeTrackSide(sb, "right", tracks.rightTrackRenderer);
	}

	private static void DescribeTrackSide(StringBuilder sb, string label, Renderer renderer)
	{
		if (renderer == null)
		{
			sb.AppendLine("[L2A7V][diag] " + label + " donor loop: <null renderer>");
			return;
		}
		Material material = null;
		Material[] sharedMaterials = renderer.sharedMaterials;
		foreach (Material material2 in sharedMaterials)
		{
			if (material2 != null)
			{
				material = material2;
				break;
			}
		}
		StringBuilder stringBuilder = new StringBuilder("[L2A7V][diag] " + label + " donor loop " + PathOf(renderer.transform) + " mat '" + ((material == null) ? "<none>" : material.name) + "'");
		Texture texture = ((material == null) ? null : material.mainTexture);
		if (texture == null)
		{
			stringBuilder.Append(" sheet=<none>");
		}
		else
		{
			Vector2 mainTextureScale = material.mainTextureScale;
			stringBuilder.Append($" sheet={texture.name} {texture.width}x{texture.height} wrap={texture.wrapMode} " + $"scale({mainTextureScale.x:0.###},{mainTextureScale.y:0.###})");
		}
		stringBuilder.Append(" run/uv:");
		AppendLoopScale(stringBuilder, renderer, (material == null) ? Vector2.one : material.mainTextureScale);
		sb.AppendLine(stringBuilder.ToString());
	}

	private static void AppendLoopScale(StringBuilder sb, Renderer renderer, Vector2 scale)
	{
		Mesh mesh = ((renderer is SkinnedMeshRenderer skinnedMeshRenderer) ? skinnedMeshRenderer.sharedMesh : ((renderer.GetComponent<MeshFilter>() == null) ? null : renderer.GetComponent<MeshFilter>().sharedMesh));
		if (mesh == null)
		{
			sb.Append(" uv=<no mesh>");
			return;
		}
		Vector2[] uv;
		try
		{
			uv = mesh.uv;
		}
		catch (Exception ex)
		{
			sb.Append(" uv=<unreadable: " + ex.GetType().Name + ">");
			return;
		}
		if (uv == null || uv.Length == 0)
		{
			sb.Append(" uv=<none>");
			return;
		}
		float num = float.MaxValue;
		float num2 = float.MinValue;
		float num3 = float.MaxValue;
		float num4 = float.MinValue;
		for (int i = 0; i < uv.Length; i++)
		{
			if (uv[i].y < num)
			{
				num = uv[i].y;
			}
			if (uv[i].y > num2)
			{
				num2 = uv[i].y;
			}
			if (uv[i].x < num3)
			{
				num3 = uv[i].x;
			}
			if (uv[i].x > num4)
			{
				num4 = uv[i].x;
			}
		}
		Bounds bounds = mesh.bounds;
		float num5 = Mathf.Max(0.1f, bounds.extents.y);
		float num6 = 2f * Mathf.Max(0f, bounds.size.z - 2f * num5) + Mathf.PI * 2f * num5;
		if (num6 < 0.5f)
		{
			sb.Append(" uv" + Range(num3, num4) + "/" + Range(num, num2));
			return;
		}
		sb.Append($" run={bounds.size.z:0.00} m perimeter={num6:0.00} m" + " uv" + Range(num3, num4) + "/" + Range(num, num2) + $" scale({scale.x:0.###},{scale.y:0.###})" + $" -> {(num2 - num) * scale.y / num6:0.###} repeat(s) per metre along y, " + $"{(num4 - num3) * scale.x / Mathf.Max(0.01f, bounds.size.x):0.###} per metre across");
	}

	private static string Range(float low, float high)
	{
		return $"({low:0.##}..{high:0.##})";
	}

	private static bool HasGeometry(Renderer renderer)
	{
		if (renderer is SkinnedMeshRenderer skinnedMeshRenderer)
		{
			return DrawsTriangles(skinnedMeshRenderer.sharedMesh);
		}
		MeshFilter component = renderer.GetComponent<MeshFilter>();
		if (component != null)
		{
			return DrawsTriangles(component.sharedMesh);
		}
		return false;
	}

	private static bool DrawsTriangles(Mesh mesh)
	{
		if (mesh == null)
		{
			return false;
		}
		for (int i = 0; i < mesh.subMeshCount; i++)
		{
			if (mesh.GetIndexCount(i) != 0)
			{
				return true;
			}
		}
		return false;
	}

	private static string MaterialSummary(Renderer renderer)
	{
		StringBuilder stringBuilder = new StringBuilder();
		Material[] sharedMaterials = renderer.sharedMaterials;
		for (int i = 0; i < sharedMaterials.Length; i++)
		{
			stringBuilder.Append((sharedMaterials[i] == null) ? "<null>" : sharedMaterials[i].name).Append((i + 1 < sharedMaterials.Length) ? "," : "");
		}
		return stringBuilder.ToString();
	}

	private static string FloatSummary(Material material)
	{
		StringBuilder stringBuilder = new StringBuilder();
		string[] floatProps = FloatProps;
		foreach (string text in floatProps)
		{
			if (material.HasProperty(text))
			{
				stringBuilder.Append(text).Append('=').Append(material.GetFloat(text).ToString("0.###"))
					.Append(' ');
			}
		}
		return stringBuilder.ToString();
	}

	private static string TextureSummary(Material material)
	{
		StringBuilder stringBuilder = new StringBuilder();
		string[] textureProps = TextureProps;
		foreach (string text in textureProps)
		{
			if (!material.HasProperty(text))
			{
				continue;
			}
			Texture texture = material.GetTexture(text);
			stringBuilder.Append(text).Append('=');
			if (texture == null)
			{
				stringBuilder.Append("<none>");
			}
			else
			{
				Texture2D texture2D = texture as Texture2D;
				stringBuilder.Append(texture.name).Append('(').Append(texture.width)
					.Append('x')
					.Append(texture.height);
				if (texture2D != null)
				{
					stringBuilder.Append(',').Append(texture2D.format).Append(',')
						.Append(texture2D.graphicsFormat);
				}
				stringBuilder.Append(',').Append(texture.wrapMode).Append(')');
			}
			stringBuilder.Append(' ');
		}
		return stringBuilder.ToString();
	}

	private static string AttributeSummary(Mesh mesh)
	{
		StringBuilder stringBuilder = new StringBuilder();
		VertexAttributeDescriptor[] vertexAttributes = mesh.GetVertexAttributes();
		for (int i = 0; i < vertexAttributes.Length; i++)
		{
			VertexAttributeDescriptor vertexAttributeDescriptor = vertexAttributes[i];
			if (vertexAttributeDescriptor.dimension != 0)
			{
				stringBuilder.Append(vertexAttributeDescriptor.attribute).Append('x').Append(vertexAttributeDescriptor.dimension)
					.Append(',');
			}
		}
		return stringBuilder.ToString();
	}

	private static string PropertyBlockSummary(MaterialPropertyBlock block)
	{
		StringBuilder stringBuilder = new StringBuilder();
		string[] floatProps = FloatProps;
		foreach (string text in floatProps)
		{
			float @float = block.GetFloat(text);
			if (!(Mathf.Abs(@float) < 1E-05f))
			{
				stringBuilder.Append(text).Append('=').Append(@float.ToString("0.###"))
					.Append(' ');
			}
		}
		return stringBuilder.ToString();
	}

	private static string ColorToString(Color color)
	{
		return $"({color.r:0.##},{color.g:0.##},{color.b:0.##},{color.a:0.##})";
	}

	private static string BoundsToString(Bounds bounds)
	{
		return $"c({bounds.center.x:0.##},{bounds.center.y:0.##},{bounds.center.z:0.##})" + $"s({bounds.size.x:0.##},{bounds.size.y:0.##},{bounds.size.z:0.##})";
	}

	private static string PathOf(Transform transform)
	{
		StringBuilder stringBuilder = new StringBuilder(transform.name);
		Transform parent = transform.parent;
		int num = 0;
		while (parent != null && num++ < 24)
		{
			stringBuilder.Insert(0, parent.name + "/");
			parent = parent.parent;
		}
		return stringBuilder.ToString();
	}
}
