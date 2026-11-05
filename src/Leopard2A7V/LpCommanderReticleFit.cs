using System;
using GHPC.Camera;
using UnityEngine;

namespace Leopard2A7V;

public class LpCommanderReticleFit : MonoBehaviour
{
	internal CameraSlot Slot;

	internal float LowFieldMrad;

	internal float HighFieldMrad;

	internal float AuthoredSpanMrad;

	internal float LowProportion = 4f;

	internal float HighProportion = 6f;

	private float _appliedScale = -1f;

	private void Update()
	{
		if (!(Slot == null))
		{
			float num = 2f * Mathf.Tan(Mathf.Max(1f, Slot.CurrentFov) * 0.5f * (Mathf.PI / 180f)) * 1000f;
			float t = Mathf.Clamp01((num - HighFieldMrad) / Mathf.Max(1f, LowFieldMrad - HighFieldMrad));
			float num2 = Mathf.Lerp(HighProportion, LowProportion, t) * num / Mathf.Max(1f, AuthoredSpanMrad);
			if (Mathf.Abs(num2 - _appliedScale) > 0.0001f)
			{
				_appliedScale = num2;
				base.transform.localScale = Vector3.one * num2;
			}
		}
	}
}
