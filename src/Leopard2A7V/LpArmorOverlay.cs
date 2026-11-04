using System;
using System.Collections.Generic;
using System.Reflection;
using GHPC;
using MelonLoader;
using UnityEngine;
using UnityEngine.Rendering;

namespace Leopard2A7V
{
    /// <summary>
    /// Armour added on top of the donor's, never instead of it.
    ///
    /// This is the arrangement the Leopard 2A7HU mod was built around and the one this vehicle now follows.
    /// The older pass here rewrote the donor's own plates and retired the ones it thought it covered, which
    /// meant the LEO2A3's arrays decided what a shot met - and once the placement was corrected the vehicle
    /// still behaved like a 1979 Leopard wherever the retirement had guessed wrong. Leaving the donor alone
    /// removes that whole class of fault: what the donor shipped keeps answering exactly as it did, and this
    /// file only puts plates where the 2A7V has armour the 2A3 does not.
    ///
    /// Two sources, both placed by the same anchors the art uses:
    ///
    /// * The visible <c>ex_armor_*</c> modules - the add-on packages the model draws - carry the vehicle's
    ///   published protection: turret front 880, turret side 460, hull side 360, glacis 360. They are the
    ///   outer surfaces, so a shot meets them first.
    /// * The x-ray export carries the arrays behind and above them: turret rear, hull deck, firewall, mantlet
    ///   block and the spall liners. The turret front and side arrays are deliberately absent, because the
    ///   visible plates already stand for them and two colliders over the same armour would add up.
    ///
    /// Every piece is ONE CONVEX mesh collider. That is not a detail: Unity refuses a non-convex MeshCollider
    /// under a non-kinematic rigidbody ("no longer supported since Unity 5") and silently ignores it, which is
    /// exactly what a piece of armour that never answers a hit looks like. One collider per plate also keeps
    /// the gun window open - a single hull spanning both cheeks would bridge the gap and armour the barrel.
    ///
    /// The pieces hang off the rig bones rather than this mod's art pivots: the sight pass scales those pivots
    /// to nothing inside an eyepiece, and scaling a collider's parent takes its hit detection with it.
    /// </summary>
    public static class LpArmorOverlay
    {
        private const string Tag = "[L2A7V][armour]";

        private const string Prefix = "L2A7V_Armor ";

        private sealed class Zone
        {
            public string Name;
            public float Ke;
            public float Ce;
            public string[] Parts;
            public bool OnTurret;
        }

        /// <summary>
        /// The visible add-on packages, by the group names the export gives them (measured on this model:
        /// ex_armor_17 is the cheek wedge across both sides, _18 the turret flank strake, _02.._09 and _16 the
        /// upper hull skirts, _10.._15 the lower ones, body_01/03..07 the glacis applique).
        ///
        /// The figures are the published 2A7V ones and match the 2A7HU table to the millimetre on the front and
        /// side arrays, which is expected: the two marks carry the same package.
        /// </summary>
        private static readonly Zone[] Zones =
        {
            new Zone
            {
                Name = "turret front array", Ke = 880f, Ce = 1500f, OnTurret = true,
                Parts = new[] { "ex_armor_l_17", "ex_armor_r_17" },
            },
            new Zone
            {
                Name = "turret side array", Ke = 460f, Ce = 800f, OnTurret = true,
                Parts = new[] { "ex_armor_l_18", "ex_armor_r_18" },
            },
            new Zone
            {
                Name = "hull side array", Ke = 360f, Ce = 650f, OnTurret = false,
                Parts = new[]
                {
                    "ex_armor_l_02", "ex_armor_r_02",
                    "ex_armor_l_03", "ex_armor_r_03",
                    "ex_armor_l_04", "ex_armor_r_04",
                    "ex_armor_l_05", "ex_armor_r_05",
                    "ex_armor_l_06", "ex_armor_r_06",
                    "ex_armor_l_07", "ex_armor_r_07",
                    "ex_armor_l_08", "ex_armor_r_08",
                    "ex_armor_l_09", "ex_armor_r_09",
                    "ex_armor_l_10", "ex_armor_r_10",
                    "ex_armor_l_11", "ex_armor_r_11",
                    "ex_armor_l_12", "ex_armor_r_12",
                    "ex_armor_l_13", "ex_armor_r_13",
                    "ex_armor_l_14", "ex_armor_r_14",
                    "ex_armor_l_15", "ex_armor_r_15",
                    "ex_armor_l_16", "ex_armor_r_16",
                },
            },
            // 360 mm normal on a plate sitting ~37 deg from vertical is the ~600 mm the real glacis is
            // credited with against a level shot, which is why the published number is the perpendicular one.
            new Zone
            {
                Name = "hull front glacis", Ke = 360f, Ce = 720f, OnTurret = false,
                Parts = new[]
                {
                    "ex_armor_body_01", "ex_armor_body_03", "ex_armor_body_04",
                    "ex_armor_body_05", "ex_armor_body_06", "ex_armor_body_07",
                },
            },
        };

