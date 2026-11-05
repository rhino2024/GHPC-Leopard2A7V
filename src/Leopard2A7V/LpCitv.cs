using System;
using System.IO;
using System.Reflection;
using GHPC;
using GHPC.Camera;
using GHPC.Equipment.Optics;
using HarmonyLib;
using MelonLoader;
using MelonLoader.Utils;
using Reticle;
using UnityEngine;

namespace Leopard2A7V;

public sealed class LpCitvSlotMarker : MonoBehaviour
{
	internal GameObject Unit;
}

public sealed class LpCitvMonitor : MonoBehaviour
{
	internal CameraSlot Slot;

	internal GameObject Overlay;

	internal GameObject Wide;

	internal GameObject Narrow;

	internal Transform NativeReticle;

	internal GameObject HudReticle;

	private bool _slotActive;

	private bool _shown;

	private static LpCitvMonitor _hudOwner;

	private static bool _hudWasActive;

	private bool _nativeWasActive;

	private float _lastFov = -1f;

	private void OnEnable()
	{
		CameraSlot.ActiveInstanceChanged += HandleSlotChanged;
	}

	private void OnDisable()
	{
		CameraSlot.ActiveInstanceChanged -= HandleSlotChanged;
		Hide();
	}

	private void OnDestroy()
	{
		if (Overlay != null)
		{
			UnityEngine.Object.Destroy(Overlay);
			Overlay = null;
		}
	}

	internal void Bind()
	{
		HandleSlotChanged(CameraSlot.ActiveInstance);
	}

	private void HandleSlotChanged(CameraSlot slot)
	{
		_slotActive = slot != null && slot == Slot;
		if (!_slotActive)
		{
			Hide();
			return;
		}
		if (!AarController.InAar)
		{
			Show();
		}
	}

	private void LateUpdate()
	{
		if (!_slotActive)
		{
			return;
		}
		if (AarController.InAar)
		{
			Hide();
			return;
		}
		if (!_shown)
		{
			Show();
		}
		UpdateZoom();
		if (_hudOwner == this && HudReticle != null && HudReticle.activeSelf)
		{
			HudReticle.SetActive(false);
		}
		if (NativeReticle != null && NativeReticle.gameObject.activeSelf)
		{
			NativeReticle.gameObject.SetActive(false);
		}
	}

	private void Show()
	{
		if (_shown)
		{
			return;
		}
		_shown = true;
		if (Overlay != null)
		{
			Overlay.SetActive(true);
		}
		AcquireHud();
		if (NativeReticle != null)
		{
			_nativeWasActive = NativeReticle.gameObject.activeSelf;
			NativeReticle.gameObject.SetActive(false);
		}
		UpdateZoom();
		MelonLogger.Msg("[L2A7V][citv] entered; M1-style thermal monitor shown");
	}

	private void Hide()
	{
		if (!_shown)
		{
			if (Overlay != null && Overlay.activeSelf)
			{
				Overlay.SetActive(false);
			}
			return;
		}
		_shown = false;
		if (Overlay != null)
		{
			Overlay.SetActive(false);
		}
		ReleaseHud();
		if (NativeReticle != null)
		{
			NativeReticle.gameObject.SetActive(_nativeWasActive);
		}
		_lastFov = -1f;
		MelonLogger.Msg("[L2A7V][citv] left; thermal monitor hidden");
	}

	private void AcquireHud()
	{
		if (_hudOwner != null && _hudOwner != this)
		{
			_hudOwner.ReleaseHud();
		}
		if (HudReticle == null)
		{
			return;
		}
		_hudOwner = this;
		_hudWasActive = HudReticle.activeSelf;
		HudReticle.SetActive(false);
	}

	private void ReleaseHud()
	{
		if (_hudOwner != this)
		{
			return;
		}
		_hudOwner = null;
		if (HudReticle != null)
		{
			HudReticle.SetActive(_hudWasActive);
		}
	}

	private void UpdateZoom()
	{
		if (Slot == null || Mathf.Abs(Slot.CurrentFov - _lastFov) < 0.01f)
		{
			return;
		}
		_lastFov = Slot.CurrentFov;
		bool flag = _lastFov >= 10f;
		if (Wide != null && Wide.activeSelf != flag)
		{
			Wide.SetActive(flag);
		}
		if (Narrow != null && Narrow.activeSelf == flag)
		{
			Narrow.SetActive(!flag);
		}
	}
}

public static class LpCitv
{
	private static readonly MethodInfo ThisActiveChanged = AccessTools.Method(typeof(CameraSlot), "ThisActiveChanged");

	private static readonly FieldInfo SnapField = AccessTools.Field(typeof(BufferedCameraFollow), "SNAPPINESS");

	private static readonly FieldInfo SensField = AccessTools.Field(typeof(BufferedCameraFollow), "aimSensitivity3P");

	private static AssetBundle _bundle;

	private static GameObject _prefab;

	private static bool _prefabTried;

	private static Material _blit;

	private static bool _blitTried;

