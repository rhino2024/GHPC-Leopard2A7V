using MelonLoader;
using UnityEngine;

namespace Leopard2A7V;

public class LpDm11Fuse : MonoBehaviour
{
	private const float Step = 0.5f;

	private const float Maximum = 3f;

	private const float HudSeconds = 2f;

	private static LpDm11Fuse _instance;

	private float _fuse;

	private float _hudUntil;

	internal static void Ensure()
	{
		if (!(_instance != null))
		{
			GameObject obj = new GameObject("L2A7V_DM11Fuse");
			Object.DontDestroyOnLoad(obj);
			_instance = obj.AddComponent<LpDm11Fuse>();
			_instance._fuse = 0f;
		}
	}

	private void Update()
	{
		AmmoType dm11Shell = LpWeapons.Dm11Shell;
		if (dm11Shell != null)
		{
			float num = 0f;
			if (Input.GetKeyDown(KeyCode.PageUp))
			{
				num = 1f;
			}
			else if (Input.GetKeyDown(KeyCode.PageDown))
			{
				num = -1f;
			}
			if (num != 0f)
			{
				_fuse = Mathf.Clamp(Mathf.Round((_fuse + num * 0.5f) * 2f) / 2f, 0f, 3f);
				LpWeapons.SetDm11Fuse(_fuse);
				_hudUntil = Time.unscaledTime + 2f;
				float num2 = _fuse * dm11Shell.MuzzleVelocity;
				MelonLogger.Msg("[L2A7V] DM11 fuse: " + ((_fuse <= 0f) ? "impact" : $"{_fuse:0.0} s, about {num2:0} m") + " (Page Up / Page Down)");
			}
		}
	}

	private void OnGUI()
	{
		if (!(Time.unscaledTime > _hudUntil))
		{
			AmmoType dm11Shell = LpWeapons.Dm11Shell;
			if (dm11Shell != null)
			{
				string text = ((_fuse <= 0f) ? "DM11 fuse: IMPACT" : $"DM11 fuse: {_fuse:0.0} s  (~{_fuse * dm11Shell.MuzzleVelocity:0} m)");
				GUIStyle gUIStyle = new GUIStyle(GUI.skin.label)
				{
					fontSize = Mathf.Max(14, Screen.height / 60),
					alignment = TextAnchor.MiddleCenter
				};
				gUIStyle.normal.textColor = new Color(0.85f, 0.95f, 0.75f);
				GUI.Label(new Rect(0f, (float)Screen.height * 0.12f, Screen.width, (float)Screen.height * 0.06f), text, gUIStyle);
			}
		}
	}
}
