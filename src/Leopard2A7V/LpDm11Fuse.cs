using MelonLoader;
using UnityEngine;

namespace Leopard2A7V
{
    /// <summary>
    /// Sets the DM11's airburst before it is fired.
    ///
    /// The real round is programmed by the fire control: the gunner dials a distance and the fuze is set to
    /// burst there. GHPC has no such control - the only thing an <see cref="AmmoType"/> exposes is a fixed
    /// <c>RangedFuseTime</c>, a countdown started at the muzzle - so the programming is done here instead:
    /// Page Up and Page Down move the fuse time, the shell is updated immediately, and the value is shown on
    /// screen with the distance it works out to at the round's muzzle velocity.
    /// </summary>
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
            if (_instance != null) return;
            var host = new GameObject("L2A7V_DM11Fuse");
            Object.DontDestroyOnLoad(host);
            _instance = host.AddComponent<LpDm11Fuse>();
            _instance._fuse = 0f;
        }

        private void Update()
        {
            AmmoType shell = LpWeapons.Dm11Shell;
            if (shell == null) return;

            float direction = 0f;
            if (Input.GetKeyDown(KeyCode.PageUp)) direction = 1f;
            else if (Input.GetKeyDown(KeyCode.PageDown)) direction = -1f;
            if (direction == 0f) return;

            _fuse = Mathf.Clamp(Mathf.Round((_fuse + direction * Step) * 2f) / 2f, 0f, Maximum);
            LpWeapons.SetDm11Fuse(_fuse);
            _hudUntil = Time.unscaledTime + HudSeconds;

            float range = _fuse * shell.MuzzleVelocity;
            MelonLogger.Msg("[L2A7V] DM11 fuse: " + (_fuse <= 0f
                ? "impact"
                : $"{_fuse:0.0} s, about {range:0} m") + " (Page Up / Page Down)");
        }

        private void OnGUI()
        {
            if (Time.unscaledTime > _hudUntil) return;
            AmmoType shell = LpWeapons.Dm11Shell;
            if (shell == null) return;

            string text = _fuse <= 0f
                ? "DM11 fuse: IMPACT"
                : $"DM11 fuse: {_fuse:0.0} s  (~{_fuse * shell.MuzzleVelocity:0} m)";
            var style = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.Max(14, Screen.height / 60),
                alignment = TextAnchor.MiddleCenter
            };
            style.normal.textColor = new Color(0.85f, 0.95f, 0.75f);
            GUI.Label(new Rect(0f, Screen.height * 0.12f, Screen.width, Screen.height * 0.06f), text, style);
        }
    }
}
