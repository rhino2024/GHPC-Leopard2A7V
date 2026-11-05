using MelonLoader;
using UnityEngine;

namespace Leopard2A7V;

public class LpConvertBehaviour : MonoBehaviour
{
	private const float TimeoutSeconds = 60f;

	private const float RetrySeconds = 0.25f;

	private float _elapsed;

	private float _nextAttempt;

	private bool _loggedWaiting;

	private void Update()
	{
		_elapsed += Time.deltaTime;
		if (Time.unscaledTime < _nextAttempt)
		{
			return;
		}
		_nextAttempt = Time.unscaledTime + 0.25f;
		if (ObjModel.Failed)
		{
			MelonLogger.Error("[L2A7V] giving up on the visual swap: " + ObjModel.Error);
			Object.Destroy(this);
		}
		else if (FindDeep(base.transform, "Leopard2A7V_Visual") != null)
		{
			Object.Destroy(this);
		}
		else if (ObjModel.Loaded && (LpXrayParts.Loaded || LpXrayParts.Failed))
		{
			LpBaseRig.SnapshotDonor(base.transform);
			if (LpVisual.Convert(base.gameObject) != null)
			{
				Object.Destroy(this);
			}
			else if (_elapsed > 60f)
			{
				MelonLogger.Error("[L2A7V] visual swap timed out, the donor look is still in place");
				Object.Destroy(this);
			}
			else if (!_loggedWaiting && _elapsed > 1f)
			{
				_loggedWaiting = true;
				MelonLogger.Msg("[L2A7V] waiting for the model to finish parsing");
			}
		}
	}

	private static Transform FindDeep(Transform root, string name)
	{
		if (root.name == name)
		{
			return root;
		}
		foreach (Transform item in root)
		{
			Transform transform = FindDeep(item, name);
			if (transform != null)
			{
				return transform;
			}
		}
		return null;
	}
}