	public static void InstallCameraHook(HarmonyLib.Harmony harmony)
	{
		try
		{
			MethodInfo methodInfo = AccessTools.Method(typeof(BufferedCameraFollow), "LateUpdate");
			if (methodInfo == null || SnapField == null || SensField == null)
			{
				MelonLogger.Warning("[L2A7V][citv] camera-feel hook unavailable; game camera defaults retained");
				return;
			}
			harmony.Patch(methodInfo, new HarmonyMethod(typeof(LpCitv), "CameraBefore"), new HarmonyMethod(typeof(LpCitv), "CameraAfter"), null, new HarmonyMethod(typeof(LpCitv), "CameraFinalizer"), null);
			MelonLogger.Msg("[L2A7V][citv] M1-style camera feel installed; no scan or target-recognition hooks");
		}
		catch (Exception ex)
		{
			MelonLogger.Warning("[L2A7V][citv] camera-feel hook failed: " + ex.Message);
		}
	}

	public static void CameraBefore(BufferedCameraFollow __instance, out float[] __state)
	{
		__state = null;
		try
		{
			CameraSlot activeInstance = CameraSlot.ActiveInstance;
			if (activeInstance == null || AarController.InAar || activeInstance.GetComponent<LpCitvSlotMarker>() == null)
			{
				return;
			}
			__state = new float[2]
			{
				(float)SnapField.GetValue(null),
				(float)SensField.GetValue(__instance)
			};
			SnapField.SetValue(null, 0.45f);
			SensField.SetValue(__instance, 0.9375f * (activeInstance.CurrentFov / 60f));
		}
		catch (Exception)
		{
			__state = null;
		}
	}

	public static void CameraAfter(BufferedCameraFollow __instance, float[] __state)
	{
		if (__state == null)
		{
			return;
		}
		try
		{
			SnapField.SetValue(null, __state[0]);
			SensField.SetValue(__instance, __state[1]);
		}
		catch (Exception)
		{
		}
	}

	private static Exception CameraFinalizer(Exception __exception, BufferedCameraFollow __instance, float[] __state)
	{
		if (__exception != null)
		{
			CameraAfter(__instance, __state);
		}
		return __exception;
	}

	public static void Attach(GameObject unitRoot, CameraSlot commander, CameraSlot sight)
	{
		if (unitRoot == null || sight == null)
		{
			return;
		}
		Configure(commander, sight);
		LpCitvSlotMarker lpCitvSlotMarker = sight.GetComponent<LpCitvSlotMarker>();
		if (lpCitvSlotMarker == null)
		{
			lpCitvSlotMarker = sight.gameObject.AddComponent<LpCitvSlotMarker>();
		}
		lpCitvSlotMarker.Unit = unitRoot;
		GameObject gameObject = GameObject.Find("_APP_GHPC_");
		GameObject gameObject2 = Prefab();
		if (gameObject == null)
		{
			MelonLogger.Warning("[L2A7V][citv] monitor not created: game HUD root is not ready");
			return;
		}
		if (gameObject2 == null)
		{
			return;
		}
		GameObject overlay = UnityEngine.Object.Instantiate(gameObject2, gameObject.transform, worldPositionStays: false);
		overlay.name = "L2A7V CITV monitor";
		overlay.transform.localPosition = Vector3.zero;
		overlay.transform.localRotation = Quaternion.identity;
		Canvas[] componentsInChildren = overlay.GetComponentsInChildren<Canvas>(includeInactive: true);
		for (int i = 0; i < componentsInChildren.Length; i++)
		{
			componentsInChildren[i].sortingOrder = -1;
		}
		overlay.SetActive(false);
		LpCitvMonitor lpCitvMonitor = unitRoot.GetComponent<LpCitvMonitor>();
		if (lpCitvMonitor == null)
		{
			lpCitvMonitor = unitRoot.AddComponent<LpCitvMonitor>();
		}
		lpCitvMonitor.Slot = sight;
		lpCitvMonitor.Overlay = overlay;
		lpCitvMonitor.Wide = Child(overlay, "wfov");
		lpCitvMonitor.Narrow = Child(overlay, "nfov");
		lpCitvMonitor.NativeReticle = FindReticle(sight);
		lpCitvMonitor.HudReticle = FindHudReticle(gameObject);
		lpCitvMonitor.Bind();
		MelonLogger.Msg("[L2A7V][citv] monitor ready: wide=" + (lpCitvMonitor.Wide != null) + ", narrow=" + (lpCitvMonitor.Narrow != null) + ", HUD reticle=" + (lpCitvMonitor.HudReticle != null));
	}

	internal static void RefreshActiveSlot(CameraSlot sight)
	{
		if (sight == null || CameraSlot.ActiveInstance != sight)
		{
			return;
		}
		try
		{
			CameraManager instance = CameraManager.Instance;
			if (instance != null)
			{
				instance.UpdateLightMode(NightVisionType.Thermal, false);
			}
			if (ThisActiveChanged != null)
			{
				ThisActiveChanged.Invoke(sight, new object[1] { true });
			}
		}
		catch (Exception ex)
		{
			MelonLogger.Warning("[L2A7V][citv] could not refresh the active thermal slot: " + ex.Message);
		}
	}

