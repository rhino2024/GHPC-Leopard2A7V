using System;
using System.Collections.Generic;
using System.Text;
using GHPC;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace Leopard2A7V
{
    /// <summary>
    /// Marks one composite plate with the protection it should have. It rides on the piece's own
    /// GameObject, so a unit instantiated from the converted template carries the values with it.
    /// </summary>
    public sealed class LpArmorZone : MonoBehaviour
    {
        public float Ke;
        public float Ce;
    }

    /// <summary>
    /// Puts the 2A7V's protection in place of the LEO2A3's.
    ///
    /// The donor measures its composites by raycasting the plate's own mesh and multiplying by the layer's
    /// ArmorType, so a piece defends whatever thickness the 1979 hull happens to be drawn at (its glacis
    /// comes out at 170 mm of RHAe, a B-technology hull). The 2A7V is armed with D-technology arrays whose
    /// published protection is much higher, and GHPC multiplies a stored number by 1/cos(obliquity) at the
    /// hit, so a table that simply stored the published figure would hand the glacis 1.67 times it against a
    /// level shot. Each zone therefore holds the published protection divided by the obliquity of that
    /// plate's own face - the front glacis face sits 36.9 deg above horizontal (normal +0.6/+0.8 in the x-ray
    /// export, ModSources/tools/wt_armor_measure_out.txt), the cheek face 37 deg off the frontal axis - which
    /// is why the glacis reads 360 and still stops 600 against a level shot.
    ///
    /// The published figures are the ones open sources agree on for this mark: about 600 mm of glacis and
    /// 800 mm of turret front against a long rod, 1200 and 1400 against a shaped charge, the mantlet the
    /// turret's weak line at 350/700. Zones where the x-ray module wraps a whole region (the hull_02/03/05
    /// side strakes, turret_04/05/06/08, the roof) never had a plate thickness to scale, and those take the
    /// real-world 2A7V plate instead; their comment says so.
    ///
    /// UniformArmor pieces take the number directly. VariableArmor pieces cannot - their thickness is
    /// computed per hit - so they carry an LpArmorZone and the Harmony postfixes answer for them. Physical
    /// thickness requests (spall cone geometry, impact effects) are passed through untouched so only the
    /// ballistic answer changes. LpConfig.ArmorCoefficient scales every number on its way in.
    /// </summary>
    public static class LpArmor
    {
        internal sealed class Zone
        {
            public float Ke;
            public float Ce;
            public string Source;
        }

        /// <summary>
        /// Keyed by the piece's own GameObject name, because several composite sections share a display name
        /// ("turret cheek composite" is used by three different plates).
        /// </summary>
        private static readonly Dictionary<string, Zone> Table = new Dictionary<string, Zone>(StringComparer.Ordinal)
        {
            // ---- hull front -------------------------------------------------------------
            // hull_01 is the 629 mm front hull composite module (D-technology array). Published protection for
            // the 2A7V's glacis is ~600 mm against a long rod and ~1200 against a shaped charge; the face is
            // 36.9 deg above horizontal, so a level shot travels 1.67x deeper and the perpendicular figure GHPC
            // wants is 600/1.67 = 360 and 1200/1.67 = 720.
            { "upper glacis array", new Zone { Ke = 360f, Ce = 720f, Source = "hull_01 629mm, real 600/1200 frontal" } },

            // The x-ray draws one glacis package (hull_01 spans y 0.65..1.46, i.e. both bands), and the donor
            // splits it into an upper and a lower plate that no round crosses both of, so the lower band takes
            // the same module.
            { "lower glacis array", new Zone { Ke = 360f, Ce = 720f, Source = "hull_01 629mm, real 600/1200 frontal" } },

            // Below the composite the nose is a plain plate; the donor measures it at 64 mm and the 2A7V keeps
            // a comparable one.
            { "lower front plate", new Zone { Ke = 70f, Ce = 70f, Source = "real 70mm steel nose plate" } },

            // ---- hull sides -------------------------------------------------------------
            // hull_02/03/05 are the side and deck strakes: 292 mm tall boxes running the length of the upper
            // hull, so their extents describe the applique's height, not what a round travels through. The
            // 2A7V's side elements are quoted against 30 mm APFSDS, which is ~120 mm of rolled steel.
            { "front left array", new Zone { Ke = 120f, Ce = 250f, Source = "hull_03 strake, real 120mm KE" } },
            { "front right array", new Zone { Ke = 120f, Ce = 250f, Source = "hull_03 strake, real 120mm KE" } },
            { "skirt left", new Zone { Ke = 120f, Ce = 250f, Source = "hull_05 strake, real 120mm KE" } },
            { "skirt right", new Zone { Ke = 120f, Ce = 250f, Source = "hull_05 strake, real 120mm KE" } },

            // The hull's own side walls sit under that applique and the donor draws them as boxes (147 mm for
            // the lower band is a box extent); the plates themselves are ~50 mm.
            { "hull left upper", new Zone { Ke = 50f, Ce = 50f, Source = "real 50mm hull side" } },
            { "hull right upper", new Zone { Ke = 50f, Ce = 50f, Source = "real 50mm hull side" } },
            { "hull left lower", new Zone { Ke = 50f, Ce = 50f, Source = "real 50mm hull side" } },
            { "hull right lower", new Zone { Ke = 50f, Ce = 50f, Source = "real 50mm hull side" } },

            // The x-ray's deck band (hull_02, y 1.35..1.63) sits over the transmission; a vertical ray crosses
            // the band plus the plate, which the donor already measures at 105 mm. Credit the 2A7V's thicker
            // deck without claiming the whole box.
            { "hull roof front", new Zone { Ke = 150f, Ce = 150f, Source = "hull_02 band, real 150mm deck" } },
            { "sponson left", new Zone { Ke = 40f, Ce = 40f, Source = "real 40mm sponson" } },
            { "sponson right", new Zone { Ke = 40f, Ce = 40f, Source = "real 40mm sponson" } },
            { "hull floor", new Zone { Ke = 30f, Ce = 30f, Source = "real 30mm floor" } },
            { "hull floor front", new Zone { Ke = 40f, Ce = 40f, Source = "real 40mm driver's floor" } },
            { "driver's hatch", new Zone { Ke = 20f, Ce = 20f, Source = "real 20mm hatch" } },
            { "hull rear", new Zone { Ke = 40f, Ce = 40f, Source = "real 40mm rear plate" } },
            { "engine deck", new Zone { Ke = 30f, Ce = 30f, Source = "real 30mm deck" } },
            { "rear grill", new Zone { Ke = 15f, Ce = 15f, Source = "real 15mm grill" } },

            // ---- turret front -----------------------------------------------------------
            // The front turret is three stacked modules in the x-ray: 1142 mm left, 809 mm right outer,
            // 1200 mm right inner. The left cheek is a single plate in the donor and the right one is two,
            // so the right shares the left's total rather than doubling it. Published protection for the
            // 2A7V's turret front is ~800 mm against a long rod and ~1400 against a shaped charge; the cheek
            // face is 37 deg off the frontal axis, so the perpendicular figures are 800/1.25 and 1400/1.25.
            { "cheek array left A", new Zone { Ke = 640f, Ce = 1120f, Source = "turret_03 1142mm, real 800/1400 frontal" } },
            { "cheek array right A", new Zone { Ke = 320f, Ce = 560f, Source = "half of turret_03, real 800/1400 frontal" } },
            { "cheek array right B", new Zone { Ke = 320f, Ce = 560f, Source = "half of turret_03, real 800/1400 frontal" } },

            // The wedge's outer steel faces, drawn 52 mm and 28 mm on the donor; the 2A7V's are the same
            // shape but the array behind them carries the protection, so these stay plain plate.
            { "left cheek plate", new Zone { Ke = 60f, Ce = 60f, Source = "real 60mm cheek face" } },
            { "right cheek plate", new Zone { Ke = 60f, Ce = 60f, Source = "real 60mm cheek face" } },
            { "front left plate", new Zone { Ke = 40f, Ce = 40f, Source = "real 40mm turret face" } },
            { "front right plate", new Zone { Ke = 40f, Ce = 40f, Source = "real 40mm turret face" } },

            // turret_10 is the 515 mm mantlet block, the turret's weak line: ~350 mm against a long rod and
            // ~700 against a shaped charge, deliberately well under the cheeks.
            { "mantlet", new Zone { Ke = 350f, Ce = 700f, Source = "turret_10 515mm, real 350/700" } },

            // ---- turret sides, roof and bustle ------------------------------------------
            // turret_04/05/06/08 fill the turret flanks: turret_04 is an 862 mm whole-turret box and the two
            // rear plates are only 123/106 mm, so the module depths there are not comparable. The 2A7V's side
            // elements plus the casting give roughly 260 mm against a rod.
            { "array left", new Zone { Ke = 260f, Ce = 340f, Source = "turret_04 flank, real 260mm KE" } },
            { "array right", new Zone { Ke = 260f, Ce = 340f, Source = "turret_04 flank, real 260mm KE" } },

            // The x-ray has no roof module; the donor draws 81 mm and 72 mm. The 2A7V added armour over the
            // crew positions, nothing over the bustle.
            { "turret roof front", new Zone { Ke = 60f, Ce = 60f, Source = "real 60mm roof" } },
            { "turret roof mid", new Zone { Ke = 50f, Ce = 50f, Source = "real 50mm roof" } },
            { "commander's hatch", new Zone { Ke = 30f, Ce = 30f, Source = "real 30mm hatch" } },
            { "commander's sight", new Zone { Ke = 30f, Ce = 30f, Source = "real 30mm PERI housing" } },
            { "commander's sight superstructure", new Zone { Ke = 30f, Ce = 30f, Source = "real 30mm ring" } },
            { "loader's hatch", new Zone { Ke = 25f, Ce = 25f, Source = "real 25mm hatch" } },
            { "loader's hatch superstructure", new Zone { Ke = 20f, Ce = 20f, Source = "real 20mm ring" } },
            { "sight front armour", new Zone { Ke = 20f, Ce = 20f, Source = "real 20mm sight cover" } },
            { "sight channel surfaces", new Zone { Ke = 15f, Ce = 15f, Source = "real 15mm channel" } },
            { "ballistic glass", new Zone { Ke = 20f, Ce = 20f, Source = "real 20mm window" } },

            // The donor's bustle is a 12 mm box with a 28 mm rear plate; the 2A7V's racks are armoured because
            // the ammunition lives there.
            { "bustle armour", new Zone { Ke = 30f, Ce = 30f, Source = "real 30mm bustle wall" } },
            { "bustle rear", new Zone { Ke = 40f, Ce = 40f, Source = "real 40mm bustle rear" } },
            { "bustle rack inner", new Zone { Ke = 30f, Ce = 30f, Source = "real 30mm rack wall" } },
            { "bustle rack outter", new Zone { Ke = 30f, Ce = 30f, Source = "real 30mm rack wall" } },
            { "turret rear right", new Zone { Ke = 30f, Ce = 30f, Source = "real 30mm turret rear" } },
            { "turret rear right inner", new Zone { Ke = 30f, Ce = 30f, Source = "real 30mm turret rear" } },

            // turret_10 sits forward of the hydraulic compartment; firewall_armor_01 is the 418 mm block behind
            // the breech, which the donor models as thin walls. Credit the walls, not the block.
            { "hydraulic compartment wall", new Zone { Ke = 30f, Ce = 30f, Source = "real 30mm bulkhead" } },
            { "hydraulic compartment roof", new Zone { Ke = 15f, Ce = 15f, Source = "real 15mm lid" } },
            { "hydraulic compartment inner wall", new Zone { Ke = 15f, Ce = 15f, Source = "real 15mm bulkhead" } },
        };

        private static bool _installed;

        public static void Install(HarmonyLib.Harmony harmony)
        {
            if (_installed) return;
            _installed = true;

            Patch(harmony, "GetNormalThickness", nameof(NormalPostfix));
            Patch(harmony, "GetLosThickness", nameof(LosPostfix));
            Patch(harmony, "SabotRha", nameof(SabotPostfix), property: true);
            Patch(harmony, "HeatRha", nameof(HeatPostfix), property: true);
            Patch(harmony, "SimpleRha", nameof(SimplePostfix), property: true);
        }

        private static void Patch(HarmonyLib.Harmony harmony, string member, string postfix, bool property = false)
        {
            System.Reflection.MethodInfo target = property
                ? AccessTools.PropertyGetter(typeof(VariableArmor), member)
                : AccessTools.Method(typeof(VariableArmor), member);
            if (target == null)
            {
                MelonLogger.Error("[L2A7V] armour hook target VariableArmor." + member +
                                  " not found; that layer keeps the donor value");
                return;
            }
            harmony.Patch(target, postfix: new HarmonyMethod(typeof(LpArmor), postfix));
        }

        private static readonly HashSet<string> _Attempted = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> _UnmatchedReported = new HashSet<string>(StringComparer.Ordinal);

        public static bool Apply(GameObject unitRoot)
        {
            if (!LpConfig.WtArmor || unitRoot == null) return true;
            if (_Reported.Contains(unitRoot.name)) return true;
            if (_Attempted.Add(unitRoot.name))
            {
                MelonLogger.Msg("[L2A7V] armour pass attempt on " + unitRoot.name + ": " +
                                unitRoot.GetComponentsInChildren<UniformArmor>(true).Length + " uniform + " +
                                unitRoot.GetComponentsInChildren<VariableArmor>(true).Length +
                                " raycast-measured pieces present, coefficient x" +
                                LpConfig.ArmorScale.ToString("0.00"));
            }

            var used = new HashSet<string>(StringComparer.Ordinal);
            var sb = new StringBuilder();
            int uniform = 0, measured = 0;
            float scale = LpConfig.ArmorScale;

            UniformArmor[] stored = unitRoot.GetComponentsInChildren<UniformArmor>(true);
            foreach (UniformArmor armor in stored)
            {
                Zone zone;
                string via;
                if (!TryMatch(armor.transform.name, armor.Name, out zone, used, out via)) continue;
                float ke = zone.Ke * scale, ce = zone.Ce * scale;
                sb.Append("\n[L2A7V]   uniform '").Append(armor.transform.name)
                  .Append("' [").Append(via).Append("] ").Append(armor.PrimarySabotRha.ToString("0")).Append('/')
                  .Append(armor.PrimaryHeatRha.ToString("0"))
                  .Append(" -> ").Append(ke.ToString("0")).Append('/').Append(ce.ToString("0"))
                  .Append(" (").Append(zone.Source).Append(')');
                armor.PrimarySabotRha = ke;
                armor.PrimaryHeatRha = ce;
                // RHAE mode stops the stored number from being scaled by the codex a second time.
                armor.ThicknessListed = UniformArmor.ThicknessMode.RHAE;
                armor.AngleMatters = true;
                uniform++;
            }

            VariableArmor[] raycast = unitRoot.GetComponentsInChildren<VariableArmor>(true);
            foreach (VariableArmor armor in raycast)
            {
                Zone zone;
                string via;
                if (!TryMatch(armor.transform.name, armor.Name, out zone, used, out via)) continue;
                float ke = zone.Ke * scale, ce = zone.Ce * scale;
                Mark(armor.gameObject, ke, ce);
                sb.Append("\n[L2A7V]   measured '").Append(armor.transform.name)
                  .Append("' [").Append(via).Append("] raycast -> ").Append(ke.ToString("0")).Append('/')
                  .Append(ce.ToString("0")).Append(" (").Append(zone.Source).Append(')');
                measured++;
            }

            bool hadPieces = stored.Length + raycast.Length > 0;
            if (!hadPieces) return false;
            _Reported.Add(unitRoot.name);

            List<string> unmatched = new List<string>();
            foreach (string key in Table.Keys)
            {
                if (used.Contains(key)) continue;
                unmatched.Add(key);
                if (_UnmatchedReported.Add(key))
                    MelonLogger.Warning("[L2A7V] armour zone '" + key + "' matched no piece on " + unitRoot.name);
            }

            MelonLogger.Msg("[L2A7V] protection applied to " + (uniform + measured) +
                            " piece(s) (" + uniform + " stored, " + measured + " measured) out of " +
                            stored.Length + " uniform + " + raycast.Length + " raycast-measured, armour " +
                            "coefficient x" + scale.ToString("0.00") + sb);

            if (unmatched.Count > 0) DumpIdentities(stored, raycast);
            return true;
        }

        /// <summary>
        /// Units whose armour has been written and reported. The write is worth repeating only while a unit
        /// still has no armour at all, which is what the spawn looks like before the art pass fills it in.
        /// </summary>
        private static readonly HashSet<string> _Reported = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>
        /// When zones went unmatched this is the only way to find out what the runtime pieces are actually
        /// called: the log has to show both identities side by side, because the component's own label
        /// (VariableArmor.Name) is not the GameObject name the table is keyed by. The crew's body parts use
        /// the same components and are four times as numerous as the plates, so they are left out.
        /// </summary>
        private static void DumpIdentities(UniformArmor[] stored, VariableArmor[] raycast)
        {
            var sb = new StringBuilder();
            int shown = 0, hidden = 0;
            foreach (UniformArmor armor in stored)
            {
                if (IsCrewPiece(armor.ArmorType)) continue;
                if (shown >= Identities) { hidden++; continue; }
                shown++;
                sb.Append("\n[L2A7V]   id uniform name='").Append(armor.transform.name)
                  .Append("' label='").Append(armor.Name).Append('\'');
            }
            int shownMeasured = 0, hiddenMeasured = 0;
            foreach (VariableArmor armor in raycast)
            {
                if (IsCrewPiece(armor.ArmorType)) continue;
                if (shownMeasured >= Identities) { hiddenMeasured++; continue; }
                shownMeasured++;
                sb.Append("\n[L2A7V]   id measured name='").Append(armor.transform.name)
                  .Append("' label='").Append(armor.Name).Append('\'');
            }
            MelonLogger.Warning("[L2A7V] runtime armour identities (name and component label, " +
                                Identities + " per kind, " + hidden + "+" + hiddenMeasured +
                                " more not shown) while zones are unmatched" + sb);
        }

        /// <summary>Pieces listed per kind before the log stops being readable.</summary>
        private const int Identities = 40;

        private static bool IsCrewPiece(ArmorCodexScriptable codex)
        {
            if (codex == null) return false;
            string name = codex.name;
            return name != null && (name.Contains("human") || name.Contains("flesh") || name.Contains("bone"));
        }

        private static void Mark(GameObject piece, float ke, float ce)
        {
            var marker = piece.GetComponent<LpArmorZone>();
            if (marker == null) marker = piece.AddComponent<LpArmorZone>();
            marker.Ke = ke;
            marker.Ce = ce;
        }

        public static bool TryGetApplied(VariableArmor armor, out float ke, out float ce)
        {
            LpArmorZone zone = armor == null ? null : armor.GetComponent<LpArmorZone>();
            ke = zone == null ? 0f : zone.Ke;
            ce = zone == null ? 0f : zone.Ce;
            return zone != null;
        }

        /// <summary>
        /// The donor's runtime names against the zone names this table was written with.
        ///
        /// Only five of the forty-nine keys matched a piece on the LEO2A3 as spawned: the table was keyed from
        /// the imported 2A7V's own part names, while the armour components on the donor are called
        /// "turret cheek composite", "upper front plate", "hull upper left" and so on. Every one of those
        /// arrays therefore kept the donor's fallback of 10 mm RHA - which is why a 600 mm dart went through
        /// the turret face - so the names are bridged here instead of rewriting the table.
        /// </summary>
        private static readonly Dictionary<string, string> Aliases = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { "turret cheek composite", "cheek array left A" },
            { "turret left side composite", "cheek array right A" },
            { "turret right side composite", "cheek array right B" },
            { "heavy skirt", "skirt left" },
            { "upper front plate", "upper glacis array" },
            { "hull front plate", "lower front plate" },
            { "hull upper left", "hull left upper" },
            { "hull upper right", "hull right upper" },
            { "hull lower left", "hull left lower" },
            { "hull lower right", "hull right lower" },
            { "hull front roof", "hull roof front" },
        };

        /// <summary>Looks a piece up by the same rules the value pass uses, for callers that build their own.</summary>
        internal static bool TryResolve(string name, string label, HashSet<string> used, out Zone zone)
        {
            string via;
            return TryMatch(name, label, out zone, used, out via);
        }

        private static bool TryMatch(string name, string label, out Zone zone, HashSet<string> used,
            out string via)
        {
            zone = null;
            via = null;
            string key = null;

            if (!string.IsNullOrEmpty(name) && Table.TryGetValue(name, out zone))
            {
                key = name;
                via = "name";
            }
            else if (!string.IsNullOrEmpty(label) && Table.TryGetValue(label, out zone))
            {
                key = label;
                via = "label";
            }
            else
            {
                key = NormalizeLookup(name) ?? NormalizeLookup(label);
                if (key != null)
                {
                    zone = Table[key];
                    via = "folded";
                }
            }

            if (zone == null)
            {
                // The donor's own name, folded once, against the zone names this table uses.
                string folded = NormalizeLookup(name) ?? NormalizeLookup(label);
                string mapped;
                if (folded != null && Aliases.TryGetValue(folded, out mapped) && Table.TryGetValue(mapped, out zone))
                {
                    key = mapped;
                    via = "alias";
                }
            }

            if (key == null) return false;
            used.Add(key);
            return true;
        }

        /// <summary>
        /// Table keys folded to lowercase with runs of punctuation collapsed to one space, so a piece whose
        /// runtime name differs only by case, a curly apostrophe or a stray space still finds its zone.
        /// </summary>
        private static Dictionary<string, string> _folded;

        private static string NormalizeLookup(string text)
        {
            string folded = Fold(text);
            if (folded == null) return null;
            if (_folded == null)
            {
                var map = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (string key in Table.Keys)
                {
                    string k = Fold(key);
                    if (k != null && !map.ContainsKey(k)) map.Add(k, key);
                }
                _folded = map;
            }
            string tableKey;
            return _folded.TryGetValue(folded, out tableKey) ? tableKey : null;
        }

        private static string Fold(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var sb = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
                else if (sb.Length > 0 && sb[sb.Length - 1] != ' ') sb.Append(' ');
            }
            if (sb.Length > 0 && sb[sb.Length - 1] == ' ') sb.Length--;
            return sb.Length == 0 ? null : sb.ToString();
        }

        public static void NormalPostfix(VariableArmor __instance, ResistanceType type,
            bool forcePhysicalDimensionsOnly, ref float __result)
        {
            if (forcePhysicalDimensionsOnly) return;
            LpArmorZone zone = __instance.GetComponent<LpArmorZone>();
            if (zone == null) return;
            __result = type == ResistanceType.KE ? zone.Ke : zone.Ce;
        }

        public static void LosPostfix(VariableArmor __instance, ResistanceType type, Vector3 surfaceNormal,
            Vector3 penetrationVector, bool forcePhysicalDimensionsOnly, ref float __result)
        {
            if (forcePhysicalDimensionsOnly) return;
            LpArmorZone zone = __instance.GetComponent<LpArmorZone>();
            if (zone == null) return;
            float normal = type == ResistanceType.KE ? zone.Ke : zone.Ce;
            // Same obliquity rule the game applies to flat armour: the round travels through more of it.
            float angle = 90f - Vector3.Angle(-surfaceNormal, penetrationVector);
            __result = normal / Mathf.Sin(angle * Mathf.Deg2Rad);
        }

        public static void SabotPostfix(VariableArmor __instance, ref float __result)
        {
            LpArmorZone zone = __instance.GetComponent<LpArmorZone>();
            if (zone != null) __result = zone.Ke;
        }

        public static void HeatPostfix(VariableArmor __instance, ref float __result)
        {
            LpArmorZone zone = __instance.GetComponent<LpArmorZone>();
            if (zone != null) __result = zone.Ce;
        }

        public static void SimplePostfix(VariableArmor __instance, ref float __result)
        {
            LpArmorZone zone = __instance.GetComponent<LpArmorZone>();
            if (zone != null) __result = zone.Ke;
        }
    }
}
