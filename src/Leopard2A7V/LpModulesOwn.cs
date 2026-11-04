using System;
using System.Reflection;
using GHPC;
using GHPC.Thermals;
using GHPC.Vehicle;
using GHPC.Weapons;
using MelonLoader;
using UnityEngine;

namespace Leopard2A7V
{
    /// <summary>
    /// Builds the 2A7V's own internal modules and gives the donor's modules a health value.
    ///
    /// Two classes in this game carry the name DestructibleComponent: one in the global namespace with public
    /// Health / MinimumRhaTrigger / Effects, and GHPC.Equipment.DestructibleComponent, which is the one the
    /// chassis and the fire control actually use and which keeps its numbers in private fields. Talking to the
    /// wrong one compiles and does nothing at runtime, so every write here goes through reflection and both
    /// shapes are filled when present.
    ///
    /// Layout comes from the object's own x-ray export, in that export's frame (+X forward with +Z to
    /// starboard, as ObjModel reads it), so the numbers below are handed to LpConfig.FromExport before they
    /// are placed on the rig, whose forward is +Z:
    ///
    ///   ammo_hull_xray    ( 1.835, 1.089, -0.352)  size (0.848, 0.706, 1.122)
    ///   ammo_turret_xray  (-1.373, 2.151, -0.644)  size (0.560, 0.523, 0.798)
    /// </summary>
    public static class LpModulesOwn
    {
        private const string Tag = "[L2A7V][modules]";

        private const float IgnitionDelay = 1.2f;

        private sealed class Spec
        {
            internal string Name;
            internal Vector3 Centre;
            internal Vector3 Size;
            internal float Health;
            internal float Threshold;
        }

        private static readonly Spec[] Racks =
        {
            new Spec
            {
                Name = "ammo_hull", Centre = new Vector3(1.835f, 1.089f, -0.352f),
                Size = new Vector3(0.848f, 0.706f, 1.122f), Health = 120f, Threshold = 25f,
            },
            new Spec
            {
                Name = "ammo_turret", Centre = new Vector3(-1.373f, 2.151f, -0.644f),
                Size = new Vector3(0.560f, 0.523f, 0.798f), Health = 100f, Threshold = 25f,
            },
        };

        public static void Build(GameObject unitRoot)
        {
            if (unitRoot == null) return;
            Transform space = Space(unitRoot);
            if (space == null)
            {
                MelonLogger.Warning(Tag + " no rig on " + unitRoot.name + "; modules left alone");
                return;
            }

            ChassisDamageManager chassis = unitRoot.GetComponentInChildren<ChassisDamageManager>(true);
            VehicleInfo info = LpFind.VehicleInfo(unitRoot);
            int layer = chassis != null && chassis.EngineHitZone != null
                ? chassis.EngineHitZone.gameObject.layer
                : 8;

            int racks = 0;
            foreach (Spec spec in Racks)
                if (BuildOne(space, info, spec, layer)) racks++;

            if (info != null && (info.AmmoRacks == null || info.AmmoRacks.Count == 0))
                BuildFallbackRack(info, space);

            int armed = ArmPowerpack(chassis);
            int gun = ArmGunAndFireControl(unitRoot);
            AddEngineHeat(space);
            LogCrew(unitRoot);

            MelonLogger.Msg(Tag + " built " + racks + " ammunition rack(s), armed " + armed +
                            " powerpack zone(s) and " + gun + " gun / fire control module(s)");
        }

        // ---------------------------------------------------------------- racks

        private static bool BuildOne(Transform space, VehicleInfo info, Spec spec, int layer)
        {
            if (AlreadyThere(space, spec.Name)) return false;

            var go = new GameObject("L2A7V_Module " + spec.Name);
            go.transform.SetParent(space, false);
            // The spec's numbers are the export's, where +X is forward; the rig is this game's, where +Z is.
            go.transform.position = space.TransformPoint(LpConfig.FromExport(spec.Centre));
            go.transform.rotation = space.rotation;
            go.transform.localScale = Vector3.one;
            go.layer = layer;
            try { go.tag = "Penetrable"; } catch (UnityException) { }

            var box = go.AddComponent<BoxCollider>();
            box.size = LpConfig.FromExport(spec.Size);
            box.isTrigger = false;

            var module = go.AddComponent<GHPC.Equipment.DestructibleComponent>();
            Configure(module, spec.Health, spec.Threshold);
            // Damage on these boxes used to be silent: the health fell and nothing happened, so a hit read as
            // "no effect". Log every change and every destruction, and say why the cook-off did or did not run.
            module.HealthChanged += (source, percent) =>
                MelonLogger.Msg(Tag + " '" + spec.Name + "' health " + (percent * 100f).ToString("0") + "%");
            module.Destroyed += _ => CookOff(info, go.transform.position, spec.Name);

            // The module is a bare hit box; without an AAR visual the after-action viewer shows nothing broken,
            // which reads as "the damage model does not exist".
            AarVisual aar = go.AddComponent<AarVisual>();
            aar.HideUntilAar = true;
            Material xray = LpArmorOwn.XrayMaterial();
            if (xray != null)
            {
                aar.SwitchMaterials = true;
                aar.AarMaterial = xray;
                aar.UpdateVisibility();
            }

            MelonLogger.Msg(Tag + " module '" + spec.Name + "' at " +
                            go.transform.position.ToString("0.00") + " health " + spec.Health +
                            " layer " + layer);
            return true;
        }

