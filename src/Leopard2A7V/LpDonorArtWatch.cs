using System;
using System.Collections.Generic;
using GHPC;
using MelonLoader;
using UnityEngine;

namespace Leopard2A7V;

public class LpDonorArtWatch : MonoBehaviour
{
	private const float IntervalSeconds = 0.5f;

	private const float TimeoutSeconds = 120f;

	private const float RescanSeconds = 2f;

	private const float SettleSeconds = 12f;

	private GameObject _unit;

	private readonly HashSet<Renderer> _hidden = new HashSet<Renderer>();

	private float _elapsed;

	private float _next;

	private float _nextRescan;

	private float _settleUntil;

	private float _nextAarCheck;

	private bool _wasInAar;

	private bool _aarSubscribed;

	private bool _camSubscribed;

	private AarController _aar;

	private bool _gunDone;

	private bool _gearDone;

	private bool _crewDone;

	private bool _writesDone;

	private bool _guardLogged;

	private int _reports;

	private bool _sampled;

	private int _collapsedReported;

	private void Awake()
	{
		_unit = base.gameObject;
	}

	internal void Adopt()
	{
		_unit = base.gameObject;
		_hidden.Clear();
		_settleUntil = Time.unscaledTime + SettleSeconds;
		_wasInAar = AarController.InAar;
		Renderer[] donorSnapshot = LpBaseRig.DonorSnapshot;
		if (donorSnapshot != null)
		{
			Renderer[] array = donorSnapshot;
			foreach (Renderer renderer in array)
			{
				if (renderer != null && !LpVisual.IsImportedArt(renderer.transform))
				{
					_hidden.Add(renderer);
				}
			}
		}
		Sweep();
		Rehide();
		Report();
		MelonLogger.Msg("[L2A7V] guard attached to " + base.gameObject.name + " after conversion: " + _hidden.Count + " donor renderer(s) kept out of the picture");
	}

	private int Sweep()
	{
		if (_unit == null)
		{
			_unit = base.gameObject;
		}
		int num = 0;
		Renderer[] componentsInChildren = _unit.GetComponentsInChildren<Renderer>(includeInactive: true);
		foreach (Renderer renderer in componentsInChildren)
		{
			if (!(renderer == null) && !(renderer is ParticleSystemRenderer) && !LpVisual.IsImportedArt(renderer.transform) && renderer.gameObject.layer != 24 && LpBaseRig.ShouldHide(renderer.transform, renderer.name) && _hidden.Add(renderer))
			{
				num++;
				try
				{
					LpBaseRig.StripGeometry(renderer);
				}
				catch (Exception ex)
				{
					MelonLogger.Warning("[L2A7V] could not strip '" + LpBaseRig.PathOf(renderer.transform) + "': " + ex.Message);
				}
			}
		}
		if (num > 0)
		{
			MelonLogger.Msg("[L2A7V] donor art: " + num + " renderer(s) added to the hide list, " + _hidden.Count + " on it now");
		}
		return num;
	}

	private void Rehide()
	{
		int num = 0;
		foreach (Renderer item in _hidden)
		{
			if (!(item == null) && item.enabled)
			{
				item.enabled = false;
				num++;
			}
		}
		if (num > 0 && !_guardLogged)
		{
			_guardLogged = true;
			MelonLogger.Msg("[L2A7V] guard switched " + num + " donor renderer(s) back off");
		}
	}

	private void Report()
	{
		if (_unit == null)
		{
			return;
		}
		int num = 0;
		int num2 = 0;
		Renderer[] componentsInChildren = _unit.GetComponentsInChildren<Renderer>(includeInactive: true);
		foreach (Renderer renderer in componentsInChildren)
		{
			if (!(renderer == null) && renderer.enabled && !(renderer is ParticleSystemRenderer) && !LpVisual.IsImportedArt(renderer.transform) && renderer.gameObject.layer != 24 && LpBaseRig.ShouldHide(renderer.transform, renderer.name))
			{
				num2++;
				if (num++ < 12)
				{
					MelonLogger.Warning("[L2A7V] still drawing: " + LpBaseRig.PathOf(renderer.transform));
				}
			}
		}
		if (num2 > 0 && _reports < 5)
		{
			_reports++;
			MelonLogger.Warning("[L2A7V] donor renderers still drawing: " + num2);
		}
		else if (num2 == 0 && _reports == 0)
		{
			_reports = 1;
			MelonLogger.Msg("[L2A7V] donor renderers still drawing: 0");
		}
		int num3 = 0;
		UniformArmor[] componentsInChildren2 = _unit.GetComponentsInChildren<UniformArmor>(includeInactive: true);
		foreach (UniformArmor uniformArmor in componentsInChildren2)
		{
			if (!(uniformArmor == null) && uniformArmor.gameObject.name.StartsWith("L2A7V_XrayArmor", StringComparison.Ordinal))
			{
				Vector3 lossyScale = uniformArmor.transform.lossyScale;
				if (Mathf.Abs(lossyScale.x) < 0.05f || Mathf.Abs(lossyScale.y) < 0.05f || Mathf.Abs(lossyScale.z) < 0.05f)
				{
					num3++;
				}
			}
		}
		if (num3 > 0 && _collapsedReported < 3)
		{
			_collapsedReported++;
			MelonLogger.Warning("[L2A7V] " + num3 + " armour piece(s) sit under a collapsed transform and cannot be hit; check what was scaled to zero above them");
		}
		if (_sampled)
		{
			return;
		}
		componentsInChildren2 = _unit.GetComponentsInChildren<UniformArmor>(includeInactive: true);
		foreach (UniformArmor uniformArmor2 in componentsInChildren2)
		{
			if (!(uniformArmor2 == null) && uniformArmor2.gameObject.name.StartsWith("L2A7V_XrayArmor", StringComparison.Ordinal))
			{
				Collider component = uniformArmor2.GetComponent<Collider>();
				_sampled = true;
				MelonLogger.Msg("[L2A7V] armour sample: '" + uniformArmor2.Name + "' world " + ((component != null) ? (component.bounds.center.ToString("0.0") + " size " + component.bounds.size.ToString("0.00") + " enabled " + component.enabled) : "no collider"));
				break;
			}
		}
	}

