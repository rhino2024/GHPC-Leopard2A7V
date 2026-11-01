using System.Collections.Generic;
using MelonLoader;
using UnityEngine;

namespace Leopard2A7V
{
    /// <summary>
    /// The writes that have to wait for the donor's own art pass, and the crew figures it carries.
    ///
    /// GHPC fills a unit's MainGun, armour pieces and crew art in some time after the spawn, and what was on
    /// the prefab when it spawned is not what draws afterwards: measured on the donor Leopard, the unit holds
    /// 220 armour pieces at the spawn and none two seconds later while the visual swap runs, and the same
    /// swap finds no MainGun on it. A write done against the spawn lands on objects the game then throws away
    /// - which is how the armour tables and the gun's round list did nothing at all. This waits for the art
    /// to arrive, then does the three writes that need it once per unit.
    ///
    /// The crew art comes with that pass, and the figures are the game's own soldiers - a skinned Soldier_body
    /// and Soldier_head wearing the donor's uniform material. The compartment imported from War Thunder
    /// already carries its own crew mannequins (the int_crew submesh of the x-ray mesh), stationed where the
    /// four men sit, so the vehicle would draw two sets of people in the x-ray view. This keeps the game's
    /// set out and leaves the War Thunder figures as the ones the view draws.
    ///
    /// A one-shot write is not enough: AarController.HandleVehiclePose assigns AarVisual.Hidden from the
    /// shot's crew-present state every time an AAR shot is shown, so the game re-shows its soldiers on the
    /// first shot that had the crew alive. LpCrewSwap re-states the flag every frame instead.
    ///
    /// Only the figure is taken out. The body parts under the rig keep their colliders and their AarModels
    /// (the anatomy the x-ray uses to show wounds), and the crew members stay damageable and bailable.
    /// </summary>
    public static class LpDonorArt
    {
        /// <summary>
        /// GHPC's own soldier art, by renderer name (measured offline with ModSources/tools/lp_crew_subtree.py
        /// on LEO2A3: every crew figure is a "Geo" node holding a Soldier_body and a Soldier_head
        /// SkinnedMeshRenderer). Going by the art rather than by node names or AarVisual membership keeps
        /// this independent of how a prefab files its stations and of when the art pass attaches them.
        /// </summary>
        private static readonly System.Predicate<string> CrewArtName = name =>
            name.StartsWith("soldier_", System.StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// The fallback: the donor's crew figure nodes, by the names its prefab files them under
        /// (ModSources/tools/lp_crew_probe.py): LEO2A3/LEO2A4 HULL COLLIDERS/Driver and
        /// LEO2A3/LEO2A4 TURRET COLLIDERS/{Gunner,Commander,Loader}.
        /// </summary>
        private static readonly System.Predicate<string> CrewFigureName = name =>
            name == "driver" || name == "gunner" || name == "commander" || name == "loader";

        private static readonly HashSet<string> _crewReported = new HashSet<string>();

        private static bool _noMannequinsReported;
        private static bool _noInteriorReported;

        public static void Request(GameObject unitRoot)
        {
            if (unitRoot == null) return;
            if (unitRoot.GetComponent<LpDonorArtWatch>() != null) return;
            unitRoot.AddComponent<LpDonorArtWatch>();
            MelonLogger.Msg("[L2A7V] watching the donor art pass on " + unitRoot.name +
                            " for the gun list, the armour table and the crew figures");
        }

        /// <summary>
        /// Takes the game's crew figures out of the picture, provided the compartment imported from War
        /// Thunder has people of its own to show. Returns whether the figures were found at all, so the
        /// watcher knows when the art that carries them has arrived.
        /// </summary>
        public static bool HideDonorCrew(GameObject unitRoot)
        {
            if (unitRoot == null || !LpConfig.ShowInterior) return true;

            if (LpInterior.Failed)
            {
                if (!_noInteriorReported)
                {
                    _noInteriorReported = true;
                    MelonLogger.Warning("[L2A7V] the War Thunder compartment failed to load, so the game's " +
                                        "own crew figures stay: there is nothing to stand in for them");
                }
                return true;
            }
            if (!LpInterior.Loaded) return false;
            if (LpInterior.CrewTriangles <= 0)
            {
                if (!_noMannequinsReported)
                {
                    _noMannequinsReported = true;
                    MelonLogger.Warning("[L2A7V] the imported compartment carries no crew mannequins " +
                                        "(int_crew), so the game's own crew figures stay");
                }
                return true;
            }

            var renderers = new List<Renderer>();
            var visuals = new List<GHPC.AarVisual>();
            var figures = new List<string>();
            var seen = new HashSet<GHPC.AarVisual>();

            foreach (Renderer renderer in unitRoot.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || LpVisual.IsImportedArt(renderer.transform)) continue;
                if (!CrewArtName(renderer.name)) continue;

                renderers.Add(renderer);
                GHPC.AarVisual visual = VisualAbove(renderer.transform);
                if (visual != null && !LpVisual.IsImportedArt(visual.transform) && seen.Add(visual))
                    visuals.Add(visual);

                string figure = FigureName(renderer.transform);
                if (!figures.Contains(figure)) figures.Add(figure);
            }

            if (renderers.Count == 0)
            {
                foreach (GHPC.AarVisual visual in unitRoot.GetComponentsInChildren<GHPC.AarVisual>(true))
                {
                    if (visual == null || LpVisual.IsImportedArt(visual.transform)) continue;
                    string lower = visual.transform.name.ToLowerInvariant();
                    if (!CrewFigureName(lower)) continue;

                    if (seen.Add(visual)) visuals.Add(visual);
                    if (!figures.Contains(visual.transform.name)) figures.Add(visual.transform.name);
                }
            }

            if (visuals.Count == 0 && renderers.Count == 0) return false;

            foreach (GHPC.AarVisual visual in visuals) visual.Hidden = true;
            foreach (Renderer renderer in renderers) renderer.enabled = false;

            LpCrewSwap swap = unitRoot.GetComponent<LpCrewSwap>();
            if (swap == null) swap = unitRoot.AddComponent<LpCrewSwap>();
            swap.Bind(visuals, renderers);

            if (_crewReported.Add(unitRoot.name))
                MelonLogger.Msg($"[L2A7V] x-ray crew replaced: the game's {figures.Count} figure(s) " +
                                $"({string.Join(", ", figures)}, {renderers.Count} soldier renderer(s)) are " +
                                $"kept out, and the War Thunder mannequins imported with the compartment " +
                                $"(int_crew, {LpInterior.CrewTriangles} triangle(s)) are what the x-ray " +
                                $"draws; the hitzones and the anatomy models stay");
            return true;
        }

