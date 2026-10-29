using System.Collections.Generic;
using System.Reflection;
using System.Text;
using GHPC;
using HarmonyLib;
using MelonLoader;
using UnityEngine;

namespace Leopard2A7V
{
    /// <summary>
    /// Reports the protection layout the donor tank is spawned with, and changes nothing.
    ///
    /// That is deliberate. The LEO2A3's armour is made of VariableArmor pieces whose thickness is measured
    /// by raycasting through the plate's own mesh (VariableArmor.getThicknessAlongVector), with AverageRha
    /// only used when the backface ray finds nothing, and its composite sections are modelled as named
    /// arrays ("upper glacis array", "turret cheek composite") with a codex material that converts
    /// physical millimetres into RHA per resistance type. Writing flat numbers over that would replace a
    /// per-centimetre model with one figure per part, so the imported look ships with the donor's
    /// ballistics intact and this file only exists to show what is actually in use.
    /// </summary>
    public static class LpArmorLog
    {
        private static readonly HashSet<int> Logged = new HashSet<int>();
        private static readonly FieldInfo VariableAverageRha = AccessTools.Field(typeof(VariableArmor), "AverageRha");

        /// <summary>Pieces listed per kind before the log stops being readable.</summary>
        private const int PerKind = 24;

        public static void Apply(GameObject unitRoot)
        {
            if (!LpConfig.LogArmor.Value || unitRoot == null) return;
            if (!Logged.Add(unitRoot.GetInstanceID())) return;

            UniformArmor[] uniform = unitRoot.GetComponentsInChildren<UniformArmor>(true);
            VariableArmor[] variable = unitRoot.GetComponentsInChildren<VariableArmor>(true);

            var sb = new StringBuilder();
            sb.Append("[L2A7V] donor protection on ").Append(unitRoot.name).Append(": ")
              .Append(uniform.Length).Append(" uniform + ").Append(variable.Length)
              .Append(" raycast-measured pieces");

            int listed = AppendUniform(sb, uniform);
            if (listed < uniform.Length)
                sb.Append("\n[L2A7V]   (").Append(uniform.Length - listed).Append(" uniform pieces not shown)");

            listed = AppendMeasured(sb, variable);
            if (listed < variable.Length)
                sb.Append("\n[L2A7V]   (").Append(variable.Length - listed).Append(" measured pieces not shown)");

            MelonLogger.Msg(sb.ToString());
        }

        private static int AppendUniform(StringBuilder sb, UniformArmor[] pieces)
        {
            int shown = 0;
            foreach (UniformArmor armor in pieces)
            {
                if (shown >= PerKind) break;
                shown++;
                if (IsCrew(armor.Name)) continue;
                sb.Append("\n[L2A7V]   uniform '").Append(Shorten(armor.Name))
                  .Append("' KE=").Append(armor.SabotRha.ToString("0"))
                  .Append(" CE=").Append(armor.HeatRha.ToString("0"))
                  .Append(" era=").Append(armor.IsEra)
                  .Append(" type=").Append(CodexName(armor.ArmorType));
            }
            return shown;
        }

        private static int AppendMeasured(StringBuilder sb, VariableArmor[] pieces)
        {
            int shown = 0;
            foreach (VariableArmor armor in pieces)
            {
                if (shown >= PerKind) break;
                shown++;
                if (IsCrew(armor.Name)) continue;
                float fallback = VariableAverageRha == null ? float.NaN : (float)VariableAverageRha.GetValue(armor);
                sb.Append("\n[L2A7V]   measured '").Append(Shorten(armor.Name))
                  .Append("' fallback RHA=").Append(fallback.ToString("0"))
                  .Append(" type=").Append(CodexName(armor.ArmorType));
            }
            return shown;
        }

        /// <summary>
        /// Crew hitzones use the same components and their values are body-part thicknesses, so printing
        /// them buries the plates that matter.
        /// </summary>
        private static bool IsCrew(string label)
        {
            if (string.IsNullOrEmpty(label)) return false;
            string lowered = label.ToLowerInvariant();
            return lowered.Contains("hitzone") || lowered.Contains("swat:");
        }

        private static string Shorten(string label)
        {
            if (string.IsNullOrEmpty(label)) return "-";
            return label.Length <= 40 ? label : label.Substring(0, 40);
        }

        private static string CodexName(ArmorCodexScriptable codex)
        {
            return codex == null ? "-" : codex.name;
        }
    }
}
