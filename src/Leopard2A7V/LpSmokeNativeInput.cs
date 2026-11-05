using GHPC;
using GHPC.Camera;
using GHPC.Equipment;
using GHPC.Player;
using UnityEngine;

namespace Leopard2A7V;

// PlayerInput.SetupUnitReferences normally binds the real manager before
// PlayerUnitChangedByUnit fires. Repair it only if the donor was not ready in
// time. Unit ownership, not the active camera, determines the native reference.
// GHPC remains solely responsible for initialization and the smoke key.
public sealed class LpSmokeNativeInput : MonoBehaviour
{
	private VehicleSmokeManager _manager;
	private PlayerInput _input;
	private bool _inputSubscribed;
	private float _nextAttempt;

	internal static void Ensure(GameObject root, VehicleSmokeManager manager)
	{
		LpSmokeNativeInput component = root.GetComponent<LpSmokeNativeInput>();
		if (component == null)
		{
			component = root.AddComponent<LpSmokeNativeInput>();
		}
		component._manager = manager;
		component.TrySubscribe();
		component.TryBind();
	}

	private void OnEnable()
	{
		CameraSlot.ActiveInstanceChanged += HandleSlotChanged;
		_nextAttempt = 0f;
		TrySubscribe();
		TryBind();
	}

	private void OnDisable()
	{
		CameraSlot.ActiveInstanceChanged -= HandleSlotChanged;
		Unsubscribe();
		LpSmoke.ReleasePlayerInput(_manager, base.gameObject);
	}

	private void Update()
	{
		if (Time.unscaledTime < _nextAttempt)
		{
			return;
		}
		_nextAttempt = Time.unscaledTime + 1f;
		TrySubscribe();
		// Bounded, ownership-checked reconciliation also covers a component that
		// was created after the unit event. It never restores another unit's value.
		TryBind();
	}

	private void TrySubscribe()
	{
		if (_inputSubscribed)
		{
			return;
		}
		_input = PlayerInput.Instance;
		if (_input != null)
		{
			_input.PlayerUnitChangedByUnit += HandleUnitChanged;
			_inputSubscribed = true;
		}
	}

	private void Unsubscribe()
	{
		if (_input != null && _inputSubscribed)
		{
			_input.PlayerUnitChangedByUnit -= HandleUnitChanged;
		}
		_inputSubscribed = false;
		_input = null;
	}

	private void TryBind()
	{
		if (_manager != null && _input != null)
		{
			LpSmoke.WirePlayerInput(_manager, base.gameObject);
		}
	}

	private void HandleUnitChanged(Unit unit)
	{
		if (unit != null && unit.gameObject == base.gameObject)
		{
			TryBind();
		}
		else
		{
			LpSmoke.ReleasePlayerInput(_manager, base.gameObject);
		}
	}

	private void HandleSlotChanged(CameraSlot slot)
	{
		TrySubscribe();
		TryBind();
	}
}