        public static int Apply(GameObject unitRoot, Transform turretBone, Transform hullBone, Vector3 hullLocal)
        {
            if (!LpConfig.OverlayArmor || unitRoot == null || turretBone == null || hullBone == null) return 0;

            bool talk = !_spoke;
            _spoke = true;

            int layer = ArmorLayer(unitRoot);
            Unit unit = unitRoot.GetComponent<Unit>();
            if (unit == null) unit = unitRoot.GetComponentInParent<Unit>();
            float scale = LpConfig.ArmorScale;

            int visible = ApplyVisible(turretBone, hullBone, hullLocal, layer, unit, scale, talk);
            int xray = 0;
            if (LpConfig.OverlayXrayArmor)
                xray = ApplyXray(turretBone, hullBone, hullLocal, layer, unit, scale, talk);

            int built = visible + xray;
            if (built > 0)
                MelonLogger.Msg(Tag + " " + built + " additive plate collider(s) at x" + scale.ToString("0.00") +
                                " (" + visible + " visible module, " + xray + " x-ray array); the donor's own " +
                                "plates are left exactly as they shipped");
            return built;
        }

        /// <summary>The per-zone breakdown is long; it is worth printing once, not for every vehicle.</summary>
        private static bool _spoke;

        // ---------------------------------------------------------------------------------------------------
        // Visible modules
        // ---------------------------------------------------------------------------------------------------

        private static int ApplyVisible(Transform turretBone, Transform hullBone, Vector3 hullLocal, int layer,
            Unit unit, float scale, bool talk)
        {
            if (!ObjModel.Loaded) return 0;

            int built = 0;
            foreach (Zone zone in Zones)
            {
                Transform bone = zone.OnTurret ? turretBone : hullBone;
                if (bone == null) continue;

                Vector3 anchor = zone.OnTurret ? LpConfig.TurretAxis : Vector3.zero;
                Vector3 localPosition = zone.OnTurret ? Vector3.zero : hullLocal;
                var wanted = new HashSet<string>(zone.Parts, StringComparer.OrdinalIgnoreCase);
                int pieces = 0;
                Vector3 sample = Vector3.zero;

                foreach (ObjGeometry geometry in ObjModel.Geometries)
                {
                    if (!wanted.Contains(geometry.ObjectName)) continue;
                    Mesh mesh = Cached(geometry.ObjectName, geometry, anchor);
                    if (mesh == null) continue;

                    GameObject piece = Spawn(zone.Name, zone.Ke * scale, zone.Ce * scale, bone, localPosition,
                        mesh, layer, unit);
                    if (pieces == 0 && piece != null) sample = piece.transform.position;
                    pieces++;
                }

                if (pieces == 0)
                {
                    MelonLogger.Warning(Tag + " zone '" + zone.Name + "' has no geometry in the model");
                    continue;
                }
                built += pieces;
                if (talk)
                    MelonLogger.Msg(Tag + " '" + zone.Name + "': " + pieces + " plate collider(s), " +
                                    (zone.Ke * scale).ToString("0") + "/" + (zone.Ce * scale).ToString("0") +
                                    " mm KE/CE, first at world " + sample.ToString("0.0"));
            }
            return built;
        }