	internal static void SetConverted(GameObject unit)
	{
		if (!(unit == null))
		{
			LpDonorArtWatch lpDonorArtWatch = unit.GetComponent<LpDonorArtWatch>();
			if (lpDonorArtWatch == null)
			{
				lpDonorArtWatch = unit.AddComponent<LpDonorArtWatch>();
			}
			lpDonorArtWatch.Adopt();
		}
	}

	private void OnEnable()
	{
		TrySubscribeAar();
	}

	private void OnDisable()
	{
		UnsubscribeAar();
	}

	private void TrySubscribeAar()
	{
		if (_aarSubscribed)
		{
			return;
		}
		_aar = AarController.Instance;
		if (_aar != null)
		{
			_aar.AarStarted += HandleAarStarted;
			_aarSubscribed = true;
			_wasInAar = AarController.InAar;
		}
		if (ConstantsAndInfoManager.PlayerCam != null && !_camSubscribed)
		{
			ConstantsAndInfoManager.PlayerCam.UpdateAarCam += HandleAarCam;
			_camSubscribed = true;
		}
		if (!_camSubscribed)
		{
			_aarSubscribed = false;
		}
	}

	private void UnsubscribeAar()
	{
		if (_aar != null && _aarSubscribed)
		{
			_aar.AarStarted -= HandleAarStarted;
		}
		if (ConstantsAndInfoManager.PlayerCam != null)
		{
			ConstantsAndInfoManager.PlayerCam.UpdateAarCam -= HandleAarCam;
		}
		_aar = null;
		_aarSubscribed = false;
		_camSubscribed = false;
	}

	private void HandleAarStarted()
	{
		_wasInAar = true;
	}

	private void HandleAarCam(bool active)
	{
		if (!active && _wasInAar)
		{
			_wasInAar = false;
			_settleUntil = Time.unscaledTime + 2f;
			_nextRescan = 0f;
		}
	}

	private void Update()
	{
		if (!_aarSubscribed && Time.unscaledTime >= _nextAarCheck)
		{
			_nextAarCheck = Time.unscaledTime + 1f;
			TrySubscribeAar();
		}
		if (_writesDone && Time.unscaledTime >= _settleUntil)
		{
			return;
		}
		_elapsed += Time.deltaTime;
		if (Time.unscaledTime < _next)
		{
			return;
		}
		_next = Time.unscaledTime + 0.5f;
		if (!_gunDone)
		{
			_gunDone = LpWeapons.ApplyGunList(base.gameObject);
		}
		if (!_crewDone)
		{
			_crewDone = LpDonorArt.HideDonorCrew(base.gameObject);
		}
		if (!_gearDone)
		{
			_gearDone = LpRunningGear.Ensure(base.transform);
		}
		if (!_gunDone || !_crewDone || !_gearDone)
		{
			if (_elapsed < 120f)
			{
				return;
			}
			MelonLogger.Warning("[L2A7V] stopped waiting for the donor's art pass after " + 120f + " s (gun list " + _gunDone + ", crew figures " + _crewDone + ", running gear " + _gearDone + ")");
		}
		_writesDone = true;
	}

	private void LateUpdate()
	{
		if (_writesDone && Time.unscaledTime >= _settleUntil)
		{
			return;
		}
		if (AarController.InAar)
		{
			_wasInAar = true;
			return;
		}
		Rehide();
		if (!(Time.unscaledTime < _nextRescan))
		{
			_nextRescan = Time.unscaledTime + 2f;
			if (Sweep() > 0)
			{
				Rehide();
			}
			Report();
		}
	}
}
