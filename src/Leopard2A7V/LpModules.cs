using System.Collections.Generic;
using GHPC;
using GHPC.Equipment;
using GHPC.Vehicle;
using MelonLoader;
using UnityEngine;

namespace Leopard2A7V
{
    /// <summary>
    /// Moves the vehicle's internals onto the 2A7V's own layout.
    ///
    /// The donor's engine, transmission and radiator hit zones sit where a LEO2A3's do. The 2A7V has a longer
    /// hull and a different powerpack position, so a round entering the engine deck was passing through the
    /// old boxes - or missing them entirely - and the modules took no damage. The centres come from the
    /// vehicle's x-ray export, which is the same source the armour is built from:
    ///
    ///   engine_xray        (-2.331, 1.093, -0.014)  size (2.838, 1.036, 2.079)
    ///   transmission_xray  (-3.099, 0.949, -0.027)  size (0.894, 0.738, 1.551)
    ///   radiator_xray      (-3.171, 1.506,  0.000)  size (1.014, 0.309, 2.027)
    /// </summary>
    public static class LpModules
    {
        private const string Tag = "[L2A7V][modules]";

        private struct Target
        {
            internal string Field;
            internal Vector3 Centre;
            internal Vector3 Size;
        }

        private static readonly Target[] Targets =
        {
            new Target { Field = "EngineHitZone", Centre = new Vector3(-2.331f, 1.093f, -0.014f), Size = new Vector3(2.838f, 1.036f, 2.079f) },
            new Target { Field = "TransmissionHitZone", Centre = new Vector3(-3.099f, 0.949f, -0.027f), Size = new Vector3(0.894f, 0.738f, 1.551f) },
            new Target { Field = "RadiatorHitZone", Centre = new Vector3(-3.171f, 1.506f, 0.0f), Size = new Vector3(1.014f, 0.309f, 2.027f) },
        };

        public static void Apply(GameObject unitRoot)
        {
            if (unitRoot == null) return;
            ChassisDamageManager chassis = unitRoot.GetComponentInChildren<ChassisDamageManager>(true);
            if (chassis == null)
            {
                MelonLogger.Msg(Tag + " no ChassisDamageManager on " + unitRoot.name + "; hit zones left where the donor put them");
                return;
            }

            Transform space = Space(unitRoot);
            if (space == null) return;

            int moved = 0;
            foreach (Target target in Targets)
            {
                DestructibleComponent zone = Field(chassis, target.Field);
                if (zone == null) continue;
                if (!Move(space, zone, target)) continue;
                moved++;
            }

            MelonLogger.Msg(Tag + " moved " + moved + " of " + Targets.Length + " hit zone(s) onto the 2A7V layout");
        }

        private static bool Move(Transform space, DestructibleComponent zone, Target target)
        {
            Transform node = zone.transform;

            // A hit zone is a bare trigger in this game, but never risk dragging artwork: if anything under it
            // draws, leave the module where the donor put it.
            if (node.GetComponentInChildren<Renderer>(true) != null) return false;

            Vector3 current = space.InverseTransformPoint(node.position);
            float drift = (current - target.Centre).magnitude;
            if (drift < 0.05f) return false;

            // Put the hit zone's transform where the export says the module is, and give its collider the
            // module's own size. A box is used even where the donor had a capsule: the export only measures a
            // bounding box, and a box at the right place is worth more than a capsule at the wrong one.
            node.position = space.TransformPoint(target.Centre);
            Vector3 size = space.TransformVector(target.Size);
            size = new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z));

            BoxCollider box = node.GetComponent<BoxCollider>();
            if (box == null)
            {
                foreach (Collider extra in node.GetComponents<Collider>()) extra.enabled = false;
                box = node.gameObject.AddComponent<BoxCollider>();
            }
            box.size = size;
            box.isTrigger = false;
            box.enabled = true;
            return true;
        }

        private static DestructibleComponent Field(ChassisDamageManager chassis, string name)
        {
            var field = typeof(ChassisDamageManager).GetField(name);
            return field == null ? null : field.GetValue(chassis) as DestructibleComponent;
        }

        /// <summary>The rig root: the transform whose space the x-ray export is expressed in.</summary>
        private static Transform Space(GameObject unitRoot)
        {
            foreach (Transform node in unitRoot.GetComponentsInChildren<Transform>(true))
                if (node != null && node.name.Equals("leo2_rig", System.StringComparison.OrdinalIgnoreCase))
                    return node;
            return unitRoot.transform;
        }
    }
}
