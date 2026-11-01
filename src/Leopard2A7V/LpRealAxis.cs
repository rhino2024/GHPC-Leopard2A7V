using GHPC;
using GHPC.Vehicle;
using MelonLoader;
using UnityEngine;

namespace Leopard2A7V
{
    /// <summary>
    /// Puts the turret and gun on the 2A7V's real axes.
    ///
    /// The donor turns its turret about a LEO2A3 ring and pitches its gun about a LEO2A3 trunnion. The 2A7V
    /// turret sits further back and its gun is mounted higher, so the whole turret was pivoting a few
    /// centimetres off - visible up close, and wrong for anything that measures from the ring. Both points come
    /// from the x-ray export:
    ///
    ///   drive_turret_h_xray  (0.407, 1.109, -0.006)   the traverse drive, on the ring
    ///   gun_trunnion_xray    (1.373, 1.987, -0.033)   the trunnion
    ///
    /// Moving a pivot while its artwork hangs under it would drag the model with it, so every child is offset
    /// back by the same amount: the mesh stays where it was, and only the axis it turns about changes.
    /// </summary>
    public static class LpRealAxis
    {
        private const string Tag = "[L2A7V][axis]";

        private static readonly Vector3 TurretRing = new Vector3(0f, 1.109f, -0.006f);

        private static readonly Vector3 GunTrunnion = new Vector3(1.373f, 1.987f, -0.033f);

        public static void Apply(GameObject unitRoot)
        {
            if (unitRoot == null) return;
            Transform space = Space(unitRoot);
            AimablePlatform turret = null;
            AimablePlatform gun = null;

            foreach (AimablePlatform platform in unitRoot.GetComponentsInChildren<AimablePlatform>(true))
            {
                if (platform == null || platform.Transform == null) continue;
                // The gun platform is the one pitching about X; matching on the axis name keeps this free of a
                // type from a helper namespace the mod does not otherwise touch.
                string axis = platform.Axis.ToString();
                string name = platform.Transform.name;
                bool isGun = axis == "X" ||
                             name.IndexOf("gun", System.StringComparison.OrdinalIgnoreCase) >= 0 ||
                             name.IndexOf("mantlet", System.StringComparison.OrdinalIgnoreCase) >= 0;
                if (isGun) { if (gun == null) gun = platform; }
                else if (turret == null) turret = platform;
            }

            int moved = 0;
            if (Move(space, turret, TurretRing, "turret")) moved++;
            if (Move(space, gun, GunTrunnion, "gun")) moved++;
            MelonLogger.Msg(Tag + " moved " + moved + " pivot(s) onto the 2A7V axes");
        }

        private static bool Move(Transform space, AimablePlatform platform, Vector3 target, string what)
        {
            if (platform == null || platform.Transform == null) return false;
            Transform pivot = platform.Transform;
            Vector3 current = space.InverseTransformPoint(pivot.position);
            Vector3 delta = target - current;
            if (delta.magnitude < 0.02f) return false;

            // Keep the artwork: every child keeps its world pose while the pivot itself moves.
            Vector3 worldDelta = space.TransformVector(delta);
            pivot.position += worldDelta;
            foreach (Transform child in pivot)
                child.position -= worldDelta;

            MelonLogger.Msg(Tag + " " + what + " axis " + Format(current) + " -> " + Format(target) +
                            " (" + delta.magnitude.ToString("0.000") + " m)");
            return true;
        }

        private static string Format(Vector3 v)
        {
            return "(" + v.x.ToString("0.00") + ", " + v.y.ToString("0.00") + ", " + v.z.ToString("0.00") + ")";
        }

        private static Transform Space(GameObject unitRoot)
        {
            foreach (Transform node in unitRoot.GetComponentsInChildren<Transform>(true))
                if (node != null && node.name.Equals("leo2_rig", System.StringComparison.OrdinalIgnoreCase))
                    return node;
            return unitRoot.transform;
        }
    }
}