        /// <summary>
        /// A rack of last resort.
        ///
        /// The donor is a LEO2A3, which has no MainGun before the art pass, so VehicleInfo.fillAmmoRacks()
        /// returns early and the vehicle can end up with an empty rack list. Cook-off then has nothing to
        /// light, and the whole ammunition damage path is dead. A bare AmmoRack adds its own transform as a
        /// slot in Awake, which is enough for the fire to exist; it is only created when the list is empty.
        /// </summary>
        private static void BuildFallbackRack(VehicleInfo info, Transform space)
        {
            var go = new GameObject("L2A7V_AmmoRack");
            go.transform.SetParent(space, false);
            go.transform.position = space.TransformPoint(new Vector3(0.0f, 1.0f, -0.3f));

            var rack = go.AddComponent<AmmoRack>();
            rack.FriendlyName = "2A7V ammunition";
            // Deliberately not a blowout-panel rack: this stand-in exists so the ammunition damage path is
            // alive at all, and a venting rack would mean the fire could never happen.
            rack.HasBlowoutPanels = false;

            if (info.AmmoRacks == null)
            {
                var field = typeof(VehicleInfo).GetProperty("AmmoRacks");
                if (field != null && field.CanWrite)
                    field.SetValue(info, new System.Collections.Generic.LinkedList<AmmoRack>());
            }
            if (info.AmmoRacks == null)
            {
                MelonLogger.Warning(Tag + " could not allocate a rack list; cook-off stays unavailable");
                return;
            }
            info.AmmoRacks.AddLast(rack);
            MelonLogger.Msg(Tag + " the vehicle had no racks; registered a fallback rack (" +
                            rack.Capacity + " slot(s), blowout panels)");
        }

        private static bool AlreadyThere(Transform space, string name)
        {
            foreach (Transform node in space.GetComponentsInChildren<Transform>(true))
                if (node != null && node.name.StartsWith("L2A7V_Module " + name, StringComparison.Ordinal))
                    return true;
            return false;
        }

        /// <summary>
        /// Sets the nearest ammunition rack on fire when one of these modules is destroyed.
        ///
        /// It is deliberately not instant: a hit that reaches the rack gives the crew a moment, and about half
        /// the time the rounds smoulder instead of cooking off. Blowout panels vent the rack and ruin the rounds
        /// without hurting anybody, which is what those panels exist for.
        /// </summary>
        private static void CookOff(VehicleInfo info, Vector3 position, string name)
        {
            MelonLogger.Msg(Tag + " ammunition rack '" + name + "' destroyed");
            if (info == null || info.AmmoRacks == null || info.AmmoRacks.Count == 0)
            {
                // The LEO2A3 donor can end up with no racks at all, and then a destroyed rack module had no
                // consequence whatsoever. Make one on the spot: a bare AmmoRack registers itself as a slot.
                GameObject spawned = new GameObject("L2A7V_AmmoRack");
                spawned.transform.position = position;
                var own = spawned.AddComponent<AmmoRack>();
                own.FriendlyName = "2A7V ammunition";
                if (info != null)
                {
                    if (info.AmmoRacks == null)
                    {
                        var list = typeof(VehicleInfo).GetProperty("AmmoRacks");
                        if (list != null && list.CanWrite)
                            list.SetValue(info, new System.Collections.Generic.LinkedList<AmmoRack>());
                    }
                    if (info.AmmoRacks != null) info.AmmoRacks.AddLast(own);
                }
                own.NotifyRackFire();
                MelonLogger.Msg(Tag + " no racks on the vehicle; built one at the hit and set it on fire");
                return;
            }

            AmmoRack nearest = null;
            float best = float.MaxValue;
            foreach (AmmoRack rack in info.AmmoRacks)
            {
                if (rack == null) continue;
                float distance = (rack.transform.position - position).sqrMagnitude;
                if (distance >= best) continue;
                best = distance;
                nearest = rack;
            }

            if (nearest == null)
            {
                MelonLogger.Warning(Tag + " no ammo rack near " + position.ToString("0.0"));
                return;
            }

            if (nearest.OnFire)
            {
                MelonLogger.Msg(Tag + " '" + nearest.GetFriendlyName() + "' is already burning");
                return;
            }

            if (nearest.HasBlowoutPanels)
            {
                int ruined = 0;
                for (int i = 0; i < 3 && i < nearest.Capacity; i++)
                {
                    nearest.DamageRoundBySlot(i);
                    ruined++;
                }
                MelonLogger.Msg(Tag + " '" + nearest.GetFriendlyName() + "' vented through its blowout panels; " +
                                ruined + " round(s) ruined, crew intact");
                return;
            }


            GameObject host = new GameObject("L2A7V_CookOffTimer");
            var timer = host.AddComponent<CookOffTimer>();
            timer.Begin(nearest, IgnitionDelay, Mathf.Sqrt(best), info.AmmoRacks.Count);
        }

