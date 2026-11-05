using GHPC;
using GHPC.Camera;
using MelonLoader;
using UnityEngine;

namespace Leopard2A7V;

public class LpCommanderSightRig : MonoBehaviour
{
	internal CameraSlot Night;

	internal Transform Head;

	internal CameraSlot Commander;

	private bool _mounted;

	private bool _wasActive;

	private float _nextLook;

	private CameraSlot _activeSlot;

	private void OnEnable()
	{
		CameraSlot.ActiveInstanceChanged += HandleSlotChanged;
		HandleSlotChanged(CameraSlot.ActiveInstance);
	}

	private void OnDisable()
	{
		CameraSlot.ActiveInstanceChanged -= HandleSlotChanged;
		if (_activeSlot == Night)
		{
			ReturnToCommander();
		}
		_activeSlot = null;
		_wasActive = false;
	}

	private void HandleSlotChanged(CameraSlot slot)
	{
		_activeSlot = ((slot != null && slot.transform.IsChildOf(base.transform)) ? slot : null);
	}

	private void Update()
	{
		if (Night == null)
		{
			return;
		}
		if (!_mounted)
		{
			if (!(Time.unscaledTime < _nextLook))
			{
				_nextLook = Time.unscaledTime + 0.5f;
				if (Head == null)
				{
					Head = LpCommanderSight.FindHead(base.gameObject);
				}
				if (!(Head == null))
				{
					LpCommanderNvg.AttachToHead(Night, Head);
					_mounted = true;
					MelonLogger.Msg("[L2A7V][citv] commander sight mounted on '" + Head.name + "'");
				}
			}
			return;
		}
		if (_activeSlot == null || AarController.InAar || !Input.GetKeyDown(KeyCode.T))
		{
			return;
		}
		CameraManager instance = CameraManager.Instance;
		if (instance == null)
		{
			return;
		}
		if (_activeSlot == Night)
		{
			ReturnToCommander();
		}
		else if (_activeSlot == Commander)
		{
			if (!Night.IsAvailable)
			{
				MelonLogger.Warning("[L2A7V][citv] CITV unavailable; commander view retained");
				return;
			}
			instance.TempAttachToSlot(Night);
			if (CameraSlot.ActiveInstance != Night)
			{
				MelonLogger.Warning("[L2A7V][citv] game refused the CITV slot; commander view retained");
				return;
			}
			LpCitv.RefreshActiveSlot(Night);
			MelonLogger.Msg("[L2A7V][citv] CITV opened from the commander (T); 15/10/4 deg thermal, no scan");
		}
	}

	private bool ReturnToCommander()
	{
		CameraManager instance = CameraManager.Instance;
		if (instance == null)
		{
			return false;
		}
		if (Commander != null && Commander.IsAvailable)
		{
			instance.TempAttachToSlot(Commander);
			if (CameraSlot.ActiveInstance == Commander)
			{
				MelonLogger.Msg("[L2A7V][citv] CITV closed (T), returned to the saved commander slot");
				return true;
			}
		}
		instance.ResetTempAttach();
		bool flag = CameraSlot.ActiveInstance != Night;
		MelonLogger.Warning("[L2A7V][citv] saved commander slot unavailable; used the game camera fallback, leftCITV=" + flag);
		return flag;
	}

	private void LateUpdate()
	{
		if (Night == null || Head == null || _activeSlot != Night)
		{
			_wasActive = false;
			return;
		}
		Night.transform.position = Head.position;
		Camera mainCam = CameraManager.MainCam;
		if (mainCam == null)
		{
			return;
		}
		Transform parent = Head.parent;
		Vector3 forward = mainCam.transform.forward;
		Vector3 vector = ((parent != null) ? parent.InverseTransformDirection(forward) : forward);
		if (vector.sqrMagnitude < 1E-06f)
		{
			return;
		}
		float y = Mathf.Atan2(vector.x, vector.z) * 57.29578f;
		float value = Mathf.Asin(Mathf.Clamp(vector.y, -1f, 1f)) * 57.29578f;
		float t = (_wasActive ? Mathf.Clamp01(Time.deltaTime * 14f) : 1f);
		_wasActive = true;
		Head.localRotation = Quaternion.Slerp(Head.localRotation, Quaternion.Euler(0f - Mathf.Clamp(value, -12f, 12f), y, 0f), t);
		Night.transform.rotation = mainCam.transform.rotation;
	}
}