	private static void Configure(CameraSlot commander, CameraSlot sight)
	{
		sight.VisionType = NightVisionType.Thermal;
		sight.IsExterior = false;
		sight.AllowFreeLook = true;
		sight.AllowFreeZoom = false;
		sight.BaseBlur = 0f;
		sight.OverrideFLIRResolution = true;
		sight.CanToggleFlirPolarity = true;
		sight.FLIRWidth = 1024;
		sight.FLIRHeight = 576;
		sight.DefaultFov = 15f;
		sight.OtherFovs = new float[2] { 10f, 4f };
		sight.SpriteType = CameraSpriteManager.SpriteType.DefaultScope;
		sight.DirectAttachOnly = true;
		sight.NightSightAtNightOnly = false;
		sight.FLIRBlitMaterialOverride = FullScreenBlit(sight.FLIRBlitMaterialOverride);
		if (commander != null)
		{
			commander.NightSightAtNightOnly = false;
			commander.IsLinkedNightSight = false;
			commander.LinkedNightSight = null;
		}
	}

	private static Material FullScreenBlit(Material donor)
	{
		if (_blitTried)
		{
			return _blit != null ? _blit : donor;
		}
		_blitTried = true;
		try
		{
			Shader shader = Shader.Find("Blit (FLIR)/Blit Simple");
			if (shader == null)
			{
				MelonLogger.Warning("[L2A7V][citv] FLIR blit shader missing; donor thermal appearance retained");
				return donor;
			}
			_blit = new Material(shader);
			_blit.name = "L2A7V_CITV_FullScreenBlit";
			_blit.hideFlags = HideFlags.DontUnloadUnusedAsset;
			if (donor != null)
			{
				_blit.CopyPropertiesFromMaterial(donor);
			}
			_blit.SetTexture("_PixelCookie", null);
			return _blit;
		}
		catch (Exception ex)
		{
			MelonLogger.Warning("[L2A7V][citv] full-screen thermal setup failed: " + ex.Message);
			return donor;
		}
	}

	private static GameObject Prefab()
	{
		if (_prefabTried)
		{
			return _prefab;
		}
		_prefabTried = true;
		try
		{
			foreach (AssetBundle allLoadedAssetBundle in AssetBundle.GetAllLoadedAssetBundles())
			{
				if (allLoadedAssetBundle != null && allLoadedAssetBundle.Contains("assets/citv monitor.prefab"))
				{
					_bundle = allLoadedAssetBundle;
					_prefab = _bundle.LoadAsset<GameObject>("assets/citv monitor.prefab");
					break;
				}
			}
			if (_prefab == null)
			{
				string text = Path.Combine(LpConfig.AssetDir, "CitvAssets", "citv_monitor");
				if (File.Exists(text))
				{
					_bundle = AssetBundle.LoadFromFile(text);
				}
				else
				{
					string text2 = Path.Combine(MelonEnvironment.ModsDirectory, "M1TrophyAssets", "CitvAssets", "citv_monitor");
					MelonLogger.Warning("[L2A7V][citv] own CitvAssets/citv_monitor is missing" + (File.Exists(text2) ? "; using M1 Trophy's asset as a fallback" : "; no fallback asset found"));
					if (File.Exists(text2))
					{
						_bundle = AssetBundle.LoadFromFile(text2);
					}
				}
				if (_bundle != null)
				{
					_prefab = _bundle.LoadAsset<GameObject>("assets/citv monitor.prefab");
				}
			}
			if (_prefab != null)
			{
				_prefab.hideFlags |= HideFlags.DontUnloadUnusedAsset;
				MelonLogger.Msg("[L2A7V][citv] M1 monitor prefab loaded");
			}
			else
			{
				MelonLogger.Warning("[L2A7V][citv] monitor prefab unavailable; thermal sight remains usable without the frame");
			}
		}
		catch (Exception ex)
		{
			MelonLogger.Warning("[L2A7V][citv] monitor asset skipped: " + ex.Message);
		}
		return _prefab;
	}

	private static GameObject Child(GameObject root, string name)
	{
		Transform transform = ((root == null) ? null : root.transform.Find(name));
		return transform == null ? null : transform.gameObject;
	}

	private static Transform FindReticle(CameraSlot slot)
	{
		ReticleMesh reticleMesh = ((slot == null) ? null : slot.GetComponentInChildren<ReticleMesh>(includeInactive: true));
		return reticleMesh == null ? null : reticleMesh.transform;
	}

	private static GameObject FindHudReticle(GameObject hud)
	{
		Transform transform = ((hud == null) ? null : hud.transform.Find("UIHUDCanvas"));
		Transform transform2 = ((transform == null) ? null : transform.Find("3P aim reticle"));
		return transform2 == null ? null : transform2.gameObject;
	}
}