        // ---------------------------------------------------------------------------------------------------
        // X-ray arrays
        // ---------------------------------------------------------------------------------------------------

        /// <summary>
        /// Which x-ray groups become extra colliders, and with what. The turret front and side arrays are
        /// deliberately absent - the visible ex_armor_* plates over them are already colliders, and stacking
        /// both would add their thicknesses - as is the hull glacis array, which sits behind body_01/03..07.
        /// </summary>
        private static bool TryXrayZone(string name, out float ke, out float ce, out bool onTurret)
        {
            ke = ce = 0f;
            onTurret = false;
            string n = name.ToLowerInvariant();

            if (n.StartsWith("composite_armor_turret"))
            {
                onTurret = true;
                if (n.Contains("_10")) { ke = 350f; ce = 700f; return true; }                  // mantlet block
                if (n.Contains("_05") || n.Contains("_06") || n.Contains("_08"))
                {
                    ke = 250f; ce = 400f; return true;                                        // rear and bustle
                }
                return false;   // front and side arrays: the visible plates already carry them
            }

            if (n.StartsWith("composite_armor_hull"))
            {
                if (n.Contains("_04")) { ke = 100f; ce = 100f; return true; }                  // firewall
                if (n.Contains("_02") || n.Contains("_03") || n.Contains("_05"))
                {
                    // The three layers of the hull deck package share one footprint; each gets a third so the
                    // package totals 150/210 like the single plate it stands for.
                    ke = 50f; ce = 70f; return true;
                }
                return false;   // hull_01 is the glacis array behind the visible applique
            }

            if (n.StartsWith("spall_liner")) { ke = 15f; ce = 15f; onTurret = true; return true; }
            if (n.StartsWith("firewall_armor")) { ke = 30f; ce = 30f; onTurret = true; return true; }
            return false;
        }

        private static int ApplyXray(Transform turretBone, Transform hullBone, Vector3 hullLocal, int layer,
            Unit unit, float scale, bool talk)
        {
            if (!LpXrayParts.Loaded || LpXrayParts.Failed) return 0;

            int built = 0;
            foreach (LpXrayParts.Part part in LpXrayParts.Parts)
            {
                string name = part.Name;
                string n = name.ToLowerInvariant();
                if (!n.StartsWith("composite_armor") && !n.StartsWith("spall_liner") &&
                    !n.StartsWith("firewall_armor"))
                    continue;

                float ke, ce;
                bool onTurret;
                if (!TryXrayZone(name, out ke, out ce, out onTurret)) continue;

                // The liner sides are decided by where they sit, not by their number: the ones that line the
                // turret are drawn above the ring, the ones that line the hull below it.
                if (n.StartsWith("spall_liner"))
                    onTurret = part.Center.y > 1.7f;

                Transform bone = onTurret ? turretBone : hullBone;
                if (bone == null) continue;

                Vector3 anchor = onTurret ? LpConfig.TurretAxis : Vector3.zero;
                Vector3 localPosition = onTurret ? Vector3.zero : hullLocal;

                Mesh mesh = Cached("x:" + name, part, anchor);
                if (mesh == null) continue;
                if (Spawn(name, ke * scale, ce * scale, bone, localPosition, mesh, layer, unit) != null)
                    built++;
            }

            if (built > 0 && talk)
                MelonLogger.Msg(Tag + " x-ray arrays: " + built + " extra collider(s) (mantlet, turret rear, " +
                                "hull deck, firewall, spall liners)");
            return built;
        }

        // ---------------------------------------------------------------------------------------------------
        // Shared
        // ---------------------------------------------------------------------------------------------------

