using System;
using MelonLoader;
using UnityEngine;

namespace Leopard2A7V;

// Donor weapons, VehicleInfo and imported art are not necessarily ready during
// the spawner postfix. Retry each stage independently for a bounded period,
// then leave this component dormant instead of rescanning forever.
public sealed class LpLateInit : MonoBehaviour
{
	private const float RetrySeconds = 0.25f;
	private const float TimeoutSeconds = 90f;

	private float _started;
	private float _next;
	private bool _weapons;
	private bool _modules;
	private bool _interior;
	private bool _armour;
	private bool _wiring;
	private bool _hatches;
	private bool _smoke;
	private bool _finished;

	private void Start()
	{
		_started = Time.unscaledTime;
		_next = _started + 0.5f;
	}

	private void Update()
	{
		if (_finished || Time.unscaledTime < _next)
		{
			return;
		}
		_next = Time.unscaledTime + RetrySeconds;
		TryStep("weapons", ref _weapons, () => LpWeapons.Apply(base.gameObject));
		TryStep("modules", ref _modules, () => LpModuleBuilder.Build(base.gameObject));
		TryStep("interior", ref _interior, () => LpInteriorSwap.Apply(base.gameObject));
		TryStep("armour", ref _armour, () => LpArmourBuilder.Build(base.gameObject));
		TryStep("hatches", ref _hatches, () => LpHatches.Bind(base.gameObject));
		TryStep("smoke", ref _smoke, () => LpSmoke.Apply(base.gameObject));
		if (_weapons && _armour && _modules && _interior && _hatches && _smoke)
		{
			TryStep("module wiring", ref _wiring, () => LpModuleBuilder.Wire(base.gameObject));
		}
		if (_weapons && _armour && _modules && _interior && _wiring && _hatches && _smoke)
		{
			_finished = true;
			base.enabled = false;
			return;
		}
		if (Time.unscaledTime - _started >= TimeoutSeconds)
		{
			_finished = true;
			MelonLogger.Warning("[L2A7V][init] bounded initialization ended on '" + base.name + "': weapons=" + _weapons + ", armour=" + _armour + ", modules=" + _modules + ", interior=" + _interior + ", wiring=" + _wiring + ", hatches=" + _hatches + ", smoke=" + _smoke + ". Unresolved donor stages were left untouched.");
			base.enabled = false;
		}
	}

	private static void TryStep(string name, ref bool done, Func<bool> step)
	{
		if (done)
		{
			return;
		}
		try
		{
			done = step();
		}
		catch (Exception ex)
		{
			MelonLogger.Warning("[L2A7V][init] " + name + " will retry after a donor initialization error: " + ex.Message);
		}
	}
}
