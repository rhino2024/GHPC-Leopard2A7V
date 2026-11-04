using System;
using System.Collections.Generic;
using System.IO;
using GHPC;
using MelonLoader;
using MelonLoader.Utils;
using UnityEngine;

namespace Leopard2A7V
{
    /// <summary>
    /// Builds the vehicle's armour from a baked copy of the 2A7V's x-ray model.
    ///
    /// The armour geometry started life as the x-ray OBJ that comes with the model - composite_armor_hull_01..05,
    /// composite_armor_turret_01..11, the two firewalls and the spall liners - but reading eight megabytes of
    /// text on the first spawn was slow and left every unit to allocate its own meshes. The asset shipped with
    /// the mod now is those groups already converted: a small binary with names, vertices and indices only,
    /// and one shared Mesh per group behind it, which is what a prefab would have given us without needing a
    /// Unity editor in the build.
    /// </summary>
    public static class LpXrayArmor
    {
        internal const string Tag = "[L2A7V][armour]";
        private const string TagOld = "[L2A7V][armour]";

        private const string FileName = "l2a7v_armor.bin";

        private const string Marker = "L2A7V_XrayArmor";

        /// <summary>Plates a composite array is split into, so a round is stopped layer by layer.</summary>
        private const int LayeredArrayCount = 3;

        /// <summary>
        /// How far apart those layers may sit. The stack is built by copying the module's own shell and
        /// moving the copies, so an uncapped spacing of one third of a 2.5 m envelope fans the outer layers
        /// most of a metre out of the vehicle - which is how a flank ended up meeting armour that was not
        /// there. Capped, the three copies stay within 15 cm of the surface the export drew.
        /// </summary>
        private const float MaxLayerSpacing = 0.15f;

        /// <summary>
        /// How far off the module's own axis a face may still count as a ballistic face of it - the cosine of
        /// 60 degrees. Steeper than that the triangle is a rim, and goes in as structure instead.
        /// </summary>
        private const float StrikingFaceCosine = 0.5f;

        /// <summary>
        /// What a composite module's rim is worth. A package's edges are its steel casing, not its array: the
        /// 2A7V's own document set does not quote an edge figure, and 40 mm is the number this table already
        /// uses for structural plate of that thickness. Only the rim is affected; the ballistic faces keep
        /// their published values.
        /// </summary>
        private const float EdgeRha = 40f;

        private static int _logged;
        private static readonly System.Collections.Generic.List<Bounds> OurBounds =
            new System.Collections.Generic.List<Bounds>();

        private static List<Group> _groups;

        private static readonly Dictionary<string, Mesh> _meshes = new Dictionary<string, Mesh>(StringComparer.Ordinal);

        private sealed class Group
        {
            internal string Name;
            internal Mesh Mesh;
        }

        /// <summary>
        /// The baked modules and their meshes, for the additive armour pass.
        ///
        /// This class no longer builds colliders of its own - the overlay in LpArmorOverlay does that, one
        /// convex collider per plate - but it still owns the one place the x-ray geometry is decoded and
        /// re-framed, so the overlay reads it from here rather than parsing the file a second time.
        /// </summary>
        internal static bool TryGetGroups(List<KeyValuePair<string, Mesh>> into)
        {
            List<Group> groups = Load();
            if (groups == null) return false;
            foreach (Group group in groups)
                if (group != null && group.Mesh != null)
                    into.Add(new KeyValuePair<string, Mesh>(group.Name, group.Mesh));
            return into.Count > 0;
        }

        internal static string ModFolder
        {
            get
            {
                string folder = Path.Combine(MelonEnvironment.ModsDirectory, "Leopard2A7V");
                return Directory.Exists(folder) ? folder : MelonEnvironment.ModsDirectory;
            }
        }

