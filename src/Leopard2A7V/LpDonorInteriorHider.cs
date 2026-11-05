using System.Collections.Generic;
using GHPC;
using MelonLoader;
using UnityEngine;

namespace Leopard2A7V;

public class LpDonorInteriorHider : MonoBehaviour
{
	private Renderer[] _renderers = new Renderer[0];

	private AarVisual[] _visuals = new AarVisual[0];

	private bool _hidden;

	private void Awake()
	{
		Collect();
		MelonLogger.Msg("[L2A7V] donor AAR renderers watched: " + _renderers.Length);
	}

	private void LateUpdate()
	{
		bool inAar = AarController.InAar;
		if (inAar && !_hidden)
		{
			Collect();
		}
		_hidden = inAar;
		if (!inAar)
		{
			return;
		}
		for (int i = 0; i < _visuals.Length; i++)
		{
			if (_visuals[i] != null && !_visuals[i].Hidden)
			{
				_visuals[i].Hidden = true;
			}
		}
		for (int j = 0; j < _renderers.Length; j++)
		{
			if (_renderers[j] != null && _renderers[j].enabled)
			{
				_renderers[j].enabled = false;
			}
		}
	}

	private void Collect()
	{
		List<Renderer> list = new List<Renderer>();
		List<AarVisual> list2 = new List<AarVisual>();
		Renderer[] componentsInChildren = GetComponentsInChildren<Renderer>(includeInactive: true);
		foreach (Renderer renderer in componentsInChildren)
		{
			if (!(renderer == null) && !(renderer is ParticleSystemRenderer) && !LpVisual.IsImportedArt(renderer.transform) && renderer.gameObject.layer != 24)
			{
				list.Add(renderer);
				AarVisual aarVisual = renderer.GetComponentInParent<AarVisual>();
				if (aarVisual == null)
				{
					aarVisual = renderer.GetComponentInChildren<AarVisual>();
				}
				if (aarVisual != null && !list2.Contains(aarVisual))
				{
					list2.Add(aarVisual);
				}
			}
		}
		_renderers = list.ToArray();
		_visuals = list2.ToArray();
	}

	private static bool IsDonorCompartment(Renderer renderer)
	{
		string text = LpBaseRig.PathOf(renderer.transform).ToLowerInvariant();
		if (text.Contains("leo2_int") || text.Contains("m1ip_interior"))
		{
			return true;
		}
		if (text.Contains("soldier_"))
		{
			return true;
		}
		Material[] sharedMaterials = renderer.sharedMaterials;
		foreach (Material material in sharedMaterials)
		{
			if (!(material == null))
			{
				string text2 = material.name.ToLowerInvariant();
				if (text2.Contains("leo2_int") || text2.Contains("m1ip_interior"))
				{
					return true;
				}
			}
		}
		return false;
	}
}