        private static GHPC.AarVisual VisualAbove(Transform node)
        {
            for (Transform step = node; step != null; step = step.parent)
            {
                GHPC.AarVisual visual = step.GetComponent<GHPC.AarVisual>();
                if (visual != null) return visual;
            }
            return null;
        }

        private static string FigureName(Transform node)
        {
            for (Transform step = node; step != null; step = step.parent)
                if (CrewFigureName(step.name.ToLowerInvariant())) return step.name;
            return node.name;
        }
    }

    /// <summary>
    /// Keeps the game's crew figures out of the views for the rest of the session.
    ///
    /// AarController.HandleVehiclePose sets AarVisual.Hidden from the AAR shot's crew-present state, so the
    /// game puts its soldiers back the moment a shot with a live crew is shown. Re-stating the flag in
    /// LateUpdate lands after AarController's own update and costs one flag pack per figure per frame.
    /// </summary>
    public class LpCrewSwap : MonoBehaviour
    {
        private GHPC.AarVisual[] _visuals;
        private Renderer[] _renderers;
        private bool _reported;

        internal void Bind(List<GHPC.AarVisual> visuals, List<Renderer> renderers)
        {
            _visuals = visuals.ToArray();
            _renderers = renderers.ToArray();
        }

        private void LateUpdate()
        {
            int putBack = 0;

            if (_visuals != null)
            {
                for (int i = 0; i < _visuals.Length; i++)
                {
                    if (_visuals[i] != null && !_visuals[i].Hidden)
                    {
                        _visuals[i].Hidden = true;
                        putBack++;
                    }
                }
            }
            if (_renderers != null)
            {
                for (int i = 0; i < _renderers.Length; i++)
                {
                    if (_renderers[i] != null && _renderers[i].enabled)
                    {
                        _renderers[i].enabled = false;
                        putBack++;
                    }
                }
            }

            if (putBack == 0 || _reported) return;
            _reported = true;
            MelonLogger.Msg($"[L2A7V] x-ray crew: the game's own figures came back {putBack} time(s) in " +
                            "one frame (AarController.HandleVehiclePose re-shows crew that is present) and " +
                            "were kept out again; the War Thunder mannequins stay the only people drawn");
        }
    }

    /// <summary>
    /// Ticks the three deferred writes until each of them reports that its target exists.
    /// </summary>
    public class LpDonorArtWatch : MonoBehaviour
    {
        private const float IntervalSeconds = 0.5f;
        private const float TimeoutSeconds = 120f;

        private float _elapsed;
        private float _next;
        private bool _gunDone;
        private bool _armourDone;
        private bool _crewDone;

        private void Update()
        {
            _elapsed += Time.deltaTime;
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + IntervalSeconds;

            if (!_gunDone) _gunDone = LpWeapons.ApplyGunList(gameObject);
            LpWeapons.ApplyThirdRound(gameObject);   // self-guarded and idempotent
            if (!_armourDone) _armourDone = LpArmor.Apply(gameObject);
            if (!_crewDone) _crewDone = LpDonorArt.HideDonorCrew(gameObject);

            if (_gunDone && _armourDone && _crewDone)
            {
                Destroy(this);
                return;
            }
            if (_elapsed < TimeoutSeconds) return;

            MelonLogger.Warning("[L2A7V] stopped waiting for the donor's art pass after " + TimeoutSeconds +
                                " s (gun list " + _gunDone + ", armour " + _armourDone +
                                ", crew figures " + _crewDone + ")");
            Destroy(this);
        }
    }


    /// <summary>
    /// Keeps calling the armour pass on every converted unit until it reports success.
    ///
    /// The per-unit watcher never fired in practice: the log shows the donor report (220 uniform + 33
    /// raycast pieces) but no "protection applied" line at all, so the table simply never landed and every
    /// composite array kept the donor's 10 mm fallback. This driver is attached to a host of its own, so it
    /// ticks no matter what happens to the unit's own hierarchy, and it retries once a second until each
    /// converted unit is done.
    /// </summary>
    public class LpArmorDriver : MonoBehaviour
    {
        private static LpArmorDriver _instance;

        private float _next;

        internal static void Ensure()
        {
            if (_instance != null) return;
            var host = new GameObject("L2A7V_ArmourDriver");
            UnityEngine.Object.DontDestroyOnLoad(host);
            _instance = host.AddComponent<LpArmorDriver>();
        }

        private void Update()
        {
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + 1f;

            foreach (LpConvertBehaviour marker in UnityEngine.Object.FindObjectsOfType<LpConvertBehaviour>())
            {
                if (marker == null) continue;
                LpArmor.Apply(marker.gameObject);
            }
        }
    }
}
