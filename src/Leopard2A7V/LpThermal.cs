using System.Collections.Generic;
using GHPC.Camera;
using GHPC.Equipment.Optics;
using GHPC.Thermals;
using MelonLoader;
using UnityEngine;

namespace Leopard2A7V;

public static class LpThermal
{
	public static void Apply(GameObject unitRoot)
	{
		if (unitRoot == null)
		{
			return;
		}
		Vector2Int thermalSize = LpConfig.ThermalSize;
		int num = 0;
		CameraSlot[] componentsInChildren = unitRoot.GetComponentsInChildren<CameraSlot>(includeInactive: true);
		foreach (CameraSlot cameraSlot in componentsInChildren)
		{
			if (!(cameraSlot == null) && cameraSlot.VisionType == NightVisionType.Thermal && !LpVisual.IsImportedArt(cameraSlot.transform))
			{
				string arg = (cameraSlot.OverrideFLIRResolution ? $"{cameraSlot.FLIRWidth}x{cameraSlot.FLIRHeight}" : "the screen");
				float baseBlur = cameraSlot.BaseBlur;
				float vibrationBlurScale = cameraSlot.VibrationBlurScale;
				cameraSlot.OverrideFLIRResolution = true;
				cameraSlot.FLIRWidth = thermalSize.x;
				cameraSlot.FLIRHeight = thermalSize.y;
				cameraSlot.FLIRFilterMode = FilterMode.Bilinear;
				cameraSlot.BaseBlur = 0f;
				cameraSlot.VibrationBlurScale = 0f;
				num++;
				if (CameraSlot.ActiveInstance == cameraSlot && FLIRCamera.Instance != null)
				{
					FLIRCamera.Instance.WidthOverride = thermalSize.x;
					FLIRCamera.Instance.HeightOverride = thermalSize.y;
					FLIRCamera.Instance.TextureFiltering = FilterMode.Bilinear;
				}
				MelonLogger.Msg("[L2A7V] thermal channel " + PathOf(cameraSlot.transform, unitRoot.transform) + " " + $"now renders at {thermalSize.x}x{thermalSize.y} instead of {arg}" + ((baseBlur != 0f || vibrationBlurScale != 0f) ? $", and its {baseBlur:0.##}/{vibrationBlurScale:0.##} of blur is off" : ", blur already at zero"));
			}
		}
		if (num == 0)
		{
			MelonLogger.Msg("[L2A7V] this unit carries no thermal channel, so there is no picture to sharpen");
		}
		else if (Mathf.Abs(((thermalSize.y == 0) ? 0f : ((float)thermalSize.x / (float)thermalSize.y)) - 2f) > 0.01f)
		{
			MelonLogger.Warning($"[L2A7V] ThermalResolution \"{thermalSize.x},{thermalSize.y}\" is not 2:1 like the " + "donor's channel; the game derives the vertical field from this ratio, so the picture's geometry - and the reticle's place on it - will shift");
		}
	}

	private static string PathOf(Transform node, Transform unitRoot)
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
