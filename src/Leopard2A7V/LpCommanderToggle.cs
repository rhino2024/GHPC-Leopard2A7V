using GHPC;
using GHPC.Camera;
using GHPC.Equipment.Optics;
using GHPC.Player;
using GHPC.Weapons;
using MelonLoader;
using Reticle;
using UnityEngine;

namespace Leopard2A7V;

public class LpCommanderToggle : MonoBehaviour
{
	private const float YawLimit = 40f;

	private const float PitchLimit = 15f;

	private const float Sensitivity = 0.9f;

	private const float MountInterval = 0.5f;

	internal CameraSlot Slot;

	internal Transform Sight;

	internal Texture2D Border;

	internal GameObject Unit;

	internal Transform Clone;

	internal Transform Art;

	internal bool Mounted;

	internal ReticleMesh Mesh;

	private bool _inside;

	private bool _reported;

	private bool _thermal;

	private bool _warnedNoImager;

	private float _yaw;

	private float _pitch;

	private float _parentYaw;

	private float _parentPitch;

	private bool _haveParent;

	private static LpCommanderToggle _active;

	private bool _mgMode;

	private Transform _mgYaw;

	private Transform _mgPitch;

	private bool _platformLogged;

	private AimablePlatform _turretPlatform;

	private float _nextMount;

	private FireControlSystem _fcs;

	private bool _stabSaved;

	private bool _savedStabsActive;

	private void Update()
	{
		CameraManager instance = CameraManager.Instance;
		if (instance == null || Slot == null)
		{
			return;
		}
		if (!Mounted)
		{
			MountWhenReady();
		}
		PlayerInput instance2 = PlayerInput.Instance;
		if ((instance2 != null && Unit != null && (instance2.CurrentPlayerUnit == null || (object)instance2.CurrentPlayerUnit.gameObject != Unit)) || (_active != null && _active != this))
		{
			return;
		}
		if (_inside)
		{
			CameraSlot activeInstance = CameraSlot.ActiveInstance;
			if (!(activeInstance == Slot) && (!(activeInstance != null) || !(Sight != null) || !activeInstance.transform.IsChildOf(Sight)))
			{
				_inside = false;
			}
		}
		if (_inside && Input.GetKeyDown(KeyCode.V))
		{
			_mgMode = !_mgMode;
			MelonLogger.Msg("[L2A7V] MG view " + (_mgMode ? "on" : "off"));
			if (!_mgMode && Sight != null)
			{
				Sight.localRotation = Quaternion.Euler(_pitch, _yaw, 0f);
			}
		}
		if (_inside)
		{
			float axisRaw = Input.GetAxisRaw("Mouse ScrollWheel");
			if (Mathf.Abs(axisRaw) > 0.001f && Slot != null)
			{
				Slot.CycleZoom(axisRaw > 0f);
			}
		}
		bool flag = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
		if (_inside && Slot != null && (Input.GetKeyDown(KeyCode.LeftControl) || Input.GetKeyDown(KeyCode.RightControl)))
		{
			Slot.ToggleFLIRPolarity();
		}
		if (_inside)
		{
			float num = Input.GetAxisRaw("Mouse X") * 0.9f;
			_yaw = Mathf.Repeat(_yaw + num + 180f, 360f) - 180f;
			_pitch = Mathf.Clamp(_pitch - Input.GetAxisRaw("Mouse Y") * 0.9f, -15f, 15f);
			if (Sight != null && Sight.parent != null)
			{
				Vector3 eulerAngles = Sight.parent.eulerAngles;
				if (_haveParent)
				{
					_yaw -= Mathf.DeltaAngle(_parentYaw, eulerAngles.y);
					_pitch = Mathf.Clamp(_pitch - Mathf.DeltaAngle(_parentPitch, eulerAngles.x), -15f, 15f);
				}
				_parentYaw = eulerAngles.y;
				_parentPitch = eulerAngles.x;
				_haveParent = true;
				Sight.localRotation = Quaternion.Euler(_pitch, _yaw, 0f);
			}
		}
		HideHeadArt();
	}

