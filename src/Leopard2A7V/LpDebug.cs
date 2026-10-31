using System.Collections.Generic;
using System.Text;
using GHPC;
using MelonLoader;
using UnityEngine;

namespace Leopard2A7V
{
    /// <summary>
    /// F9 puts the imported look on the tanks already in the loaded scenario, so alignment and materials
    /// can be judged without building a mission around the new unit key first. Only donor Leopards are
    /// touched, which the presence of the leo2_rig drive bones decides.
    /// </summary>
    public class LpDebug : MonoBehaviour
    {
        private static LpDebug _instance;

        public static void Install()
        {
            if (_instance != null) return;
            var root = new GameObject("[Leopard2A7V-Debug]");
            _instance = root.AddComponent<LpDebug>();
            DontDestroyOnLoad(root);
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F9)) ApplyToLiveUnits();
        }

        private static void ApplyToLiveUnits()
        {
            if (!ObjModel.Loaded)
            {
                MelonLogger.Warning("[L2A7V] model not parsed yet" + (ObjModel.Failed ? ": " + ObjModel.Error : ""));
                return;
            }

            List<Unit> units = FindLiveUnits();
            if (units.Count == 0)
            {
                MelonLogger.Msg("[L2A7V] no live units in the scene");
                return;
            }

            var summary = new StringBuilder("[L2A7V] F9 on ");
            foreach (Unit unit in units) summary.Append(unit.name).Append(' ');
            MelonLogger.Msg(summary.ToString());

            int applied = 0;
            int skipped = 0;
            foreach (Unit unit in units)
            {
                GameObject root = unit.gameObject;
                if (root.GetComponent<LpConvertBehaviour>() != null) continue;
                if (FindDeep(root.transform, "leo2_rig") == null)
                {
                    skipped++;
                    continue;
                }
                if (FindDeep(root.transform, LpConfig.VisualRootName) != null) continue;

                LpArmorLog.Apply(root);
                root.AddComponent<LpConvertBehaviour>();
                applied++;
            }
            MelonLogger.Msg($"[L2A7V] queued the Leopard 2A7V look on {applied} unit(s), " +
                            $"{skipped} were not Leopards");
        }

        private static List<Unit> FindLiveUnits()
        {
            var found = new List<Unit>();
            foreach (Unit unit in Resources.FindObjectsOfTypeAll<Unit>())
            {
                if (unit == null || !unit.isActiveAndEnabled) continue;
                if (!unit.gameObject.scene.isLoaded) continue;
                found.Add(unit);
            }
            return found;
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform child in root)
            {
                Transform match = FindDeep(child, name);
                if (match != null) return match;
            }
            return null;
        }
    }
}