        /// <summary>Lights the rack after a short delay, so a hit is not instantly a funeral.</summary>
        public class CookOffTimer : MonoBehaviour
        {
            private AmmoRack _rack;
            private float _remaining;
            private float _distance;
            private int _rackCount;

            internal void Begin(AmmoRack rack, float delay, float distance, int rackCount)
            {
                _rack = rack;
                _remaining = delay;
                _distance = distance;
                _rackCount = rackCount;
            }

            private void Update()
            {
                _remaining -= Time.deltaTime;
                if (_remaining > 0f) return;
                if (_rack != null && !_rack.OnFire)
                {
                    _rack.NotifyRackFire();
                    MelonLogger.Msg(Tag + " cook-off started in '" + _rack.GetFriendlyName() + "' (" +
                                    _distance.ToString("0.0") + " m from the hit, " + _rackCount +
                                    " rack(s) on the vehicle)");
                }
                Destroy(gameObject);
            }
        }

        // ------------------------------------------------------------ existing

        private static int ArmPowerpack(ChassisDamageManager chassis)
        {
            if (chassis == null) return 0;
            int armed = 0;
            if (Arm(chassis.EngineHitZone, 200f, 25f)) armed++;
            if (Arm(chassis.TransmissionHitZone, 160f, 25f)) armed++;
            if (Arm(chassis.RadiatorHitZone, 100f, 20f)) armed++;
            return armed;
        }

        private static int ArmGunAndFireControl(GameObject unitRoot)
        {
            int armed = 0;
            foreach (WeaponSystem weapon in unitRoot.GetComponentsInChildren<WeaponSystem>(true))
            {
                if (Arm(weapon.BarrelHitZone, 120f, 25f)) armed++;
                if (Arm(weapon.BreechHitZone, 120f, 25f)) armed++;
            }

            FireControlSystem fcs = unitRoot.GetComponentInChildren<FireControlSystem>(true);
            if (fcs != null)
            {
                if (Arm(fcs.ComputerComponent, 80f, 20f)) armed++;
                if (Arm(fcs.LaserComponent, 60f, 15f)) armed++;

                // Where these boxes sit matters as much as their health: left on the donor they are inside a
                // hull that no longer exists, so a hit through the 2A7V's turret never reaches them. Both move
                // into the turret volume, which is where the crew would be working on them.
                Transform turret = Turret(unitRoot);
                if (turret != null)
                {
                    MoveZone(fcs.ComputerComponent, turret.position + turret.up * 0.35f, turret.rotation,
                             new Vector3(0.45f, 0.30f, 0.45f));
                    MoveZone(fcs.LaserComponent,
                             turret.position + turret.up * 0.15f + turret.right * 0.40f, turret.rotation,
                             new Vector3(0.25f, 0.20f, 0.30f));
                }

                MelonLogger.Msg(Tag + " fire control: computer health " +
                                (fcs.ComputerComponent == null ? "n/a" : fcs.ComputerComponent.Health.ToString("0")) +
                                ", laser health " +
                                (fcs.LaserComponent == null ? "n/a" : fcs.LaserComponent.Health.ToString("0")));
            }
            return armed;
        }