        /// <summary>
        /// The collider mesh for one part, built once per session.
        ///
        /// Every spawn used to rebuild all sixty of them - the vertices copied and the convex hull cooked
        /// again - which is the most expensive thing this mod does per vehicle. The meshes never change, so a
        /// shared one per part is both correct and free after the first spawn. Keyed by part name, with the
        /// x-ray parts prefixed so the two sources cannot collide.
        /// </summary>
        private static readonly Dictionary<string, Mesh> MeshCache =
            new Dictionary<string, Mesh>(StringComparer.Ordinal);

        private static Mesh Cached(string key, ObjGeometry geometry, Vector3 anchor)
        {
            Mesh mesh;
            if (MeshCache.TryGetValue(key, out mesh) && mesh != null) return mesh;
            mesh = BuildMesh(geometry, anchor);
            if (mesh != null) MeshCache[key] = mesh;
            return mesh;
        }

        private static Mesh Cached(string key, LpXrayParts.Part part, Vector3 anchor)
        {
            Mesh mesh;
            if (MeshCache.TryGetValue(key, out mesh) && mesh != null) return mesh;
            mesh = BuildMesh(part, anchor);
            if (mesh != null) MeshCache[key] = mesh;
            return mesh;
        }

        private static GameObject Spawn(string zoneName, float ke, float ce, Transform bone, Vector3 localPosition,
            Mesh mesh, int layer, Unit unit)
        {
            var go = new GameObject(Prefix + zoneName);
            go.transform.SetParent(bone, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            go.layer = layer;
            try { go.tag = "Penetrable"; } catch (UnityException) { }

            var collider = go.AddComponent<MeshCollider>();
            bool convex = false;
            try
            {
                collider.sharedMesh = mesh;
                collider.convex = true;
                convex = collider.sharedMesh != null;
            }
            catch (Exception ex)
            {
                MelonLogger.Warning(Tag + " convex cook failed for " + mesh.name + ": " + ex.Message);
            }

            if (!convex)
            {
                // A plate the physics cooker refuses must not leave the zone silently unarmoured: fall back to
                // its box, which at least keeps the volume hittable.
                UnityEngine.Object.Destroy(collider);
                var box = go.AddComponent<BoxCollider>();
                box.center = mesh.bounds.center;
                box.size = mesh.bounds.size;
            }

            var armor = go.AddComponent<UniformArmor>();
            armor.PrimarySabotRha = ke;
            armor.PrimaryHeatRha = ce;
            // SabotRha/HeatRha return the secondary figures once a piece counts as detonated; left at zero a
            // round that reached a piece in that state would meet no armour at all.
            armor.SecondarySabotRha = ke;
            armor.SecondaryHeatRha = ce;
            armor.ThicknessListed = UniformArmor.ThicknessMode.RHAE;
            armor.AngleMatters = true;
            armor.DetonatedObjects = Array.Empty<GameObject>();
            armor.UndetonatedObjects = Array.Empty<GameObject>();
            armor.CenterPoint = go.transform;
            armor.SetName(zoneName + " (" + mesh.name + ")");
            ArmorCodexScriptable codex = CodexFor(zoneName);
            if (codex != null && ArmorTypeField != null) ArmorTypeField.SetValue(armor, codex);
            if (unit != null) armor.Unit = unit;
            armor.Punctured += pierced =>
                MelonLogger.Msg(Tag + " pierced: '" + zoneName + "' (" + mesh.name + ")");
            return go;
        }

        /// <summary>
        /// The game's own armour codex for a zone, so spall, ricochet and long-rod behaviour come from the
        /// vanilla material rather than from defaults. Thickness stays ours: every piece is RHAe, which is the
        /// one mode where the codex multiplier is not applied.
        /// </summary>
        private static readonly FieldInfo ArmorTypeField =
            typeof(UniformArmor).GetField("_armorType", BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly Dictionary<string, ArmorCodexScriptable> CodexCache =
            new Dictionary<string, ArmorCodexScriptable>(StringComparer.OrdinalIgnoreCase);

        private static ArmorCodexScriptable CodexFor(string zoneName)
        {
            string fragment = CodexFragmentFor(zoneName);
            if (fragment == null) return null;

            ArmorCodexScriptable cached;
            if (CodexCache.TryGetValue(fragment, out cached)) return cached;

            ArmorCodexScriptable found = null;
            foreach (ArmorCodexScriptable codex in Resources.FindObjectsOfTypeAll<ArmorCodexScriptable>())
            {
                if (codex == null || codex.ArmorType == null || codex.ArmorType.Name == null) continue;
                if (codex.ArmorType.Name.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) < 0) continue;
                found = codex;
                break;
            }
            CodexCache[fragment] = found;
            return found;
        }

        private static string CodexFragmentFor(string zoneName)
        {
            string n = zoneName.ToLowerInvariant();
            if (n.Contains("turret front")) return "turret front";
            if (n.Contains("turret side")) return "turret side";
            if (n.Contains("mantlet")) return "mantlet";
            if (n.Contains("spall")) return "spall";
            if (n.Contains("firewall")) return "steel";
            if (n.Contains("hull") || n.Contains("rear") || n.Contains("roof")) return "hull array";
            return null;
        }

        /// <summary>
        /// The layer the game's own armour colliders live on. Read from a donor plate rather than hard-coded
        /// so a change to the project's layer table does not silently put the pieces out of reach of the shot
        /// raycast - which is the whole failure mode this guards against: the projectile raycasts against
        /// ConstantsAndInfoManager.LiveRoundLayerMask, and a plate on a layer outside that mask is simply not
        /// there as far as a round is concerned.
        ///
        /// The template is cloned from the donor prefab, which has no armour pieces until the game's art pass
        /// runs on a live unit, so the read has to be remembered from the first unit that had them. Falling
        /// back to a guessed 8 for every templated spawn was leaving the player's own vehicle on a layer
        /// nothing may be looking at.
        /// </summary>
        private static int _donorArmorLayer = -1;

        private static int ArmorLayer(GameObject unitRoot)
        {
            foreach (UniformArmor armor in unitRoot.GetComponentsInChildren<UniformArmor>(true))
            {
                if (armor == null) continue;
                if (armor.GetComponent<Collider>() == null) continue;
                int layer = armor.gameObject.layer;
                if (layer != _donorArmorLayer)
                {
                    _donorArmorLayer = layer;
                    MelonLogger.Msg(Tag + " armour layer " + layer + " (read from a donor plate)");
                }
                return layer;
            }

            if (_donorArmorLayer >= 0)
            {
                MelonLogger.Msg(Tag + " no donor plate on this unit yet; armour layer " + _donorArmorLayer +
                                " remembered from an earlier one");
                return _donorArmorLayer;
            }

            MelonLogger.Warning(Tag + " no donor armour collider found anywhere yet, armour pieces use layer 8");
            return 8;
        }

        /// <summary>One convex collider mesh from one exterior export group.</summary>
        private static Mesh BuildMesh(ObjGeometry geometry, Vector3 anchor)
        {
            if (geometry.Positions.Length < 3 || geometry.Triangles.Length < 3) return null;

            var vertices = new Vector3[geometry.Positions.Length];
            for (int i = 0; i < vertices.Length; i++)
                vertices[i] = geometry.Positions[i] - anchor;
            return BuildMesh(geometry.ObjectName, vertices, geometry.Triangles);
        }

        /// <summary>One convex collider mesh from one x-ray module.</summary>
        private static Mesh BuildMesh(LpXrayParts.Part part, Vector3 anchor)
        {
            if (part.Positions.Length < 3 || part.Triangles.Length < 3) return null;

            var vertices = new Vector3[part.Positions.Length];
            for (int i = 0; i < vertices.Length; i++)
                vertices[i] = part.Positions[i] - anchor;
            return BuildMesh(part.Name, vertices, part.Triangles);
        }

        private static Mesh BuildMesh(string name, Vector3[] vertices, int[] triangles)
        {
            var mesh = new Mesh
            {
                name = name,
                indexFormat = vertices.Length > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16,
            };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