	private void MountWhenReady()
	{
		if (!(Unit == null) && !(Clone == null) && !(Time.unscaledTime < _nextMount))
		{
			_nextMount = Time.unscaledTime + 0.5f;
			Transform transform = LpCommanderSight.FindHead(Unit);
			if (!(transform == null))
			{
				LpCommanderSight.Mount(Clone, transform);
				Sight = transform;
				Art = LpCommanderSight.FindHeadArt(transform);
				Mounted = true;
				_yaw = 0f;
				_pitch = 0f;
				MelonLogger.Msg("[L2A7V] commander's eyepiece mounted on the sight head " + transform.name + ": eye " + Clone.localPosition.z.ToString("0.##") + " m aft of its centre, device art " + ((Art == null) ? "not found" : ("'" + Art.name + "'")) + ", so the device now turns with the view");
			}
		}
	}

	private AimablePlatform TurretPlatform()
	{
		if (_turretPlatform != null)
		{
			return _turretPlatform;
		}
		Unit unit = ((Unit == null) ? null : Unit.GetComponent<Unit>());
		if (unit == null)
		{
			return null;
		}
		AimablePlatform aimablePlatform = null;
		AimablePlatform[] componentsInChildren = unit.GetComponentsInChildren<AimablePlatform>(includeInactive: true);
		foreach (AimablePlatform aimablePlatform2 in componentsInChildren)
		{
			if (!(aimablePlatform2 == null) && !(aimablePlatform2.Transform == null))
			{
				string text = LpBaseRig.PathOf(aimablePlatform2.Transform).ToLowerInvariant();
				if (!_platformLogged)
				{
					MelonLogger.Msg("[L2A7V] aimable platform '" + aimablePlatform2.Name + "' axis=" + aimablePlatform2.Axis.ToString() + " at " + text);
				}
				if (aimablePlatform2.Axis.ToString() == "Y" || text.Contains("turret"))
				{
					_turretPlatform = aimablePlatform2;
					break;
				}
				if (aimablePlatform == null && !text.Contains("gun") && !text.Contains("mantlet"))
				{
					aimablePlatform = aimablePlatform2;
				}
			}
		}
		if (_turretPlatform == null)
		{
			_turretPlatform = aimablePlatform;
		}
		_platformLogged = true;
		if (_turretPlatform == null)
		{
			MelonLogger.Warning("[L2A7V] no turret platform found on " + unit.name + " (stabiliser stays off)");
		}
		else
		{
			MelonLogger.Msg("[L2A7V] turret platform '" + _turretPlatform.Name + "' (axis " + _turretPlatform.Axis.ToString() + ")");
		}
		return _turretPlatform;
	}

	private void ResolveFcs()
	{
		if (!(_fcs != null))
		{
			Unit unit = ((Unit == null) ? null : Unit.GetComponent<Unit>());
			if (unit != null)
			{
				_fcs = unit.GetComponentInChildren<FireControlSystem>(includeInactive: true);
			}
		}
	}

	private void SetVehicleStab(bool on)
	{
		ResolveFcs();
		FireControlSystem fcs = _fcs;
		if (fcs == null)
		{
			return;
		}
		if (on)
		{
			if (!_stabSaved)
			{
				_savedStabsActive = fcs.StabsActive;
				_stabSaved = true;
			}
			fcs.SetStabsActive(value: true);
			MelonLogger.Msg("[L2A7V] commander sight stabiliser on");
		}
		else
		{
			if (_stabSaved)
			{
				fcs.SetStabsActive(_savedStabsActive);
				_stabSaved = false;
			}
			MelonLogger.Msg("[L2A7V] commander sight stabiliser restored (stabs " + (_savedStabsActive ? "on" : "off") + ")");
		}
	}

	private void HideHeadArt()
	{
		if (!(Art == null))
		{
			Vector3 vector = (_inside ? Vector3.zero : Vector3.one);
			if (Art.localScale != vector)
			{
				Art.localScale = vector;
			}
		}
	}

	private void SwitchThermal()
	{
		if (!LpCommanderThermal.Available())
		{
			if (!_warnedNoImager)
			{
				_warnedNoImager = true;
				MelonLogger.Warning("[L2A7V] commander's sight: T asked for the thermal channel but this scene has no thermal camera, so the sight stays on its glass");
			}
		}
		else
		{
			_thermal = !_thermal;
			MelonLogger.Msg("[L2A7V] commander's sight " + (_thermal ? "on thermal: " : "on glass: ") + LpCommanderThermal.Set(Slot, Mesh, Unit, _thermal));
		}
	}

	private void OnGUI()
	{
		if (_inside && !(Border == null) && (!_thermal || !(Slot != null) || !(Slot.FLIRBlitMaterialOverride != null)))
		{
			GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Border, ScaleMode.StretchToFill, alphaBlend: true);
		}
	}
}