        /// <summary>
        /// Reports where the crew hit zones actually are.
        ///
        /// The crew are characters with their own rigs and auto-generated hit boxes; they are positioned by crew
        /// stations, not by the armour export, so a converted hull can leave them sitting where the donor put
        /// them. Moving them means re-authoring the stations, so this only logs the positions - it is the
        /// evidence needed to decide whether they are inside the 2A7V's fighting compartment or not.
        /// </summary>
        private static void LogCrew(GameObject unitRoot)
        {
            int found = 0;
            foreach (MonoBehaviour behaviour in unitRoot.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null) continue;
                if (behaviour.GetType().Name != "CrewMember") continue;
                found++;
                MelonLogger.Msg(Tag + " crew '" + behaviour.name + "' at " +
                                behaviour.transform.position.ToString("0.00"));
            }
            MelonLogger.Msg(Tag + " " + found + " crew member(s) in the unit");
        }

        private static Transform Turret(GameObject unitRoot)
        {
            foreach (Transform node in unitRoot.GetComponentsInChildren<Transform>(true))
                if (node != null && node.name.Equals("turret", StringComparison.OrdinalIgnoreCase))
                    return node;
            return null;
        }

        private static bool Arm(GHPC.Equipment.DestructibleComponent module, float health, float threshold)
        {
            if (module == null) return false;
            if (module.Health > 0f) return false;      // the donor already gave it a value
            Configure(module, health, threshold);
            return true;
        }

        /// <summary>
        /// Writes the health numbers into whichever DestructibleComponent this is. The equipment class keeps
        /// them private (_health, _fullHealth, _damageThreshold, _pressureTolerance, _children); the global
        /// class exposes Health / MinimumRhaTrigger.
        /// </summary>
        private static void Configure(Component component, float health, float threshold)
        {
            if (component == null) return;
            Type type = component.GetType();
            const BindingFlags Priv = BindingFlags.Instance | BindingFlags.NonPublic;
            const BindingFlags Pub = BindingFlags.Instance | BindingFlags.Public;

            SetFloat(type, component, "_health", health, Priv);
            SetFloat(type, component, "_fullHealth", health, Priv);
            SetFloat(type, component, "_damageThreshold", threshold, Priv);
            SetFloat(type, component, "_pressureTolerance", 1f, Priv);

            FieldInfo children = type.GetField("_children", Priv);
            if (children != null && children.GetValue(component) == null)
                children.SetValue(component, Activator.CreateInstance(children.FieldType));

            SetFloat(type, component, "Health", health, Pub);
            SetFloat(type, component, "MinimumRhaTrigger", threshold, Pub);
        }

        private static void SetFloat(Type type, object target, string name, float value, BindingFlags flags)
        {
            FieldInfo field = type.GetField(name, flags);
            if (field == null || field.FieldType != typeof(float)) return;
            field.SetValue(target, value);
        }

        private static void MoveZone(Component component, Vector3 position, Quaternion rotation, Vector3 size)
        {
            if (component == null) return;
            Transform node = component.transform;
            node.position = position;
            node.rotation = rotation;

            BoxCollider box = node.GetComponent<BoxCollider>();
            if (box == null)
            {
                foreach (Collider extra in node.GetComponents<Collider>()) extra.enabled = false;
                box = node.gameObject.AddComponent<BoxCollider>();
            }
            box.size = size;
            box.isTrigger = false;
            box.enabled = true;
        }

        /// <summary>
        /// The engine bay is the hot part of any tank and the thermal sight should say so. Without a heat
        /// source the 2A7V reads as room temperature, which is the wrong signature for a running vehicle.
        /// </summary>
        private static void AddEngineHeat(Transform space)
        {
            GameObject host = new GameObject("L2A7V_Heat_Engine");
            host.transform.SetParent(space, false);
            host.transform.position = space.TransformPoint(
                LpConfig.FromExport(new Vector3(-2.331f, 1.093f, -0.014f)));
            var heat = host.AddComponent<HeatSource>();
            heat.Heat = 0.85f;
            MelonLogger.Msg(Tag + " engine heat source at " + host.transform.position.ToString("0.0"));
        }

        private static Transform Space(GameObject unitRoot)
        {
            foreach (Transform node in unitRoot.GetComponentsInChildren<Transform>(true))
                if (node != null && node.name.Equals("leo2_rig", StringComparison.OrdinalIgnoreCase))
                    return node;
            return unitRoot.transform;
        }
    }
}
