using System;
using System.Reflection;
using GHPC.Camera;
using GHPC.Equipment.Optics;
using GHPC.Thermals;
using HarmonyLib;
using Reticle;
using UnityEngine;

namespace Leopard2A7V;

internal static class LpCommanderThermal
{
	private static readonly MethodInfo ThisActiveChanged = AccessTools.Method(typeof(CameraSlot), "ThisActiveChanged");

	internal static bool Available()
	{
		if (FLIRCamera.Instance != null)
		{
			return ThisActiveChanged != null;
		}
		return false;
	}

	internal static string Set(CameraSlot slot, ReticleMesh mesh, GameObject unitRoot, bool on)
	{
		FLIRCamera instance = FLIRCamera.Instance;
		if (slot == null || mesh == null)
		{
			return "no slot or no reticle mesh to switch";
		}
		if (instance == null)
		{
			return "refused: this scene has no thermal camera";
		}
		if (!on)
		{
			slot.VisionType = NightVisionType.None;
			slot.CanToggleFlirPolarity = false;
			slot.OverrideFLIRResolution = false;
			slot.BaseBlur = 0f;
			slot.VibrationBlurScale = 0.1f;
			slot.VibrationShakeMultiplier = 0.15f;
			FitPlate(mesh, LpCommanderSight.OpticalPlate, ReticleTree.Light.Type.NightIllumination, atFull: false);
			instance.enabled = false;
			return "back to glass: " + mesh.reticleSO.name + " on the illumination light, the thermal camera is off, blur " + slot.VibrationBlurScale.ToString("0.##") + "/" + slot.VibrationShakeMultiplier.ToString("0.##");
		}
		Vector2Int thermalSize = LpConfig.ThermalSize;
		slot.VisionType = NightVisionType.Thermal;
		slot.OverrideFLIRResolution = true;
		slot.FLIRWidth = thermalSize.x;
		slot.FLIRHeight = thermalSize.y;
		slot.FLIRFilterMode = FilterMode.Bilinear;
		slot.CanToggleFlirPolarity = true;
		slot.BaseBlur = 0f;
		slot.VibrationBlurScale = 0f;
		slot.VibrationShakeMultiplier = 0.1f;
		string text = FitBlit(slot, unitRoot);
		FitPlate(mesh, LpCommanderSight.ThermalPlate, ReticleTree.Light.Type.Powered, atFull: true);
		string text2 = "the game's slot wiring is missing, so the imager was not started";
		if (ThisActiveChanged != null)
		{
			((Action<bool>)Delegate.CreateDelegate(typeof(Action<bool>), slot, ThisActiveChanged))(obj: true);
			text2 = "imager started by the game's own slot wiring";
		}
		return $"{thermalSize.x}x{thermalSize.y} imager on a {slot.DefaultFov:0.##} deg field, surround {text}, " + "plate " + mesh.reticleSO.name + " in powered green, polarity key live, " + text2;
	}

	private static string FitBlit(CameraSlot slot, GameObject unitRoot)
	{
		if (slot.FLIRBlitMaterialOverride != null)
		{
			return "kept " + slot.FLIRBlitMaterialOverride.name;
		}
		return "none, the picture fills the window and the sight's own border frames it";
	}

	private static void FitPlate(ReticleMesh mesh, ReticleSO plate, ReticleTree.Light.Type light, bool atFull)
	{
		if (!(mesh.reticleSO == plate))
		{
			mesh.reticleSO = plate;
			SkinnedMeshRenderer component = mesh.GetComponent<SkinnedMeshRenderer>();
			if (component != null)
			{
				component.sharedMesh = null;
			}
			mesh.Load();
			if (atFull)
			{
				mesh.SetLight(light, 1f);
				return;
			}
			mesh.GetLight(light, out var value);
			mesh.SetLight(light, (float.IsNaN(value) || value <= 0f) ? 0f : 1f);
		}
	}
}
