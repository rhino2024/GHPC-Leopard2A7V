using GHPC;
using MelonLoader;
using UnityEngine;

namespace Leopard2A7V;

public class LpGunArcWatch : MonoBehaviour
{
	private const float IntervalSeconds = 0.5f;

	private const float ToleranceDegrees = 0.5f;

	private AimablePlatform _platform;

	private float _nextSample;

	private float _elevationLimit;

	private float _depressionLimit;

	private bool _restReported;

	private bool _warned;

	public void Init(AimablePlatform platform)
	{
		_platform = platform;
		_elevationLimit = Mathf.Abs(20f);
		_depressionLimit = 0f - Mathf.Abs(-9f);
	}

	private void Update()
	{
		if (_warned)
		{
			base.enabled = false;
		}
		else if (!(_platform == null) && !(_platform.Transform == null) && !(Time.time < _nextSample))
		{
			_nextSample = Time.time + 0.5f;
			float currentEulerAngle = _platform.GetCurrentEulerAngle();
			float num = WorldElevation(_platform.Transform.forward);
			float num2 = WorldElevation(base.transform.forward);
			float num3 = num - num2;
			if (!_restReported)
			{
				_restReported = true;
				MelonLogger.Msg("[L2A7V] gun arc baseline on '" + _platform.Name + "': platform reports " + $"{currentEulerAngle:F1} deg while the bone the barrel rides sits {num3:F1} deg off " + $"the hull (bone {num:F1}, hull {num2:F1}) - if those two disagree, " + "the arc is measured from a different zero than the art's rest pose");
			}
			bool flag = currentEulerAngle > _elevationLimit + 0.5f || currentEulerAngle < _depressionLimit - 0.5f;
			bool flag2 = num3 > _elevationLimit + 0.5f || num3 < _depressionLimit - 0.5f;
			if ((flag || flag2) && !_warned)
			{
				_warned = true;
				MelonLogger.Warning("[L2A7V] gun arc check on '" + _platform.Name + "': platform reports " + $"{currentEulerAngle:F1} deg, barrel {_elevationLimit:F0}/{_depressionLimit:F0} " + $"| barrel {num:F1} deg above horizon, hull {num2:F1}, " + $"so turret-relative {num3:F1} | clamp bypassed: {flag}, " + $"art outside arc: {flag2}");
			}
		}
	}

	private static float WorldElevation(Vector3 forward)
	{
		return Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f)) * 57.29578f;
	}
}
