using System;
using GHPC;
using GHPC.Vehicle;
using MelonLoader;
using UnityEngine;

namespace Leopard2A7V
{
    /// <summary>
    /// Rebinds the vehicle's animated hatches to the 2A7V's own art.
    ///
    /// VehicleInfo holds an AnimatedPart per crew hatch, and each part drives one transform between two poses.
    /// The donor's art (and those pose transforms) are hidden when the visual is converted, so a hatch would
    /// keep animating a node nobody can see. This looks for the matching node in the imported model and points
    /// the part at it.
    ///
    /// Matching is by name: the part keeps the name of the transform it drove on the donor, and the model uses
    /// the same naming convention. When nothing matches, the names that were available are logged, which is the
    /// only way to work out the convention for a model that names them differently.
    /// </summary>
    public static class LpHatches
    {
        private const string Tag = "[L2A7V][hatches]";

        public static void Bind(GameObject unitRoot)
        {
            if (unitRoot == null) return;
            VehicleInfo info = LpFind.VehicleInfo(unitRoot);
            if (info == null)
            {
                MelonLogger.Msg(Tag + " no VehicleInfo near " + unitRoot.name + "; hatches untouched");
                return;
            }

            Transform art = ImportedRoot(unitRoot);
            if (art == null)
            {
                MelonLogger.Msg(Tag + " no imported art yet; hatches left on the donor");
                return;
            }

            int bound = 0;
            bound += TryBind(info.CommanderHatch, art, "commander");
            bound += TryBind(info.GunnerHatch, art, "gunner");
            bound += TryBind(info.DriverHatch, art, "driver");
            bound += TryBind(info.LoaderHatch, art, "loader");
            if (info.OtherTurretHatches != null)
                foreach (AnimatedPart part in info.OtherTurretHatches)
                    bound += TryBind(part, art, "turret");

            MelonLogger.Msg(Tag + " bound " + bound + " hatch part(s) to the 2A7V art");
            if (bound == 0) DumpCandidates(art);
        }

        private static int TryBind(AnimatedPart part, Transform art, string role)
        {
            if (part == null || part.Transform == null) return 0;
            string donorName = part.Transform.name;

            Transform match = FindByName(art, donorName) ?? FindByHatchNumber(art, donorName) ?? FindByRole(art, role);
            if (match == null) return 0;

            Transform pivot = MakeHinge(match, role, part);
            part.Transform = pivot != null ? pivot : match;
            if (part.StartTransform == null) part.StartTransform = part.Transform;
            if (part.EndTransform == null) part.EndTransform = part.Transform;

            MelonLogger.Msg(Tag + " " + role + " hatch '" + donorName + "' -> '" + match.name +
                            (pivot != null ? "' on a hinge" : "' (no hinge)"));
            return 1;
        }

        /// <summary>
        /// Puts a hinge under the lid, the way the T-90M mod's rig_map does with its hatch lines. A lid is a
        /// flat piece of the turret mesh, so it can only open if it gets a pivot of its own. The hinge comes from
        /// the table when the line names one; otherwise it is derived from the lid's bounds - the aft edge, on
        /// the lateral axis - and the lid is rotated by the fraction the game's own hatch animator reports.
        /// </summary>
        private static Transform MakeHinge(Transform lid, string role, AnimatedPart part)
        {
            LpData.Hatch hatch = LpData.HatchFor(lid.name, role);

            Bounds bounds = BoundsOf(lid);
            Vector3 axis = hatch != null ? hatch.Axis : Vector3.forward;
            float degrees = hatch != null ? hatch.Degrees : 95f;
            Vector3 hingeLocal = hatch != null && hatch.HasHinge
                ? hatch.Hinge
                : new Vector3(bounds.min.x, bounds.center.y, bounds.center.z);

            Vector3 world = lid.TransformPoint(hingeLocal);
            var go = new GameObject("L2A7V_Hatch_" + lid.name);
            go.transform.SetParent(lid.parent, false);
            go.transform.position = world;
            go.transform.rotation = lid.rotation;

            lid.SetParent(go.transform, true);

            var motion = go.AddComponent<LpHatchMotion>();
            motion.Begin(go.transform, axis, degrees,
                         part == null ? null : (Component)part.Controller, part == null ? null : part.FloatParam);
            return go.transform;
        }

