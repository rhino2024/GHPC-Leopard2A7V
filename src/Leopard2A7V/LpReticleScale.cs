using System;
using GHPC.Camera;
using MelonLoader;
using UnityEngine;

namespace Leopard2A7V;

internal sealed class LpReticleScale : MonoBehaviour
{
	internal CameraSlot Slot;

	internal float ReferenceFov = 11.34f;

	private float _last = -1f;

	private float _appliedScale = -1f;

	private void Update()
	{
		if (!(Slot == null))
		{
			float num = Mathf.Max(1f, Slot.CurrentFov);
			float num2 = Mathf.Tan(num * 0.5f * (Mathf.PI / 180f)) / Mathf.Tan(ReferenceFov * 0.5f * (Mathf.PI / 180f));
			if (Mathf.Abs(num2 - _appliedScale) > 0.0001f)
			{
				_appliedScale = num2;
				base.transform.localScale = Vector3.one * num2;
			}
			if (Mathf.Abs(num2 - _last) > 0.05f)
			{
				_last = num2;
				MelonLogger.Msg("[L2A7V] commander reticle scale x" + num2.ToString("0.00") + " at " + num.ToString("0.##") + " deg (reference " + ReferenceFov.ToString("0.##") + ")");
			}
		}
	}
}