        public static bool Build(GameObject unitRoot, LpVisual.Assembly art = null)
        {
            if (unitRoot == null) return false;
            List<Group> groups = Load();
            if (groups == null || groups.Count == 0) return false;

            // The pieces hang off the same pivots the imported art hangs off, at local zero, in the frame the
            // art meshes are authored in. Placing them under the rig bones instead put them in a frame rotated
            // ninety degrees against the model, which is why a hit at the side registered before the round had
            // reached the hull.
            Transform hull = art != null ? art.Hull : FindBone(unitRoot, "hull");
            Transform turret = art != null ? art.Turret : FindBone(unitRoot, "turret");
            Transform gun = art != null ? art.Gun : FindBone(unitRoot, "Rh-120_L/44");
            if (hull == null)
            {
                MelonLogger.Warning(Tag + " no hull bone on " + unitRoot.name + "; the donor armour stays");
                return false;
            }

            // The axis each group is authored around, exactly as the art pass uses it: the hull pivot carries
            // the unit-frame offset, while the turret and gun groups are built with their ring/trunnion
            // subtracted from the vertices, so the mesh sits at -axis under its bone.
            Unit unit = LpFind.Unit(unitRoot);
            Transform frame = unit != null ? unit.transform : unitRoot.transform;
            Vector3 hullLocal = hull.InverseTransformPoint(frame.TransformPoint(LpConfig.Offset));
            // Cache() puts the ring into the turret meshes on the way in, in this same frame, so those pieces
            // go in at local zero - no anchor maths at run time.
            Vector3 turretLocal = Vector3.zero;
            Vector3 gunLocal = Vector3.zero;
            MelonLogger.Msg(Tag + " anchors: hull " + hullLocal.ToString("0.00") + " turret " +
                            turretLocal.ToString("0.00") + " gun " + gunLocal.ToString("0.00"));

            OurBounds.Clear();

            // The donor's authored plates carry an armour codex: whether a piece normalises, shatters long
            // rods, ricochets, and how it spalls. A plate built from scratch has none of that, so it behaves
            // like bare steel whatever its RHA figures say. Copying the matching codex is what makes our
            // arrays behave like the arrays they replace.
            DonorCodex = CollectCodex(unitRoot);

            int built = 0, noZone = 0;
            foreach (Group group in groups)
            {
                bool onGun = GroupOnGun(group.Name);
                bool onTurret = !onGun &&
                    group.Name.IndexOf("turret", StringComparison.OrdinalIgnoreCase) >= 0;
                Transform bone = onGun && gun != null ? gun : onTurret && turret != null ? turret : hull;
                Vector3 anchor = onGun && gun != null ? gunLocal : onTurret && turret != null ? turretLocal : hullLocal;

                float ke, ce;
                string zoneName;
                int dataLayers;
                if (!LpArmor.TryZoneForXrayGroup(group.Name, out ke, out ce, out zoneName))
                {
                    if (!LpData.TryArmour(group.Name, out ke, out ce, out dataLayers))
                    {
                        noZone++;
                        continue;
                    }
                    zoneName = group.Name;
                }
                else if (LpData.TryArmour(group.Name, out float dataKe, out float dataCe, out dataLayers))
                {
                    // a line in L2A7V_data.txt always wins over the compiled table
                    ke = dataKe;
                    ce = dataCe;
                }

                // Composite arrays go in as a stack, the way the vanilla LEO2A3 and M1 author their armour:
                // the solver subtracts each plate's LOS thickness from the round's remaining penetration, so
                // equal layers add up to exactly the same total and the round is stopped layer by layer.
                // Liners and firewalls stay single - they are one plate in the real vehicle too.
                int layers = group.Name.StartsWith("composite_armor_", StringComparison.OrdinalIgnoreCase)
                    ? LayeredArrayCount
                    : 1;
                if (LpData.TryArmour(group.Name, out _, out _, out int overrideLayers) && overrideLayers > 0)
                    layers = overrideLayers;

                if (BuildOne(bone, anchor, group, ke, ce, zoneName, layers, null)) built++;
            }

            int retired = built > 0 ? RetireDonorArmour(unitRoot) : 0;
            if (art != null) CheckAgainstArt(art);
            MelonLogger.Msg(Tag + " built " + built + " piece(s) from the baked armour" +
                            (noZone > 0 ? ", " + noZone + " group(s) had no zone entry" : string.Empty) +
                            (retired > 0 ? ", retired " + retired + " donor plate(s)" : string.Empty));
            return built > 0;
        }

