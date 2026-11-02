using GHPC.Equipment;
using GHPC.Vehicle;
using MelonLoader;
using UnityEngine;

namespace Leopard2A7V
{
    /// <summary>
    /// Reports and repairs the vehicle's smoke launchers.
    ///
    /// The 2A7V's tubes are part of the turret mesh, so there is nothing separate to bind; what matters is
    /// whether the smoke manager the game uses still has its slots after the visual conversion. The donor
    /// carries them as transforms under its own turret art, which this mod hides rather than deletes, so the
    /// slots survive and the launchers keep working from the same place on the turret.
    ///
    /// This logs what was found: a missing manager or a slot count of zero is the reason a launcher key would
    /// do nothing, and it is worth knowing before hunting for a bug elsewhere.
    /// </summary>
    public static class LpSmoke
    {
        private const string Tag = "[L2A7V][smoke]";

        public static void Apply(GameObject unitRoot)
        {
            if (unitRoot == null) return;

            VehicleSmokeManager manager = unitRoot.GetComponentInChildren<VehicleSmokeManager>(true);
            if (manager == null)
            {
                MelonLogger.Warning(Tag + " no VehicleSmokeManager on " + unitRoot.name +
                                    "; the launchers cannot fire");
                return;
            }

            var slotsField = manager.GetType().GetField("_smokeSlots",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var list = slotsField == null ? null : slotsField.GetValue(manager) as System.Array;
            int count = list == null ? 0 : list.Length;

            int aimed = 0;
            if (list != null)
            {
                foreach (object slot in list)
                {
                    if (slot == null) continue;
                    var spawn = slot.GetType().GetField("SpawnLocation");
                    var rounds = slot.GetType().GetField("Rounds");
                    Transform at = spawn == null ? null : spawn.GetValue(slot) as Transform;
                    int roundsValue = rounds == null ? 0 : (int)rounds.GetValue(slot);
                    if (at == null) continue;
                    aimed++;
                    MelonLogger.Msg(Tag + " slot " + aimed + ": " + at.position.ToString("0.0") +
                                    " rounds " + roundsValue);
                }
            }

            MelonLogger.Msg(Tag + " smoke manager present, " + count + " slot(s), " + aimed +
                            " with a spawn point (tubes are part of the turret mesh)");
        }
    }
}
