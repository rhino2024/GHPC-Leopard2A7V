using System.Collections.Generic;
using GHPC;
using MelonLoader;
using UnityEngine;

namespace Leopard2A7V;

public class LpCrewSwap : MonoBehaviour
{
	private AarVisual[] _visuals;

	private Renderer[] _renderers;

	private bool _reported;

	internal void Bind(List<AarVisual> visuals, List<Renderer> renderers)
	{
		_visuals = visuals.ToArray();
		_renderers = renderers.ToArray();
	}

	private void LateUpdate()
	{
		int num = 0;
		if (_visuals != null)
		{
			for (int i = 0; i < _visuals.Length; i++)
			{
				if (_visuals[i] != null && !_visuals[i].Hidden)
				{
					_visuals[i].Hidden = true;
					num++;
				}
			}
		}
		if (_renderers != null)
		{
			for (int j = 0; j < _renderers.Length; j++)
			{
				if (_renderers[j] != null && _renderers[j].enabled)
				{
					_renderers[j].enabled = false;
					num++;
				}
			}
		}
		if (num != 0 && !_reported)
		{
			_reported = true;
			MelonLogger.Msg($"[L2A7V] x-ray crew: the game's own figures came back {num} time(s) in " + "one frame (AarController.HandleVehiclePose re-shows crew that is present) and were kept out again; the War Thunder mannequins stay the only people drawn");
		}
	}
}