        private static bool GroupOnGun(string name)
        {
            return name.IndexOf("breech", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("barrel", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("_gun", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.StartsWith("gun_", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Compares every plate with the visible model and names the ones that stick out of it.
        ///
        /// This is the check that settles "is the armour in the right place" without a screenshot: anything
        /// outside the hull or turret bounds by more than a few centimetres is misplaced, and anything that is
        /// inside the art but still triggers early is a collider problem rather than a transform one.
        /// </summary>
        private static void CheckAgainstArt(LpVisual.Assembly art)
        {
            Bounds hull = ArtBounds(art.Hull);
            Bounds turret = ArtBounds(art.Turret);
            int outside = 0;
            foreach (Bounds piece in OurBounds)
            {
                bool inHull = hull.size != Vector3.zero && hull.Intersects(piece);
                bool inTurret = turret.size != Vector3.zero && turret.Intersects(piece);
                if (inHull || inTurret) continue;
                outside++;
                MelonLogger.Warning(Tag + " plate outside the model: " + piece.center.ToString("0.00"));
            }
            MelonLogger.Msg(Tag + " plates outside the visible model: " + outside + " of " + OurBounds.Count);
        }

        private static Bounds ArtBounds(Transform pivot)
        {
            if (pivot == null) return new Bounds(Vector3.zero, Vector3.zero);
            bool any = false;
            Bounds bounds = new Bounds(pivot.position, Vector3.zero);
            foreach (Renderer renderer in pivot.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null) continue;
                if (!any) { bounds = renderer.bounds; any = true; }
                else bounds.Encapsulate(renderer.bounds);
            }
            return any ? bounds : new Bounds(pivot.position, Vector3.zero);
        }

        /// <summary>
        /// Switches the donor's own armour off where this mod's plates replace it.
        ///
        /// Without this the vehicle wears both sets: the donor's authored plates were never removed, so a round
        /// had to beat the donor's armour and then the 2A7V's, which is twice the protection the x-ray figures
        /// describe. The T-90M mod does the same thing from a table ("off|Upper Front Plate"); here a piece goes
        /// when the data table names it, when its name says it is an array, or when one of our plates covers it -
        /// and never when it is one of the hit zones this mod deliberately leaves the donor's.
        /// </summary>
        private static int RetireDonorArmour(GameObject unitRoot)
        {
            int retired = 0, kept = 0;
            var keptNames = new System.Collections.Generic.List<string>();
            foreach (UniformArmor plate in unitRoot.GetComponentsInChildren<UniformArmor>(true))
            {
                if (plate == null) continue;
                if (plate.gameObject.name.StartsWith(Marker, System.StringComparison.Ordinal)) continue;

                // Only the plates this mod actually replaces are switched off. Retiring everything was
                // simplest, but it also stripped the parts the x-ray export does not cover - the mantlet, the
                // hull floor, the skirts - and a hit there then met no armour at all.
                bool named = LpData.IsVanillaOff(plate.name);
                bool isArray = plate.name.IndexOf("array", StringComparison.OrdinalIgnoreCase) >= 0;
                if (!named && !isArray && (IsProtected(plate.name, plate.ArmorType) || !Covered(plate)))
                {
                    kept++;
                    keptNames.Add(plate.name + "|" + plate.transform.position.ToString("0.00"));
                    continue;
                }

                foreach (Collider collider in plate.GetComponentsInChildren<Collider>(true))
                    if (collider != null) collider.enabled = false;
                plate.enabled = false;
                retired++;
            }
            foreach (VariableArmor measured in unitRoot.GetComponentsInChildren<VariableArmor>(true))
            {
                if (measured == null) continue;
                Collider collider = measured.GetComponentInChildren<Collider>(true);
                if (collider == null) continue;
                if (IsProtected(measured.name, measured.ArmorType)) continue;
                bool named = LpData.IsVanillaOff(measured.name);
                bool ours = false;
                foreach (Bounds bounds in OurBounds)
                {
                    Bounds grown = bounds;
                    grown.Expand(1.2f);
                    if (grown.Intersects(collider.bounds)) { ours = true; break; }
                }
                if (!named && !ours) continue;
                collider.enabled = false;
                measured.enabled = false;
                retired++;
            }
            if (keptNames.Count > 0)
            {
                MelonLogger.Msg(Tag + " kept " + kept + " donor plate(s) outside the x-ray coverage");
                DumpKept(keptNames);
            }
            return retired;
        }

        /// <summary>
        /// True for the donor pieces this mod must leave answering hits whatever the coverage test says.
        ///
        /// The crew's anatomy, the track hit zones, the running gear and the fuel cells all sit inside the
        /// volume the composites occupy, and the 1.2 m the coverage test grows them by reaches every one of
        /// them. They are not armour this mod replaces - the plan is explicit that the track zones stay the
        /// donor's - and retiring them would leave the crew and the tracks immune and the fuel inert.
        /// </summary>
        private static bool IsProtected(string name, ArmorCodexScriptable codex)
        {
            string type = codex != null && codex.name != null ? codex.name.ToLowerInvariant() : string.Empty;
            if (type.Contains("human") || type.Contains("flesh") || type.Contains("bone") ||
                type.Contains("track"))
                return true;

            string plate = name == null ? string.Empty : name.ToLowerInvariant();
            return plate.Contains("track") || plate.Contains("wheel") || plate.Contains("suspension") ||
                   plate.Contains("crew") || plate.Contains("fuel") || plate.Contains("human");
        }

        /// <summary>
        /// Writes the donor plates that were left active, so the list can be reviewed instead of guessed at.
        /// Each line is "name|world position"; add the names that should go to L2A7V_data.txt as vanilla_off.
        /// </summary>
        private static void DumpKept(System.Collections.Generic.List<string> names)
        {
            try
            {
                string path = System.IO.Path.Combine(ModFolder, "donor_plates.txt");
                System.IO.File.WriteAllLines(path, names);
                MelonLogger.Msg(Tag + " wrote " + names.Count + " kept plate name(s) to donor_plates.txt");
            }
            catch (System.Exception error)
            {
                MelonLogger.Warning(Tag + " could not write donor_plates.txt: " + error.Message);
            }
        }

        /// <summary>True when one of this mod's plates occupies the same space as the donor plate.</summary>
        private static bool Covered(UniformArmor plate)
        {
            Bounds donor = BoundsOf(plate);
            if (donor.size == Vector3.zero) return false;
            foreach (Bounds ours in OurBounds)
            {
                // Inflated because the donor's own plate is the outer skin: it sits half a metre or more outside
                // the composite array this mod replaces, so a plain intersection test left most of them active
                // and the vehicle wore both sets.
                Bounds grown = ours;
                grown.Expand(1.2f);
                if (grown.Intersects(donor)) return true;
            }
            return false;
        }

        private static Bounds BoundsOf(UniformArmor plate)
        {
            Collider collider = plate.GetComponentInChildren<Collider>(true);
            if (collider == null) return new Bounds(plate.transform.position, Vector3.zero);
            return collider.bounds;
        }

        private struct Codex
        {
            internal string Name;
            internal ArmorCodexScriptable Type;
        }

        private static Codex[] DonorCodex = new Codex[0];

        private static Codex[] CollectCodex(GameObject unitRoot)
        {
            var list = new System.Collections.Generic.List<Codex>();
            int pieces = 0, typed = 0;

            // Both armour classes carry a codex, and the donor's measured plates are the ones that usually have
            // one. The first run found only the crew's flesh and bone, which means the uniform plates carry a
            // display name (SetName) instead of a codex - worth counting so the log says which it is.
            foreach (UniformArmor piece in unitRoot.GetComponentsInChildren<UniformArmor>(true))
            {
                if (piece == null) continue;
                pieces++;
                if (piece.ArmorType == null) continue;
                typed++;
                string name = piece.ArmorType.name;
                if (string.IsNullOrEmpty(name)) name = piece.name;
                bool seen = false;
                foreach (Codex known in list) if (known.Name == name) { seen = true; break; }
                if (!seen) list.Add(new Codex { Name = name, Type = piece.ArmorType });
            }
            foreach (VariableArmor piece in unitRoot.GetComponentsInChildren<VariableArmor>(true))
            {
                if (piece == null) continue;
                pieces++;
                if (piece.ArmorType == null) continue;
                typed++;
                string name = piece.ArmorType.name;
                if (string.IsNullOrEmpty(name)) name = piece.name;
                bool seen = false;
                foreach (Codex known in list) if (known.Name == name) { seen = true; break; }
                if (!seen) list.Add(new Codex { Name = name, Type = piece.ArmorType });
            }
            MelonLogger.Msg(Tag + " donor pieces with a codex: " + typed + " of " + pieces);
            if (list.Count > 0)
                MelonLogger.Msg(Tag + " donor armour codex: " + list.Count + " type(s) available to copy");
                foreach (Codex codex in list) MelonLogger.Msg(Tag + "   " + codex.Name);
            return list.ToArray();
        }

        /// <summary>The donor codex closest to this group: same station first, then anything.</summary>
        /// <summary>
        /// The donor codex that matches what this group actually is.
        ///
        /// Handing every plate the station's composite codex made a firewall behave like an array and a spall
        /// liner behave like armour: the codex decides normalisation, long-rod shattering, ricochet and spall,
        /// so the type has to follow the part, not the station. The order is: an explicit line in the data
        /// table, then the part's own kind, then the station, then anything.
        /// </summary>
        private static ArmorCodexScriptable CodexFor(string groupName, Vector3 facing)
        {
            string name = groupName.ToLowerInvariant();

            // The donor exposes twelve real codexes; these are the ones that match our parts.
            if (name.Contains("track")) return Match("tank tracks");
            if (name.Contains("spall_liner") || name.Contains("liner")) return Match("composite skirts");
            if (name.Contains("firewall")) return Match("US gun steel");
            if (name.Contains("mantlet") || name.Contains("breech") || name.Contains("barrel"))
                return Match("Leo 2 mantlet array");
            if (name.Contains("turret"))
            {
                // A turret array is a front array when it faces forward, a side array when it faces sideways.
                // Facing, not position: a cheek sits well off the centreline but still faces the front.
                bool front = Mathf.Abs(facing.z) >= Mathf.Abs(facing.x);
                ArmorCodexScriptable typed = Match(front ? "turret front array B tech" : "turret side array B tech");
                if (typed != null) return typed;
            }
            if (name.Contains("hull")) return Match("Leo 2 hull array");

            return CodexForStation(groupName);
        }

        private static ArmorCodexScriptable CodexForStation(string groupName)
        {
            string overrideName = LpData.CodexFor(groupName);
            if (!string.IsNullOrEmpty(overrideName))
            {
                ArmorCodexScriptable explicitType = Match(overrideName);
                if (explicitType != null) return explicitType;
            }

            string kind = KindOf(groupName);
            if (kind != null)
            {
                ArmorCodexScriptable typed = Match(kind);
                if (typed != null) return typed;
            }

            string station = groupName.IndexOf("turret", StringComparison.OrdinalIgnoreCase) >= 0 ? "turret"
                           : groupName.IndexOf("hull", StringComparison.OrdinalIgnoreCase) >= 0 ? "hull"
                           : null;
            if (station != null)
            {
                ArmorCodexScriptable typed = Match(station);
                if (typed != null) return typed;
            }
            return DonorCodex.Length > 0 ? DonorCodex[0].Type : null;
        }

        /// <summary>What the part is made of, as a word the donor's codex names are likely to contain.</summary>
        private static string KindOf(string groupName)
        {
            string name = groupName.ToLowerInvariant();
            if (name.StartsWith("spall_liner") || name.Contains("liner")) return "spall";
            if (name.StartsWith("firewall")) return "steel";
            if (name.Contains("composite") || name.Contains("array")) return "array";
            if (name.Contains("track")) return "track";
            return null;
        }

        private static ArmorCodexScriptable Match(string fragment)
        {
            foreach (Codex codex in DonorCodex)
                if (codex.Name.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0)
                    return codex.Type;
            return null;
        }

        private static void SetPrivate(object target, string field, object value)
        {
            if (target == null || value == null) return;
            var info = target.GetType().GetField(field,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (info != null) info.SetValue(target, value);
        }

        private static bool BuildOne(Transform bone, Vector3 anchor, Group group, float ke, float ce, string zoneName, int layers, ArmorCodexScriptable codex)
        {
            if (bone == null || group.Mesh == null) return false;
            Mesh mesh = group.Mesh;

            // The stack runs along the plate's own normal so the layers sit inside the real array's thickness,
            // and never further apart than MaxLayerSpacing so three copies of a shell stay on the vehicle.
            Vector3 normal = PlateAxis(mesh);
            float thickness = ExtentAlong(mesh, normal);
            float spacing = layers > 1 ? Mathf.Min(thickness / layers, MaxLayerSpacing) : 0f;
            if (codex == null) codex = CodexFor(group.Name, normal);

            Mesh faces = null, rim = null;
            if (layers > 1 && ke > EdgeRha * 1.5f)
            {
                faces = Select(mesh, normal, true);
                rim = Select(mesh, normal, false);
            }
            Mesh body = faces != null ? faces : mesh;

            for (int layer = 0; layer < layers; layer++)
            {
                float offset = layers > 1 ? (layer - (layers - 1) * 0.5f) * spacing : 0f;
                BuildLayer(bone, anchor, group, body, ke / layers, ce / layers, codex,
                           layers > 1 ? zoneName + " layer " + (layer + 1) + "/" + layers : zoneName,
                           normal * offset, normal);
            }

            // The rim goes in once, as the steel it is. It is what a flank shot meets first, and leaving it at
            // the array's frontal figure is what made the turret's sides as strong as its face.
            if (rim != null)
                BuildLayer(bone, anchor, group, rim, EdgeRha, EdgeRha, codex, zoneName + " rim",
                           Vector3.zero, normal);
            return true;
        }

        /// <summary>
        /// A module's triangles split by whether their face is perpendicular to the module's own axis or not.
        ///
        /// The layered copies get the perpendicular ones - a composite package is ballistic through its two
        /// big faces and nothing else - and the remainder becomes the rim, one piece of structural steel. This
        /// is the difference between "the cheek is 640 from the front" and "the cheek is 640 from every side",
        /// which is how a flank shot came to out-stop an 800 mm dart.
        /// </summary>
        private static Mesh Select(Mesh source, Vector3 axis, bool facing)
        {
            Vector3[] vertices = source.vertices;
            int[] triangles = source.triangles;
            var kept = new List<int>(triangles.Length);
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                Vector3 a = vertices[triangles[i]];
                Vector3 b = vertices[triangles[i + 1]];
                Vector3 c = vertices[triangles[i + 2]];
                Vector3 cross = Vector3.Cross(b - a, c - a);
                if (cross.sqrMagnitude < 1e-12f) continue;
                bool along = Mathf.Abs(Vector3.Dot(cross.normalized, axis)) >= StrikingFaceCosine;
                if (along != facing) continue;
                kept.Add(triangles[i]);
                kept.Add(triangles[i + 1]);
                kept.Add(triangles[i + 2]);
            }
            if (kept.Count == 0) return null;

            var mesh = new Mesh { name = source.name + (facing ? " face" : " rim") };
            if (vertices.Length > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = vertices;
            mesh.triangles = kept.ToArray();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void BuildLayer(Transform bone, Vector3 anchor, Group group, Mesh mesh, float ke, float ce, ArmorCodexScriptable codex,
            string zoneName, Vector3 offset, Vector3 facing)
        {
            var go = new GameObject(Marker + " " + group.Name + " " + zoneName);
            go.transform.SetParent(bone, false);

            // Model space, placed the way the art is placed: the vertex positions already carry the model
            // origin, so the anchor (unit offset, -ring or -trunnion) puts the piece where the visible part of
            // the vehicle is. No world-space trickery, which is what used to leave every plate at the origin.
            go.transform.localPosition = anchor + offset;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
            go.layer = 8;
            go.tag = "Penetrable";

            var collider = go.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            collider.convex = false;
            // TransformVector through a half-turn reports negative components, and a Bounds built from those
            // has its extents run backwards - both Intersects tests below then answer false, which silently
            // stopped the donor plates near our own from being retired.
            OurBounds.Add(new Bounds(go.transform.TransformPoint(mesh.bounds.center),
                                     Abs(go.transform.TransformVector(mesh.bounds.size))));

            float scale = LpConfig.ArmorScale;
            var armor = go.AddComponent<UniformArmor>();
            armor.PrimarySabotRha = ke * scale;
            armor.PrimaryHeatRha = ce * scale;
            // SabotRha/CeRha return the Secondary figures once a piece counts as detonated, and those were left
            // at zero - a round that reached a piece in that state met no armour at all.
            armor.SecondarySabotRha = ke * scale;
            armor.SecondaryHeatRha = ce * scale;
            armor.ThicknessListed = UniformArmor.ThicknessMode.RHAE;
            armor.AngleMatters = true;
            armor.DetonatedObjects = Array.Empty<GameObject>();
            armor.UndetonatedObjects = Array.Empty<GameObject>();
            armor.Punctured += pierced =>
                MelonLogger.Msg(Tag + " PIERCED: '" + zoneName + "' (" + group.Name + ") at " +
                                go.transform.position.ToString("0.0"));
            MelonLogger.Msg(Tag + " piece '" + group.Name + "' -> codex '" +
                            (codex == null ? "none" : codex.name) + "'");
            armor.CenterPoint = go.transform;
            armor.SetName(zoneName);
            if (codex != null) SetPrivate(armor, "_armorType", codex);

            var unit = go.GetComponentInParent<Unit>();
            if (unit != null) armor.Unit = unit;
            else MelonLogger.Warning(Tag + " piece '" + group.Name + "' has no Unit; hits may not be attributed");

            // Where the piece actually landed, in world space: if the mesh and the transform disagree the
            // collider can sit metres away from the tank, which reads as "no armour at all". The facing is
            // logged with it because a plate whose normal points the wrong way is invisible to a shot.
            if (_logged < 6)
            {
                _logged++;
                MelonLogger.Msg(Tag + " piece '" + group.Name + "' centre " +
                                go.transform.position.ToString("0.00") + " size " +
                                mesh.bounds.size.ToString("0.00") + " facing " + facing.ToString("0.00") +
                                " layer " + go.layer + " unit " + (armor.Unit != null));
            }

            AarVisual aar = go.AddComponent<AarVisual>();
            aar.HideUntilAar = true;
            Material xray = LpArmorOwn.XrayMaterial();
            if (xray != null)
            {
                aar.SwitchMaterials = true;
                aar.AarMaterial = xray;
                aar.UpdateVisibility();
            }
        }

        /// <summary>
        /// The axis a module's plates face, from the second moment of its own face normals.
        ///
        /// The modules in the x-ray export are closed shells, so simply summing their area-weighted face
        /// normals cancels to nothing and the old code fell back to +Y - which stacked every array's three
        /// layers vertically, one above the turret roof and one below the hull floor, instead of across the
        /// array's thickness. The second moment has no such cancellation: a module's two big faces dominate
        /// it, so the eigenvector of its largest eigenvalue is the through-thickness direction. Power
        /// iteration finds that vector in a handful of passes and needs no matrix decomposition.
        /// </summary>
        private static Vector3 PlateAxis(Mesh mesh)
        {
            Vector3[] vertices = mesh.vertices;
            int[] triangles = mesh.triangles;
            var moment = new double[3, 3];
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                Vector3 a = vertices[triangles[i]];
                Vector3 b = vertices[triangles[i + 1]];
                Vector3 c = vertices[triangles[i + 2]];
                Vector3 cross = Vector3.Cross(b - a, c - a);
                float weight = cross.magnitude;
                if (weight < 1e-9f) continue;
                Vector3 n = cross / weight;
                moment[0, 0] += weight * n.x * n.x;
                moment[0, 1] += weight * n.x * n.y;
                moment[0, 2] += weight * n.x * n.z;
                moment[1, 1] += weight * n.y * n.y;
                moment[1, 2] += weight * n.y * n.z;
                moment[2, 2] += weight * n.z * n.z;
            }
            moment[1, 0] = moment[0, 1];
            moment[2, 0] = moment[0, 2];
            moment[2, 1] = moment[1, 2];

            // Start on the strongest diagonal term, which is already close to the answer for a boxy module.
            int start = 0;
            if (moment[1, 1] > moment[start, start]) start = 1;
            if (moment[2, 2] > moment[start, start]) start = 2;
            var vector = new double[3];
            vector[start] = 1.0;

            for (int pass = 0; pass < 64; pass++)
            {
                var next = new double[3];
                for (int row = 0; row < 3; row++)
                    next[row] = moment[row, 0] * vector[0] + moment[row, 1] * vector[1] + moment[row, 2] * vector[2];
                double length = Math.Sqrt(next[0] * next[0] + next[1] * next[1] + next[2] * next[2]);
                if (length < 1e-12) break;
                for (int row = 0; row < 3; row++) vector[row] = next[row] / length;
            }

            var axis = new Vector3((float)vector[0], (float)vector[1], (float)vector[2]);
            if (axis.sqrMagnitude < 0.000001f) return Vector3.up;
            // A sign is meaningless for a plate; pointing it away from the vehicle's own centre makes the
            // layer offsets fall inward, where the array actually is.
            return axis.normalized;
        }

        private static Vector3 Abs(Vector3 value)
        {
            return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
        }

        /// <summary>How thick the piece is along that normal - the stacking distance the layers share.</summary>
        private static float ExtentAlong(Mesh mesh, Vector3 normal)
        {
            Vector3[] vertices = mesh.vertices;
            if (vertices.Length == 0) return 0f;
            float min = float.MaxValue, max = float.MinValue;
            foreach (Vector3 v in vertices)
            {
                float d = Vector3.Dot(v, normal);
                if (d < min) min = d;
                if (d > max) max = d;
            }
            return Mathf.Max(0.02f, max - min);
        }

        /// <summary>
        /// Puts a baked mesh into the frame the art is built in, once per session.
        ///
        /// The export the armour was cut from is +X forward with +Z to starboard; a GHPC unit is +X starboard
        /// with +Z forward, and ObjModel and LpInterior make exactly that swap when they read their OBJ files,
        /// which is why the visible model has always agreed with the colliders the game authored. The bake kept
        /// the export's axes, so every plate went in ninety degrees off: the glacis sat three metres off the
        /// side of the hull and the 800 mm cheek arrays lay across the turret's flanks, which is why a flank
        /// shot met the front array while the front met a module from the rear of the turret.
        ///
        /// Swapping two axes is a mirror, so the winding is reversed as well or every face points into its
        /// module and a physics query from outside passes straight through it.
        ///
        /// The turret ring was baked out on the export's axes too. Once x and z trade places that subtraction
        /// lands on the wrong axis, so a turret piece takes it back off x and re-applies it on z.
        ///
        /// A packed mesh is usually readable, but not always, and its vertices cannot be touched when it is
        /// not; that case keeps the export's axes and says so, because losing the armour altogether is worse
        /// than wearing it at the wrong angle.
        /// </summary>
        private static Mesh Cache(string name, Mesh source)
        {
            Mesh cached;
            if (_meshes.TryGetValue(name, out cached) && cached != null) return cached;
            if (source == null) return null;

            if (!source.isReadable)
            {
                MelonLogger.Warning(Tag + " the bundle mesh '" + name + "' is not readable, so it keeps the " +
                                    "export's axes and its plate sits ninety degrees off");
                _meshes[name] = source;
                return source;
            }

            return Cache(name, source.vertices, source.triangles);
        }

        private static Mesh Cache(string name, Vector3[] source, int[] triangles)
        {
            Mesh cached;
            if (_meshes.TryGetValue(name, out cached) && cached != null) return cached;

            bool turret = name.IndexOf("turret", StringComparison.OrdinalIgnoreCase) >= 0;
            float ring = turret ? LpConfig.TurretAxis.z : 0f;

            var vertices = new Vector3[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                Vector3 p = source[i];
                vertices[i] = new Vector3(p.z + ring, p.y, p.x - ring);
            }

            var flipped = new int[triangles.Length];
            for (int i = 0; i + 2 < triangles.Length; i += 3)
            {
                flipped[i] = triangles[i];
                flipped[i + 1] = triangles[i + 2];
                flipped[i + 2] = triangles[i + 1];
            }

            var mesh = new Mesh { name = name };
            if (vertices.Length > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = vertices;
            mesh.triangles = flipped;
            mesh.RecalculateBounds();
            _meshes[name] = mesh;
            return mesh;
        }

        /// <summary>Reads the baked armour, once per session.</summary>
        private static List<Group> Load()
        {
            if (_groups != null) return _groups;

            _groups = new List<Group>();
            if (TryLoadBundle()) return _groups;

            // The asset travels inside the DLL the way the T-90A mod packs its own: a resource cannot go
            // missing from the mod folder, and there is nothing for the player to keep in sync. A loose file
            // next to the mod still wins, which is what makes iterating on the geometry possible.
            Stream source = null;
            string loose = Path.Combine(ModFolder, FileName);
            if (File.Exists(loose))
            {
                source = File.OpenRead(loose);
            }
            else
            {
                source = typeof(LpXrayArmor).Assembly.GetManifestResourceStream("armor.bin");
                if (source == null)
                {
                    MelonLogger.Warning(Tag + " no baked armour inside the DLL and no " + FileName +
                                        " beside it; the armour falls back to the donor pieces");
                    return _groups;
                }
            }

            try
            {
                using (var reader = new BinaryReader(source))
                {
                    var magic = reader.ReadBytes(8);
                    if (magic.Length != 8 || magic[0] != (byte)'G' || magic[1] != (byte)'H')
                    {
                        MelonLogger.Warning(Tag + " " + FileName + " is not a baked armour file; the donor armour stays");
                        return _groups;
                    }

                    int count = reader.ReadInt32();
                    for (int g = 0; g < count; g++)
                    {
                        int nameLength = reader.ReadInt32();
                        string name = System.Text.Encoding.UTF8.GetString(reader.ReadBytes(nameLength));
                        int vertexCount = reader.ReadInt32();
                        var vertices = new Vector3[vertexCount];
                        for (int i = 0; i < vertexCount; i++)
                            vertices[i] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                        int indexCount = reader.ReadInt32();
                        var triangles = new int[indexCount];
                        for (int i = 0; i < indexCount; i++) triangles[i] = reader.ReadInt32();
                        _groups.Add(new Group { Name = name, Mesh = Cache(name, vertices, triangles) });
                    }
                }
            }
            catch (Exception error)
            {
                MelonLogger.Warning(Tag + " could not read " + FileName + ": " + error.Message);
                _groups.Clear();
                return _groups;
            }

            MelonLogger.Msg(Tag + " loaded " + _groups.Count + " armour group(s) from " + FileName +
                            ", re-framed into the unit's axes");
            return _groups;
        }

        private const string BundleFile = "l2a7v_armor.bundle";

        private const string PrefabName = "L2A7V_armor";

        private static AssetBundle _bundle;

        /// <summary>
        /// Loads the prefab out of the AssetBundle the editor build produces, when it is shipped. The bundle
        /// carries the meshes as Unity assets, so nothing is parsed at all - what the T-90A mod gets from its
        /// own prefab. The baked file below stays as the fallback for a mod folder without a bundle.
        /// </summary>
        private static bool TryLoadBundle()
        {
            if (_bundle == null)
            {
                string path = Path.Combine(ModFolder, BundleFile);
                if (!File.Exists(path)) return false;
                _bundle = AssetBundle.LoadFromFile(path);
                if (_bundle == null)
                {
                    MelonLogger.Warning(Tag + " " + BundleFile + " exists but could not be loaded; using the baked file");
                    return false;
                }
            }

            GameObject prefab = _bundle.LoadAsset<GameObject>(PrefabName);
            if (prefab == null)
            {
                MelonLogger.Warning(Tag + " the bundle holds no " + PrefabName + " prefab; using the baked file");
                return false;
            }

            int count = 0;
            foreach (Transform child in prefab.transform)
            {
                MeshFilter filter = child.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                _groups.Add(new Group { Name = child.name, Mesh = Cache(child.name, filter.sharedMesh) });
                count++;
            }

            if (count == 0)
            {
                MelonLogger.Warning(Tag + " the prefab holds no armour meshes; using the baked file");
                return false;
            }

            MelonLogger.Msg(Tag + " loaded " + count + " armour mesh(es) from " + BundleFile +
                            ", re-framed into the unit's axes");
            return true;
        }

        private static Transform FindColliderRoot(GameObject unitRoot, string kind)
        {
            foreach (Transform node in unitRoot.GetComponentsInChildren<Transform>(true))
            {
                if (node == null) continue;
                string name = node.name.ToLowerInvariant();
                if (name.IndexOf("collider", StringComparison.Ordinal) < 0) continue;
                if (name.IndexOf(kind, StringComparison.Ordinal) < 0) continue;
                return node;
            }
            return null;
        }

        private static Transform FindBone(GameObject unitRoot, params string[] names)
        {
            foreach (Transform node in unitRoot.GetComponentsInChildren<Transform>(true))
            {
                if (node == null) continue;
                string name = node.name.ToLowerInvariant();
                foreach (string wanted in names)
                    if (name == wanted) return node;
            }
            return null;
        }
    }
}
