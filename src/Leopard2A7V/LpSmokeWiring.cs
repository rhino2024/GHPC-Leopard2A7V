using System;
using System.Reflection;
using GHPC;
using GHPC.Camera;
using GHPC.Equipment;
using GHPC.Player;
using GHPC.Utility;
using MelonLoader;
using UnityEngine;

namespace Leopard2A7V;

public class LpSmokeWiring : MonoBehaviour
{
	private static readonly BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

	private VehicleSmokeManager _manager;

	private bool _adopted;

	private float _next;

	private static PropertyInfo _mainPlayer;

	private static MethodInfo _buttonDown;

	private static readonly object[] SmokeButtonArgs = new object[1] { "Smoke Grenades" };

	private bool _vehicleActive;

	private void OnEnable()
	{
		CameraSlot.ActiveInstanceChanged += HandleSlotChanged;
		HandleSlotChanged(CameraSlot.ActiveInstance);
	}

	private void OnDisable()
	{
		CameraSlot.ActiveInstanceChanged -= HandleSlotChanged;
		_vehicleActive = false;
	}

	private void HandleSlotChanged(CameraSlot slot)
	{
		bool flag = slot != null && slot.transform.IsChildOf(base.transform);
		if (_vehicleActive != flag)
		{
			_vehicleActive = flag;
			_adopted = false;
		}
	}

	private void Update()
	{
		try
		{
			Tick();
		}
		catch (Exception ex)
		{
			MelonLogger.Warning("[L2A7V][smoke] smoke key handler stopped: " + ex.Message);
			base.enabled = false;
		}
	}

	private void Tick()
	{
		if (!_vehicleActive)
		{
			return;
		}
		PlayerInput input = PlayerInput.Instance;
		if (input == null)
		{
			return;
		}
		if (_manager == null)
		{
			_manager = GetComponentInChildren<VehicleSmokeManager>(includeInactive: true);
			if (_manager == null)
			{
				return;
			}
		}
		if (!_adopted)
		{
			if (Time.unscaledTime < _next)
			{
				return;
			}
			_next = Time.unscaledTime + 0.5f;
			if (!Adopt(input))
			{
				return;
			}
		}
		if (Pressed())
		{
			Fire();
		}
	}

	private bool IsPlayerVehicle(PlayerInput input)
	{
		if (input == null)
		{
			return false;
		}
		Unit unit = LpSmoke.CurrentPlayerUnit(input);
		return unit != null && unit.gameObject == base.gameObject;
	}

	private bool Adopt(PlayerInput instance)
	{
		if (!IsPlayerVehicle(instance))
		{
			return false;
		}
		FieldInfo field = instance.GetType().GetField("_smokeLauncherManager", Private);
		object obj = ((field == null) ? null : field.GetValue(instance));
		if (field != null && obj != _manager)
		{
			_adopted = LpSmoke.WirePlayerInput(_manager, base.gameObject);
			return _adopted;
		}
		_adopted = true;
		MelonLogger.Msg("[L2A7V][smoke] player launcher ownership verified; original smoke input left intact");
		return true;
	}

	private static bool Pressed()
	{
		try
		{
			if (_mainPlayer == null)
			{
				_mainPlayer = typeof(InputUtil).GetProperty("MainPlayer", BindingFlags.Static | BindingFlags.Public);
			}
			object obj = ((_mainPlayer == null) ? null : _mainPlayer.GetValue(null));
			if (obj == null)
			{
				return Input.GetKeyDown(KeyCode.G);
			}
			if (_buttonDown == null)
			{
				_buttonDown = obj.GetType().GetMethod("GetButtonDown", new Type[1] { typeof(string) });
			}
			if (_buttonDown == null)
			{
				return Input.GetKeyDown(KeyCode.G);
			}
			return (bool)_buttonDown.Invoke(obj, SmokeButtonArgs);
		}
		catch (Exception)
		{
			return Input.GetKeyDown(KeyCode.G);
		}
	}

	private void Fire()
	{
		if (!_manager.IsInitialized || !_manager.HasSmokeLauncher)
		{
			MelonLogger.Msg("[L2A7V][smoke] launcher is not ready; leaving the key press to the game's normal input path");
			return;
		}
		int num = ((_manager.SmokeGroups != null) ? _manager.SmokeGroups.Length : 0);
		MelonLogger.Msg("[L2A7V][smoke] smoke key: salvos=" + num + " rounds=" + Rounds());
		try
		{
			_manager.FireSmokes(-1, useRound: true, force: false);
		}
		catch (Exception ex)
		{
			MelonLogger.Error("[L2A7V][smoke] FireSmokes threw: " + ex);
		}
	}

	private int Rounds()
	{
		FieldInfo field = typeof(VehicleSmokeManager).GetField("_smokeSlots", Private);
		Array array = ((field == null) ? null : (field.GetValue(_manager) as Array));
		if (array == null)
		{
			return 0;
		}
		int num = 0;
		foreach (object item in array)
		{
			if (item != null)
			{
				FieldInfo field2 = item.GetType().GetField("Rounds");
				if (field2 != null)
				{
					num += (int)field2.GetValue(item);
				}
			}
		}
		return num;
	}
}
