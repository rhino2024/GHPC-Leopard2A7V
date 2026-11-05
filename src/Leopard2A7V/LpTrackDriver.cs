using MelonLoader;
using NWH.VehiclePhysics;
using UnityEngine;

namespace Leopard2A7V;

public class LpTrackDriver : MonoBehaviour
{
	private Tracks _native;

	private Material _left;

	private Material _right;

	private float _leftUnits;

	private float _rightUnits;

	private float _leftPhase;

	private float _rightPhase;

	private float _leftApplied = float.NaN;

	private float _rightApplied = float.NaN;

	private bool _logged;

	public void SetNative(Tracks native)
	{
		_native = native;
	}

	public void SetSide(bool isLeft, Material material, float scrollUnitsPerMetre)
	{
		if (isLeft)
		{
			_left = material;
			_leftUnits = scrollUnitsPerMetre;
		}
		else
		{
			_right = material;
			_rightUnits = scrollUnitsPerMetre;
		}
	}

	private void LateUpdate()
	{
		if (_native == null || (_left == null && _right == null))
		{
			return;
		}
		float deltaTime = Time.deltaTime;
		if (!(deltaTime <= 0f) && !(deltaTime > 1f))
		{
			if (_left != null)
			{
				_leftPhase = Advance(_leftPhase, _native.LeftTrackVelocity, _leftUnits, deltaTime);
				if (float.IsNaN(_leftApplied) || Mathf.Abs(_leftPhase - _leftApplied) > 0.00001f)
				{
					_leftApplied = _leftPhase;
					_left.mainTextureOffset = new Vector2(0f, _leftPhase);
				}
			}
			if (_right != null)
			{
				_rightPhase = Advance(_rightPhase, _native.RightTrackVelocity, _rightUnits, deltaTime);
				if (float.IsNaN(_rightApplied) || Mathf.Abs(_rightPhase - _rightApplied) > 0.00001f)
				{
					_rightApplied = _rightPhase;
					_right.mainTextureOffset = new Vector2(0f, _rightPhase);
				}
			}
			if (!_logged)
			{
				_logged = true;
				MelonLogger.Msg("[L2A7V][track] driver running, track velocity " + $"{_native.LeftTrackVelocity:0.00}/{_native.RightTrackVelocity:0.00} m/s at " + $"{_native.wheelEnlargementCoefficient:0.#}x wheel size");
			}
		}
	}

	private static float Advance(float phase, float velocity, float units, float dt)
	{
		if (float.IsNaN(velocity) || float.IsInfinity(velocity))
		{
			return phase;
		}
		float num = phase + velocity * dt * units;
		return num - Mathf.Floor(num);
	}
}
