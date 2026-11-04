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
    /// Keeps the donor's own LEO2A3 art out of the picture, for as long as its unit lives, and ticks the three
    /// deferred writes until each of them reports that its target exists.
    ///
    /// This is a standing job, not a one-shot pass, for three measured reasons. The donor's art pass arrives a
    /// moment after the conversion and brings a second set of renderers with it (the camouflage net is one);
    /// GHPC re-enables renderers from its own passes when the AAR root is torn down and when LODs switch; and
    /// a converted unit can be cloned again from the template, so each one needs its own list rather than the
    /// last one converting. Every piece of state below is therefore per-unit, and the hiding is re-stated in
    /// LateUpdate, after the game's own updates have run.
    /// </summary>
    public class LpDonorArtWatch : MonoBehaviour
    {
        private const float IntervalSeconds = 0.5f;
        private const float TimeoutSeconds = 120f;
        private const float RescanSeconds = 2f;

        private GameObject _unit;
        private readonly HashSet<Renderer> _hidden = new HashSet<Renderer>();
        private float _elapsed;
        private float _next;
        private float _nextRescan;
        private bool _gunDone;
        private bool _armourDone;
        private bool _gearDone;
        private bool _crewDone;
        private bool _writesDone;
        private bool _guardLogged;
        private int _reports;

        private void Awake()
        {
            _unit = gameObject;
        }

        /// <summary>
        /// Called once the art pass has finished converting this unit: seeds the list with the renderers the
        /// pre-conversion snapshot found, hides them, and from then on the per-frame guard owns them.
        ///
        /// The snapshot is only a seed, never the list itself. Treating it as the list - or worse, replacing it
        /// with the renderers that were *kept* - is what made an earlier build hide this mod's own hull and
        /// then let the donor's back on screen when the guard retired.
        /// </summary>
        internal void Adopt()
        {
            _unit = gameObject;
            _hidden.Clear();
            Renderer[] seed = LpBaseRig.DonorSnapshot;
            if (seed != null)
                foreach (Renderer renderer in seed)
                    if (renderer != null && !LpVisual.IsImportedArt(renderer.transform)) _hidden.Add(renderer);

            Sweep();
            Rehide();
            Report();
            MelonLogger.Msg("[L2A7V] guard attached to " + gameObject.name + " after conversion: " +
                            _hidden.Count + " donor renderer(s) kept out of the picture");
        }

        /// <summary>
        /// Adds every renderer under this unit that the hide rule covers and our list does not have yet.
        /// Goes through <see cref="LpBaseRig.ShouldHide"/> and not a rule of its own, because the donor's
        /// wheels, its two skinned track loops, its crew compartment and the sight housings have to keep
        /// drawing - they are what this vehicle drives on and sees through.
        /// </summary>
        private int Sweep()
        {
            if (_unit == null) _unit = gameObject;
            int added = 0;
            foreach (Renderer renderer in _unit.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null) continue;
                if (renderer is ParticleSystemRenderer) continue;
                if (LpVisual.IsImportedArt(renderer.transform)) continue;
                if (renderer.gameObject.layer == LpBaseRig.ImprintLayer) continue;
                if (!LpBaseRig.ShouldHide(renderer.transform, renderer.name)) continue;
                if (!_hidden.Add(renderer)) continue;
                added++;
                // The switch and the scale are both things the game's own passes re-state, so the geometry
                // goes as well: a renderer with no indices cannot be made to draw by toggling a bool.
                try { LpBaseRig.StripGeometry(renderer); }
                catch (System.Exception error)
                {
                    MelonLogger.Warning("[L2A7V] could not strip '" + LpBaseRig.PathOf(renderer.transform) +
                                        "': " + error.Message);
                }
            }
            if (added > 0)
                MelonLogger.Msg("[L2A7V] donor art: " + added + " renderer(s) added to the hide list, " +
                                _hidden.Count + " on it now");
            return added;
        }

        /// <summary>Re-states the flag that takes a renderer off screen.</summary>
        private void Rehide()
        {
            int switched = 0;
            foreach (Renderer renderer in _hidden)
            {
                if (renderer == null) continue;
                if (renderer.enabled)
                {
                    renderer.enabled = false;
                    switched++;
                }
            }
            if (switched > 0 && !_guardLogged)
            {
                _guardLogged = true;
                MelonLogger.Msg("[L2A7V] guard switched " + switched + " donor renderer(s) back off");
            }
        }

        /// <summary>
        /// Names any renderer that is still drawing and is not this mod's art. If this list is not empty the
        /// old hull is still visible, and the names say which part of it.
        /// </summary>
        private void Report()
        {
            if (_unit == null) return;
            int shown = 0, total = 0;
            foreach (Renderer renderer in _unit.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer == null || !renderer.enabled) continue;
                if (renderer is ParticleSystemRenderer) continue;
                if (LpVisual.IsImportedArt(renderer.transform)) continue;
                if (renderer.gameObject.layer == LpBaseRig.ImprintLayer) continue;
                if (!LpBaseRig.ShouldHide(renderer.transform, renderer.name)) continue;
                total++;
                if (shown++ >= 12) continue;
                MelonLogger.Warning("[L2A7V] still drawing: " + LpBaseRig.PathOf(renderer.transform));
            }
            if (total > 0 && _reports < 5)
            {
                _reports++;
                MelonLogger.Warning("[L2A7V] donor renderers still drawing: " + total);
            }
            else if (total == 0 && _reports == 0)
            {
                _reports = 1;
                MelonLogger.Msg("[L2A7V] donor renderers still drawing: 0");
            }

            // The armour is the other half of this job: a piece whose transform was collapsed somewhere up the
            // chain answers nothing, and the vehicle reads as unarmoured on that face with no other symptom.
            int collapsed = 0;
            foreach (GHPC.UniformArmor armor in _unit.GetComponentsInChildren<GHPC.UniformArmor>(true))
            {
                if (armor == null) continue;
                if (!armor.gameObject.name.StartsWith("L2A7V_XrayArmor", System.StringComparison.Ordinal)) continue;
                Vector3 scale = armor.transform.lossyScale;
                if (Mathf.Abs(scale.x) < 0.05f || Mathf.Abs(scale.y) < 0.05f || Mathf.Abs(scale.z) < 0.05f)
                    collapsed++;
            }
            if (collapsed > 0 && _collapsedReported < 3)
            {
                _collapsedReported++;
                MelonLogger.Warning("[L2A7V] " + collapsed + " armour piece(s) sit under a collapsed transform " +
                                    "and cannot be hit; check what was scaled to zero above them");
            }

            // One hard data point on where the armour actually is, in world space, so the next log says
            // whether a plate covers the hull it is drawn on instead of leaving us to guess from a screenshot.
            if (!_sampled)
            {
                foreach (GHPC.UniformArmor armor in _unit.GetComponentsInChildren<GHPC.UniformArmor>(true))
                {
                    if (armor == null) continue;
                    if (!armor.gameObject.name.StartsWith("L2A7V_XrayArmor", System.StringComparison.Ordinal)) continue;
                    Collider collider = armor.GetComponent<Collider>();
                    _sampled = true;
                    MelonLogger.Msg("[L2A7V] armour sample: '" + armor.Name + "' world " +
                                    (collider != null ? collider.bounds.center.ToString("0.0") + " size " +
                                                        collider.bounds.size.ToString("0.00") + " enabled " +
                                                        collider.enabled : "no collider"));
                    break;
                }
            }
        }

        private bool _sampled;
        private int _collapsedReported;

        /// <summary>Tells the guard of a freshly converted unit to take over. Called by the art pass itself.</summary>
        internal static void SetConverted(GameObject unit)
        {
            if (unit == null) return;
            LpDonorArtWatch watch = unit.GetComponent<LpDonorArtWatch>();
            if (watch == null) watch = unit.AddComponent<LpDonorArtWatch>();
            watch.Adopt();
        }

        private void Update()
        {
            if (_writesDone) return;
            _elapsed += Time.deltaTime;
            if (Time.unscaledTime < _next) return;
            _next = Time.unscaledTime + IntervalSeconds;

            if (!_gunDone) _gunDone = LpWeapons.ApplyGunList(gameObject);
            LpWeapons.ApplyThirdRound(gameObject);   // self-guarded and idempotent
            if (!_crewDone) _crewDone = LpDonorArt.HideDonorCrew(gameObject);
            // A unit spawned from the template has no wheel bones until the game's art pass gives it a rig, so
            // the gear built at conversion is a static stand-in. This replaces it the moment the bones exist.
            if (!_gearDone) _gearDone = LpRunningGear.Ensure(transform);

            if (!(_gunDone && _crewDone && _gearDone))
            {
                if (_elapsed < TimeoutSeconds) return;
                MelonLogger.Warning("[L2A7V] stopped waiting for the donor's art pass after " + TimeoutSeconds +
                                    " s (gun list " + _gunDone + ", crew figures " + _crewDone +
                                    ", running gear " + _gearDone + ")");
            }
            _writesDone = true;
        }

        /// <summary>
        /// The guard itself, after the game has had its say: AarController re-shows what it thinks should be
        /// drawn, the LOD pass re-enables renderers, and the donor's late art arrives at any time.
        /// </summary>
        private void LateUpdate()
        {
            // The after-action picture has one owner: LpDonorInteriorHider, which takes the whole donor set
            // out while the report is up. Two systems writing the same renderer flags every frame would only
            // take turns undoing each other.
            if (GHPC.AarController.InAar) return;

            Rehide();
            if (Time.unscaledTime < _nextRescan) return;
            _nextRescan = Time.unscaledTime + RescanSeconds;
            if (Sweep() > 0) Rehide();
            Report();
        }
    }
}
