using System;
using System.Reflection;
using GHPC;
using GHPC.Camera;
using GHPC.Crew;
using GHPC.Player;
using GHPC.Weapons;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace Leopard2A7V;

public class LpRoofGunView : MonoBehaviour
{
	private const string Tag = "[L2A7V][roof-gun]";

	private static readonly MethodInfo AttachCamSlot = AccessTools.Method(typeof(CameraManager), "AttachCamToSlot", new Type[1] { typeof(CameraSlot) });

	internal WeaponSystem Weapon;

	internal CameraSlot Sight;

	private CameraSlot _return;

	private bool _shown;

	private CameraSlot _activeSlot;

	private void OnEnable()
	{
		CameraSlot.ActiveInstanceChanged += HandleSlotChanged;
		HandleSlotChanged(CameraSlot.ActiveInstance);
	}

	private void OnDisable()
	{
		CameraSlot.ActiveInstanceChanged -= HandleSlotChanged;
		_activeSlot = null;
		if (_shown)
		{
			_shown = false;
			RestoreView();
		}
	}

	private void HandleSlotChanged(CameraSlot slot)
	{
		_activeSlot = ((slot != null && slot.transform.IsChildOf(base.transform)) ? slot : null);
		if (_shown && slot != Sight)
		{
			_shown = false;
			_return = null;
		}
	}

	internal static void Attach(GameObject unitRoot, WeaponSystem weapon, CameraSlot sight)
	{
		if (!(unitRoot == null) && !(weapon == null) && !(sight == null))
		{
			LpRoofGunView lpRoofGunView = unitRoot.GetComponent<LpRoofGunView>();
			if (lpRoofGunView == null)
			{
				lpRoofGunView = unitRoot.AddComponent<LpRoofGunView>();
			}
			lpRoofGunView.Weapon = weapon;
			lpRoofGunView.Sight = sight;
		}
	}

	private void Update()
	{
		if (Sight == null || Weapon == null || (_activeSlot == null && !_shown))
		{
			return;
		}
		PlayerInput instance = PlayerInput.Instance;
		if (instance == null)
		{
			return;
		}
		if (_activeSlot != null && Selected(instance))
		{
			if (!_shown)
			{
				if (!(CameraSlot.ActiveInstance == Sight))
				{
					_return = _activeSlot;
					Move(instance, Sight);
				}
				_shown = CameraSlot.ActiveInstance == Sight;
				if (!_shown)
				{
					_return = null;
				}
				else
				{
					MelonLogger.Msg("[L2A7V][roof-gun] MG3 selected: view moved to its sight");
				}
			}
		}
		else if (_shown)
		{
			_shown = false;
			RestoreView();
		}
	}

	private void RestoreView()
	{
		CameraSlot activeInstance = CameraSlot.ActiveInstance;
		if (activeInstance != Sight && activeInstance != null)
		{
			_return = null;
			return;
		}
		CameraSlot cameraSlot = ((_return != null && _return.IsAvailable) ? _return : null);
		_return = null;
		Move(PlayerInput.Instance, cameraSlot);
		MelonLogger.Msg("[L2A7V][roof-gun] MG3 deselected: view returned" + ((cameraSlot == null) ? " to the exterior camera" : (" to " + cameraSlot.name)));
	}

	private bool Selected(PlayerInput input)
	{
		ICrewBrain currentPlayerCrewBrain = input.CurrentPlayerCrewBrain;
		if (currentPlayerCrewBrain == null || currentPlayerCrewBrain.WeaponsModule == null)
		{
			return false;
		}
		WeaponSystemInfo activeWeapon = currentPlayerCrewBrain.WeaponsModule.ActiveWeapon;
		if (activeWeapon != null)
		{
			return (object)activeWeapon.Weapon == Weapon;
		}
		return false;
	}

	private static void Move(PlayerInput input, CameraSlot slot)
	{
		CameraManager instance = CameraManager.Instance;
		if (instance == null)
		{
			return;
		}
		try
		{
			if (slot != null && slot.IsLinkedNightSight)
			{
				instance.TempAttachToSlot(slot);
			}
			else if (AttachCamSlot != null)
			{
				AttachCamSlot.Invoke(instance, new object[1] { slot });
			}
		}
		catch (Exception ex)
		{
			MelonLogger.Warning("[L2A7V][roof-gun] could not move the view: " + ex.Message);
		}
	}
}
