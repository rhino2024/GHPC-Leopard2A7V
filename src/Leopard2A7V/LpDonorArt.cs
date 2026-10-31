using System.Collections.Generic;
using GHPC.Crew;
using MelonLoader;
using UnityEngine;

namespace Leopard2A7V
{
    /// <summary>
    /// The writes that have to wait for the donor's own art pass, and the crew figures that have to go away.
    ///
    /// GHPC fills a unit's MainGun, armour pieces and crew art in some time after the spawn, and what was on
    /// the prefab when it spawned is not what draws afterwards: measured on the donor Leopard, the unit holds
    /// 220 armour pieces at the spawn and none two seconds later while the visual swap runs, and the same
    /// swap finds no MainGun on it. A write done against the spawn lands on objects the game then throws away
    /// - which is how the armour tables and the gun's round list did nothing at all. This waits for the art
    /// to arrive, then does the three writes that need it once per unit.
    ///
    /// The crew figures come with that art, and the game draws them in the x-ray view beside the compartment
    /// imported from War Thunder, so a vehicle shows two sets of people. AarVisual.Hidden is the switch the
    /// game itself uses when a crew member bails out, and it disables the figure's renderers whatever view is
    /// live, so this uses it rather than removing anything: the body parts keep their colliders and stay
    /// damageable, only the donor's own art stops drawing.
    /// </summary>
    public static class LpDonorArt
    {
        /// <summary>
        /// The donor's crew figures, by the node names its prefab files them under (measured offline with
        /// ModSources/tools/lp_crew_probe.py): LEO2A3/LEO2A4 HULL COLLIDERS/Driver and
        /// LEO2A3/LEO2A4 TURRET COLLIDERS/{Gunner,Commander,Loader}.
        /// </summary>
        private static readonly System.Predicate<string> CrewFigureName = name =>
            name == "driver" || name == "gunner" || name == "commander" || name == "loader";

        private static readonly HashSet<string> _crewReported = new HashSet<string>();

        public static void Request(GameObject unitRoot)
        {
            if (unitRoot == null) return;
            if (unitRoot.GetComponent<LpDonorArtWatch>() != null) return;
            unitRoot.AddComponent<LpDonorArtWatch>();
        }

        /// <summary>
        /// Switches the donor's crew art off. Returns whether the figures were found at all, so the watcher
        /// knows when the art that carries them has arrived.
        /// </summary>
        public static bool HideDonorCrew(GameObject unitRoot)
        {
            if (unitRoot == null || !LpConfig.ShowInterior) return true;

            var fromTheGame = new HashSet<GHPC.AarVisual>();
            CrewManager crew = unitRoot.GetComponent<CrewManager>();
            if (crew != null && crew.CrewMembers != null)
            {
                foreach (CrewManager.CrewMember member in crew.CrewMembers)
                {
                    if (member == null || member.AllAarVisuals == null) continue;
                    foreach (GHPC.AarVisual visual in member.AllAarVisuals)
                        if (visual != null) fromTheGame.Add(visual);
                }
            }

            int found = 0, hidden = 0;
            var names = new List<string>();
            foreach (GHPC.AarVisual visual in unitRoot.GetComponentsInChildren<GHPC.AarVisual>(true))
            {
                if (LpVisual.IsImportedArt(visual.transform)) continue;
                string lower = visual.transform.name.ToLowerInvariant();
                bool byName = CrewFigureName(lower);
                if (!byName && !fromTheGame.Contains(visual)) continue;

                found++;
                names.Add(visual.transform.name);
                if (!visual.Hidden)
                {
                    visual.Hidden = true;
                    hidden++;
                }
            }

            if (found == 0) return false;
            if (_crewReported.Add(unitRoot.name))
                MelonLogger.Msg($"[L2A7V] hid {hidden} donor crew figure(s) (found at " +
                                string.Join(", ", names) + "), so the x-ray view draws only the compartment " +
                                "imported from War Thunder");
            return true;
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
}
