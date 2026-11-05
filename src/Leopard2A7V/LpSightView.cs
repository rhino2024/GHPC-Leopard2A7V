using System;
using System.Collections.Generic;
using System.Text;
using GHPC.Camera;
using GHPC.Equipment.Optics;
using MelonLoader;
using UnityEngine;

namespace Leopard2A7V;

public class LpSightView : MonoBehaviour
{
	private Transform _unitRoot;

	private Transform[] _art = Array.Empty<Transform>();

	private readonly Dictionary<CameraSlot, bool> _logged = new Dictionary<CameraSlot, bool>();

	private Vector3[] _restScales = Array.Empty<Vector3>();

	private bool _warned;

	private bool _artVisible = true;

	private static bool _building;

	public static void Attach(GameObject unitRoot, LpVisual.Assembly art)
	{
		if (unitRoot == null || art == null)
		{
			return;
		}
		LpSightView lpSightView = unitRoot.GetComponent<LpSightView>();
		_building = true;
		try
		{
			if (lpSightView == null)
			{
				lpSightView = unitRoot.AddComponent<LpSightView>();
			}
			lpSightView.Setup(unitRoot.transform, art);
		}
		finally
		{
			_building = false;
		}
	}

	public static void ShiftCommanderSlot(Transform unitRoot)
	{
		Vector3 commanderView = LpConfig.CommanderView;
		if (commanderView == Vector3.zero)
		{
			return;
		}
		List<CameraSlot> list = new List<CameraSlot>();
		CameraSlot[] componentsInChildren = unitRoot.GetComponentsInChildren<CameraSlot>(includeInactive: true);
		foreach (CameraSlot cameraSlot in componentsInChildren)
		{
			if (IsCommanderSlot(cameraSlot.transform, unitRoot))
			{
				list.Add(cameraSlot);
			}
		}
		foreach (CameraSlot item in list)
		{
			if (!IsBelowAny(item.transform, list))
			{
				Transform transform = item.transform;
				transform.position += unitRoot.TransformDirection(commanderView);
				MelonLogger.Msg("[L2A7V] commander view " + PathOf(transform, unitRoot) + " moved by " + $"({commanderView.x:F2},{commanderView.y:F2},{commanderView.z:F2}) m in unit frame");
			}
		}
	}

	public static void AddGunSightZoom(Transform unitRoot)
	{
		CameraSlot[] componentsInChildren = unitRoot.GetComponentsInChildren<CameraSlot>(includeInactive: true);
		foreach (CameraSlot cameraSlot in componentsInChildren)
		{
			if (!cameraSlot.IsExterior && cameraSlot.VisionType == NightVisionType.None && !IsRoofGunSlot(cameraSlot.transform) && (cameraSlot.OtherFovs == null || cameraSlot.OtherFovs.Length == 0))
			{
				float[] values = (cameraSlot.OtherFovs = ZoomLevels(cameraSlot.DefaultFov));
				MelonLogger.Msg("[L2A7V] gunner sight " + PathOf(cameraSlot.transform, unitRoot) + " can now zoom " + string.Format("{0:0.#} / {1} deg", cameraSlot.DefaultFov, string.Join(" / ", values)));
			}
		}
	}

	private static float[] ZoomLevels(float baseFov)
	{
		if (!(baseFov > 8f))
		{
			return new float[1] { baseFov * 0.5f };
		}
		return new float[2]
		{
			baseFov * 0.75f,
			baseFov * 0.5f
		};
	}

	private static bool IsRoofGunSlot(Transform node)
	{
		while (node != null)
		{
			string text = node.name.ToLowerInvariant();
			if (text.Contains("roofgun") || text.Contains("roof gun"))
			{
				return true;
			}
			node = node.parent;
		}
		return false;
	}

	private void Setup(Transform unitRoot, LpVisual.Assembly art)
	{
		_unitRoot = unitRoot;
		List<Transform> list = new List<Transform>();
		Add(list, art.Hull);
		Add(list, art.Turret);
		Add(list, art.Gun);
		Add(list, art.CommanderGear);
		Add(list, art.CommanderSight);
		Add(list, art.RoofGunYawArt);
		Add(list, art.RoofGunPitchArt);
		_art = list.ToArray();
		CacheRestScales();
		ShiftCommanderSlot(unitRoot);
		AddGunSightZoom(unitRoot);
		Apply(CameraSlot.ActiveInstance);
	}

	private static void Add(List<Transform> list, Transform node)
	{
		if (node != null)
		{
			list.Add(node);
		}
	}

	private void OnEnable()
	{
		CameraSlot.ActiveInstanceChanged += HandleSlotChanged;
		if (!_building)
		{
			Recover();
		}
		Apply(CameraSlot.ActiveInstance);
	}

	private void OnDisable()
	{
		CameraSlot.ActiveInstanceChanged -= HandleSlotChanged;
		Apply(null);
	}

