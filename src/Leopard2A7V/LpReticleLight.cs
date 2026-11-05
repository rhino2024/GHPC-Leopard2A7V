using GHPC.Camera;
using GHPC.Equipment.Optics;
using Reticle;
using UnityEngine;

namespace Leopard2A7V;

public class LpReticleLight : MonoBehaviour
{
	private ReticleMesh _mesh;

	private ReticleTree.Light.Type _type = ReticleTree.Light.Type.NightIllumination;

	private bool _observed;

	private bool _last;

	private UsableOptic _optic;

	private Renderer _renderer;

	private bool _active;

	public static void Attach(ReticleMesh mesh)
	{
		if (!(mesh == null) && !(mesh.GetComponent<LpReticleLight>() != null))
		{
			LpReticleLight lpReticleLight = mesh.gameObject.AddComponent<LpReticleLight>();
			lpReticleLight._mesh = mesh;
			lpReticleLight._optic = mesh.GetComponentInParent<UsableOptic>(includeInactive: true);
		lpReticleLight._renderer = mesh.GetComponent<Renderer>();
		lpReticleLight.HandleSlotChanged(CameraSlot.ActiveInstance);
	}
	}

	private void OnEnable()
	{
		if (_optic == null)
		{
			_optic = GetComponentInParent<UsableOptic>(includeInactive: true);
		}
		if (_renderer == null)
		{
			_renderer = GetComponent<Renderer>();
		}
		CameraSlot.ActiveInstanceChanged += HandleSlotChanged;
		HandleSlotChanged(CameraSlot.ActiveInstance);
	}

	private void OnDisable()
	{
		CameraSlot.ActiveInstanceChanged -= HandleSlotChanged;
		_active = false;
	}

	private void HandleSlotChanged(CameraSlot slot)
	{
		_active = _mesh != null && slot != null && slot.VisionType != NightVisionType.Thermal && _optic != null && (object)_optic.slot == slot;
		if (_active)
		{
			Show();
		}
	}

	private void Update()
	{
		if (_mesh == null)
		{
			Object.Destroy(this);
			return;
		}
		if (!_active)
		{
			return;
		}
		bool reticleIlluminated = PlayerControl.ReticleIlluminated;
		if (!_observed)
		{
			_observed = true;
			_last = reticleIlluminated;
		}
		else if (reticleIlluminated != _last)
		{
			_last = reticleIlluminated;
			_mesh.GetLight(_type, out var value);
			if (!float.IsNaN(value))
			{
				_mesh.SetLight(_type, reticleIlluminated ? 1f : 0f);
			}
		}
	}

	private void Show()
	{
		if (!_mesh.gameObject.activeSelf)
		{
			_mesh.gameObject.SetActive(value: true);
		}
		if (_renderer != null && !_renderer.enabled)
		{
			_renderer.enabled = true;
		}
	}
}