        private static Bounds BoundsOf(Transform node)
        {
            bool any = false;
            Bounds bounds = new Bounds(node.position, Vector3.zero);
            foreach (MeshFilter filter in node.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter == null || filter.sharedMesh == null) continue;
                Bounds local = filter.sharedMesh.bounds;
                var world = new Bounds(filter.transform.TransformPoint(local.center),
                                       filter.transform.TransformVector(local.size));
                if (!any) { bounds = world; any = true; }
                else bounds.Encapsulate(world);
            }
            return any ? bounds : new Bounds(node.position, Vector3.zero);
        }

        private static Transform FindByName(Transform art, string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            foreach (Transform node in art.GetComponentsInChildren<Transform>(true))
                if (node != null && string.Equals(node.name, name, StringComparison.OrdinalIgnoreCase))
                    return node;
            return null;
        }

        private static Transform FindByHatchNumber(Transform art, string donorName)
        {
            string number = HatchNumber(donorName);
            if (number == null) return null;
            string wanted = "hatch_" + number;
            foreach (Transform node in art.GetComponentsInChildren<Transform>(true))
                if (node != null && string.Equals(node.name, wanted, StringComparison.OrdinalIgnoreCase))
                    return node;
            return null;
        }

        private static string HatchNumber(string name)
        {
            int at = name.IndexOf("hatch", StringComparison.OrdinalIgnoreCase);
            if (at < 0) return null;
            int i = at + "hatch".Length;
            while (i < name.Length && (name[i] == '_' || name[i] == '-' || name[i] == ' ')) i++;
            int start = i;
            while (i < name.Length && char.IsDigit(name[i])) i++;
            if (i == start) return null;
            return name.Substring(start, i - start).PadLeft(2, '0');
        }

        private static Transform FindByRole(Transform art, string role)
        {
            foreach (Transform node in art.GetComponentsInChildren<Transform>(true))
            {
                if (node == null) continue;
                string n = node.name;
                if (n.IndexOf("hatch", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (n.IndexOf(role, StringComparison.OrdinalIgnoreCase) >= 0) return node;
            }
            return null;
        }

        private static Transform ImportedRoot(GameObject unitRoot)
        {
            foreach (Transform node in unitRoot.GetComponentsInChildren<Transform>(true))
                if (node != null && node.name.Equals(LpConfig.VisualRootName, StringComparison.OrdinalIgnoreCase))
                    return node;
            return null;
        }

        private static void DumpCandidates(Transform art)
        {
            int shown = 0;
            foreach (Transform node in art.GetComponentsInChildren<Transform>(true))
            {
                if (node == null) continue;
                if (node.name.IndexOf("hatch", StringComparison.OrdinalIgnoreCase) < 0) continue;
                MelonLogger.Msg(Tag + "   candidate '" + node.name + "'");
                if (++shown >= 12) break;
            }
            if (shown == 0) MelonLogger.Msg(Tag + "   the imported model has no node with 'hatch' in its name");
            else MelonLogger.Msg(Tag + "   " + shown + " candidate(s) listed");
        }
    }

    /// <summary>
    /// Rotates a lid about its hinge by the fraction the game's own hatch animator reports. A lid with no
    /// animator simply stays shut.
    /// </summary>
    public class LpHatchMotion : MonoBehaviour
    {
        private Transform _pivot;
        private Vector3 _axis;
        private float _degrees;
        private Component _controller;
        private string _parameter;

        internal void Begin(Transform pivot, Vector3 axis, float degrees, Component controller, string parameter)
        {
            _pivot = pivot;
            _axis = axis;
            _degrees = degrees;
            _controller = controller;
            _parameter = parameter;
        }

        private void Update()
        {
            if (_pivot == null) return;
            float fraction = 0f;
            if (_controller != null && !string.IsNullOrEmpty(_parameter))
            {
                try
                {
                    var parameters = _controller.GetType().GetProperty("parameters");
                    var array = parameters == null ? null : parameters.GetValue(_controller) as System.Array;
                    if (array != null)
                    {
                        foreach (object parameter in array)
                        {
                            var name = parameter.GetType().GetField("name");
                            if (name == null || (string)name.GetValue(parameter) != _parameter) continue;
                            var getFloat = _controller.GetType().GetMethod("GetFloat", new[] { typeof(string) });
                            if (getFloat != null)
                                fraction = Mathf.Clamp01((float)getFloat.Invoke(_controller, new object[] { _parameter }));
                            break;
                        }
                    }
                }
                catch (Exception) { }
            }
            _pivot.localRotation = Quaternion.AngleAxis(fraction * _degrees, _axis);
        }
    }
}