	private void Recover()
	{
		if (_unitRoot == null)
		{
			_unitRoot = base.transform;
		}
		int num = 0;
		if (_art != null)
		{
			for (int i = 0; i < _art.Length; i++)
			{
				if (_art[i] != null)
				{
					num++;
				}
			}
		}
		if (num != 0)
		{
			MelonLogger.Msg($"[L2A7V] sight view on {_unitRoot.name}: {num}/{_art.Length} art " + $"pivot(s) carried over from the template, art visible={_artVisible}");
			return;
		}
		List<Transform> list = new List<Transform>();
		Add(list, FindDeep(_unitRoot, "L2A7V_HullPivot"));
		Add(list, FindDeep(_unitRoot, "L2A7V_TurretPivot"));
		Add(list, FindDeep(_unitRoot, "L2A7V_GunPivot"));
		Add(list, FindDeep(_unitRoot, "L2A7V_CupolaPivot"));
		Add(list, FindDeep(_unitRoot, "L2A7V_CmdSightPivot"));
		Add(list, FindDeep(_unitRoot, "RoofGunMount"));
		Add(list, FindDeep(_unitRoot, "RoofGunReceiver"));
		_art = list.ToArray();
		CacheRestScales();
		MelonLogger.Warning("[L2A7V] sight view on " + _unitRoot.name + " arrived with no art pivots, " + $"re-found {_art.Length} of them by name");
	}

	private static Transform FindDeep(Transform node, string name)
	{
		if (node == null)
		{
			return null;
		}
		if (node.name == name)
		{
			return node;
		}
		for (int i = 0; i < node.childCount; i++)
		{
			Transform transform = FindDeep(node.GetChild(i), name);
			if (transform != null)
			{
				return transform;
			}
		}
		return null;
	}

	private void HandleSlotChanged(CameraSlot slot)
	{
		Apply(slot);
	}

	private void Apply(CameraSlot slot)
	{
		int num;
		int num2;
		if (slot != null && _unitRoot != null)
		{
			num = (IsBelow(slot.transform, _unitRoot) ? 1 : 0);
			if (num != 0)
			{
				num2 = ((!slot.IsExterior) ? 1 : 0);
				goto IL_003a;
			}
		}
		else
		{
			num = 0;
		}
		num2 = 0;
		goto IL_003a;
		IL_003a:
		bool flag = (byte)num2 != 0;
		if (_artVisible != !flag)
		{
			_artVisible = !flag;
			if (!_artVisible && !_warned)
			{
				_warned = true;
				MelonLogger.Msg("[L2A7V] clearing the imported art out of the eyepiece");
			}
		}
		Enforce();
		if (num != 0)
		{
			Log(slot, flag);
		}
	}

	private void CacheRestScales()
	{
		if (_restScales.Length != _art.Length)
		{
			_restScales = new Vector3[_art.Length];
		}
		for (int i = 0; i < _art.Length; i++)
		{
			if (_art[i] != null)
			{
				_restScales[i] = RestScale(_art[i]);
			}
		}
	}

	private void Enforce()
	{
		SetVisible(_art, _restScales, _artVisible);
	}

	private static void SetVisible(Transform[] pivots, Vector3[] restScales, bool visible)
	{
		for (int i = 0; i < pivots.Length; i++)
		{
			Transform transform = pivots[i];
			if (!(transform == null))
			{
				Vector3 vector = (visible ? ((restScales != null && i < restScales.Length) ? restScales[i] : RestScale(transform)) : Vector3.zero);
				if (!(transform.localScale == vector))
				{
					transform.localScale = vector;
				}
			}
		}
	}

	private static Vector3 RestScale(Transform pivot)
	{
		Transform parent = pivot.parent;
		if (parent == null)
		{
			return Vector3.one;
		}
		Vector3 lossyScale = parent.lossyScale;
		return new Vector3(Reciprocal(lossyScale.x), Reciprocal(lossyScale.y), Reciprocal(lossyScale.z));
	}

	private static float Reciprocal(float value)
	{
		if (!(Mathf.Abs(value) < 1E-05f))
		{
			return 1f / value;
		}
		return 1f;
	}

	private void Log(CameraSlot slot, bool eyepiece)
	{
		if (!_logged.ContainsKey(slot))
		{
			_logged[slot] = eyepiece;
			Vector3 vector = _unitRoot.InverseTransformPoint(slot.transform.position);
			Camera mainCam = CameraManager.MainCam;
			MelonLogger.Msg("[L2A7V] sight slot " + PathOf(slot.transform, _unitRoot) + " " + $"exterior={slot.IsExterior} fov={slot.DefaultFov:F0} " + $"vision={slot.VisionType} " + "hiddenArt=" + (eyepiece ? "all" : "none") + " " + $"unit-local=({vector.x:F2},{vector.y:F2},{vector.z:F2}) " + $"camMask={((mainCam == null) ? (-1) : mainCam.cullingMask)}");
			// Scene-wide renderer occlusion diagnostics are disabled in normal builds.

		}
	}

	private static bool IsCommanderSlot(Transform node, Transform unitRoot)
	{
		string text = PathOf(node, unitRoot).ToLowerInvariant();
		if (!text.Contains("command"))
		{
			return text.Contains("cupola");
		}
		return true;
	}

	private static bool IsBelowAny(Transform node, List<CameraSlot> others)
	{
		foreach (CameraSlot other in others)
		{
			if ((object)other.transform != node && IsBelow(node, other.transform))
			{
				return true;
			}
		}
		return false;
	}

	private static bool IsBelow(Transform node, Transform ancestor)
	{
		while (node != null)
		{
			if (node == ancestor)
			{
				return true;
			}
			node = node.parent;
		}
		return false;
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
