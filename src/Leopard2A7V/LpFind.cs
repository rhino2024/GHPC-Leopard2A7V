using GHPC;
using GHPC.Vehicle;
using UnityEngine;

namespace Leopard2A7V
{
    /// <summary>
    /// Finds the parts that hang off a unit but are not children of the object the spawner hands out.
    ///
    /// VehicleInfo is one of them: the log says "no VehicleInfo on LEO2A7V 13" while the vehicle is clearly a
    /// vehicle, so the component lives on a container that is not under the spawn root. Looking it up by
    /// proximity is enough - a unit is tens of metres from the next one even on a crowded map.
    /// </summary>
    public static class LpFind
    {
        private const float MaxDistance = 12f;

        internal static VehicleInfo VehicleInfo(GameObject unitRoot)
        {
            if (unitRoot == null) return null;

            VehicleInfo info = unitRoot.GetComponentInChildren<VehicleInfo>(true);
            if (info != null) return info;

            Unit unit = unitRoot.GetComponent<Unit>();
            if (unit == null) unit = unitRoot.GetComponentInChildren<Unit>(true);
            if (unit != null)
            {
                info = unit.GetComponent<VehicleInfo>();
                if (info != null) return info;

                Transform parent = unit.transform.parent;
                for (int depth = 0; parent != null && depth < 4; depth++)
                {
                    info = parent.GetComponent<VehicleInfo>();
                    if (info != null) return info;
                    info = parent.GetComponentInChildren<VehicleInfo>(true);
                    if (info != null) return info;
                    parent = parent.parent;
                }
            }

            Vector3 origin = unitRoot.transform.position;
            float best = MaxDistance * MaxDistance;
            VehicleInfo closest = null;
            foreach (VehicleInfo candidate in Object.FindObjectsOfType<VehicleInfo>())
            {
                if (candidate == null) continue;
                float distance = (candidate.transform.position - origin).sqrMagnitude;
                if (distance >= best) continue;
                best = distance;
                closest = candidate;
            }
            return closest;
        }

        /// <summary>The Unit that owns this object, wherever it sits in the scene.</summary>
        internal static Unit Unit(GameObject unitRoot)
        {
            if (unitRoot == null) return null;
            Unit unit = unitRoot.GetComponent<Unit>();
            if (unit != null) return unit;
            unit = unitRoot.GetComponentInChildren<Unit>(true);
            if (unit != null) return unit;

            VehicleInfo info = VehicleInfo(unitRoot);
            if (info != null)
            {
                unit = info.GetComponent<Unit>();
                if (unit != null) return unit;
                unit = info.GetComponentInParent<Unit>();
            }
            return unit;
        }
    }
}
