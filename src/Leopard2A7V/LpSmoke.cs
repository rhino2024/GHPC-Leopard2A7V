using System.Collections.Generic;
using GHPC.Equipment;
using GHPC.Vehicle;
using MelonLoader;
using UnityEngine;

namespace Leopard2A7V
{
    /// <summary>
    /// Moves the donor's smoke launcher onto this vehicle's tubes and hands it back to the game.
    ///
    /// The launcher itself is the donor's - its slots, salvo patterns, impulse maths, sound and pool - and that
    /// is the point: nothing here reimplements smoke. Two things have to be repaired for it to work on a
    /// converted vehicle:
    ///
    /// * <see cref="VehicleSmokeManager.HasSmokeLauncher"/> is a gate over three fields, and one of them is a
    ///   donor GameObject that must be active. This mod hides donor art, so that gate can read false and the
    ///   fire key then does nothing at all - every slot still reports its position and rounds, which is why it
    ///   looks like the launcher "stopped working" rather than vanished. The gate object is switched back on
    ///   and its renderers are left off, so repairing the gate cannot put donor tubes back on the model.
    ///
    /// * The slots' SpawnLocation transforms sit on the LEO2A3's turret, at the LEO2A3's tube positions. The
    ///   2A7V turret is wider and carries its tubes further out, so a grenade launched from the donor's point
    ///   starts inside the new turret skin. Each slot is therefore re-seated on this model's turret surface,
    ///   along the same direction from the turret's own centre, so the banks end up where the tubes are drawn.
    ///
    /// The manager is also pushed into the player's input cache, because that field is filled when the player
    /// unit is set and a converted unit can be handed over before the manager is reachable.
    /// </summary>
    public static class LpSmoke
    {
        private const string Tag = "[L2A7V][smoke]";

        private const System.Reflection.BindingFlags Private =
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;

        /// <summary>How far outside the turret skin a grenade starts, so it does not spawn inside it.</summary>
        private const float MuzzleOffset = 0.10f;

        public static void Apply(GameObject unitRoot)
        {
            if (unitRoot == null) return;

            // Nothing in this file may throw. It runs inside the spawn postfix, and an exception there aborts
            // the game's own spawn bookkeeping - the run that logged "No units in platoon: 1st Plt" and never
            // handed the player a unit was `manager.IsCurrentUnit` throwing from the log line below, with this
            // method's frame right under it in the stack.
            try
            {
                ApplyUnsafe(unitRoot);
            }
            catch (System.Exception error)
            {
                MelonLogger.Warning(Tag + " smoke setup failed, the donor's launcher is left as it is: " + error);
            }
        }

        private static void ApplyUnsafe(GameObject unitRoot)
        {
            VehicleSmokeManager manager = unitRoot.GetComponentInChildren<VehicleSmokeManager>(true);
            if (manager == null)
            {
                MelonLogger.Warning(Tag + " no VehicleSmokeManager on " + unitRoot.name +
                                    "; the launchers cannot fire");
                return;
            }

            // ---- the gate -------------------------------------------------------------------------------
            var hasLauncherField = manager.GetType().GetField("_hasSmokeLauncher", Private);
            var gateField = manager.GetType().GetField("_requiredActiveLauncherObject", Private);
            var prefabField = manager.GetType().GetField("_smokePrefab", Private);

            if (hasLauncherField != null && !(bool)hasLauncherField.GetValue(manager))
            {
                hasLauncherField.SetValue(manager, true);
                MelonLogger.Warning(Tag + " the donor reports no smoke launcher; switched the flag on so the " +
                                    "donor's own launcher answers the key");
            }

            GameObject gate = gateField == null ? null : gateField.GetValue(manager) as GameObject;
            if (gate != null && !gate.activeInHierarchy)
            {
                gate.SetActive(true);
                foreach (Renderer renderer in gate.GetComponentsInChildren<Renderer>(true))
                    if (renderer != null) renderer.enabled = false;
                MelonLogger.Warning(Tag + " the launcher gate '" + gate.name + "' was inactive (that alone makes " +
                                    "the whole launcher report itself missing); switched back on, donor art left off");
            }

            // ---- the slots ------------------------------------------------------------------------------
            var slotsField = manager.GetType().GetField("_smokeSlots", Private);
            var slots = slotsField == null ? null : slotsField.GetValue(manager) as System.Array;
            int moved = Reposition(manager, unitRoot, slots);

            // ---- the player's cache ---------------------------------------------------------------------
            // The field is filled when a unit is handed to the player, which happens AFTER this conversion.
            // LpSmokeWiring takes over from here and fires the donor's launcher on the game's own smoke button.
            if (unitRoot.GetComponent<LpSmokeWiring>() == null)
                unitRoot.AddComponent<LpSmokeWiring>();

            GameObject prefab = prefabField == null ? null : prefabField.GetValue(manager) as GameObject;
            MelonLogger.Msg(Tag + " launcher: slots=" + (slots == null ? 0 : slots.Length) + " moved=" + moved +
                            " gate=" + (gate == null ? "-" : gate.name) +
                            " prefab=" + (prefab == null ? "-" : prefab.name) +
                            " HasSmokeLauncher=" + manager.HasSmokeLauncher);
        }



        /// The tube grid, in the export's own frame (x starboard, y up, z forward), measured off the
        /// ex_decor_l/r_04 clusters: mouths on the outboard face, four columns by two rows.
        private const float LeftFace = -1.56f;
        private const float RightFace = 1.58f;
        private const float LowerRow = 1.90f;
        private const float UpperRow = 2.30f;
        private static readonly float[] Columns = { -0.02f, -0.21f, -0.40f, -0.59f };

        private static int Reposition(VehicleSmokeManager manager, GameObject unitRoot, System.Array slots)
        {
            if (slots == null || slots.Length == 0) return 0;

            Transform turret = FindDeep(unitRoot.transform, "L2A7V_TurretPivot");
            if (turret == null) return 0;

            int moved = 0;
            for (int i = 0; i < slots.Length && i < 16; i++)
            {
                object entry = slots.GetValue(i);
                if (entry == null) continue;
                var spawnField = entry.GetType().GetField("SpawnLocation");
                Transform spawn = spawnField == null ? null : spawnField.GetValue(entry) as Transform;
                if (spawn == null) continue;

                Vector3 model = TubePosition(i);
                // The art is built with the turret ring subtracted from its vertices and hung under the pivot,
                // so a mesh-space point becomes a world point through the same subtraction.
                Vector3 world = turret.TransformPoint(model - LpConfig.TurretAxis);
                spawn.SetParent(turret, true);
                spawn.position = world;
                moved++;
            }
            return moved;
        }

        /// <summary>
        /// Tube <paramref name="i"/> in model space. The donor numbers its slots left lower, left upper, right
        /// lower, right upper, four to a row, and this keeps that order so each salvo still fires the tubes the
        /// donor meant while its launch angles fan the way the modelled mouths point.
        /// </summary>
        private static Vector3 TubePosition(int i)
        {
            int row = (i % 16) / 4;
            int column = i % 4;

            float forward = Columns[column];
            bool upper = row == 1 || row == 3;
            float up = upper ? UpperRow : LowerRow;
            float starboard = row < 2 ? LeftFace : RightFace;
            return new Vector3(starboard, up, forward);
        }

        /// <summary>
        /// Puts this manager where the player's input looks for it. The field is filled from the unit that is
        /// handed to the player, and the conversion runs before that, so this returns false until the vehicle
        /// becomes the player's - which is why <see cref="LpSmokeWiring"/> keeps asking.
        /// </summary>
        internal static bool WirePlayerInput(VehicleSmokeManager manager, GameObject unitRoot)
        {
            GHPC.Player.PlayerInput input = GHPC.Player.PlayerInput.Instance;
            if (input == null || manager == null) return false;

            var field = input.GetType().GetField("_smokeLauncherManager", Private);
            if (field == null) return false;

            object current = field.GetValue(input);
            if (ReferenceEquals(current, manager)) return true;

            GHPC.Unit player = CurrentPlayerUnit(input);
            if (player == null) return false;
            if (player.gameObject != unitRoot) return false;
            if (player.GetComponentInChildren<VehicleSmokeManager>(true) != manager) return false;

            field.SetValue(input, manager);
            MelonLogger.Msg(Tag + " the player's smoke key now points at this vehicle's launcher");
            return true;
        }

        /// <summary>
        /// The unit the player is driving.
        ///
        /// The shipped build declares this as a public field (<c>public Unit CurrentPlayerUnit;</c>). This
        /// helper used to look for a property first and then for a non-public field only, so it matched
        /// neither shape and answered "no player" on every frame of every session. Every caller reads that
        /// answer as "this vehicle is not mine yet", so the commander's T key and the smoke key went silent
        /// instead of reporting anything - which is exactly what "T does nothing at the commander" looked
        /// like. Both shapes are asked for here, with every visibility, so a build that moves the member
        /// cannot bring that silence back.
        /// </summary>
        internal static GHPC.Unit CurrentPlayerUnit(GHPC.Player.PlayerInput input)
        {
            if (input == null) return null;
            const System.Reflection.BindingFlags Any =
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic;

            System.Type type = input.GetType();
            try
            {
                var property = type.GetProperty("CurrentPlayerUnit", Any);
                if (property != null && property.CanRead && property.GetIndexParameters().Length == 0)
                    return property.GetValue(input, null) as GHPC.Unit;
            }
            catch (System.Exception)
            {
                // An ambiguous or throwing property must not hide the field that is actually there.
            }

            try
            {
                var field = type.GetField("CurrentPlayerUnit", Any);
                return field == null ? null : field.GetValue(input) as GHPC.Unit;
            }
            catch (System.Exception)
            {
                return null;
            }
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root == null) return null;
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindDeep(root.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }
    }

    /// <summary>
    /// Fires the donor's launcher for this vehicle, on the game's own smoke binding.
    ///
    /// The game's path is <c>PlayerInput</c> caching a <c>VehicleSmokeManager</c> and calling
    /// <c>FireSmokes()</c> when "Smoke Grenades" is pressed. On a converted vehicle that path has now been
    /// observed to do nothing at all while the manager itself reports a working launcher - so this component
    /// does not rely on it. It waits until this vehicle is the player's, takes the manager's reference out of
    /// the input cache (so one press cannot fire twice) and calls the donor's own <c>FireSmokes</c> with
    /// <c>force</c>, which skips the crew-brain check but keeps the donor's salvos, angles, impulse maths,
    /// sound and projectile pool exactly as shipped.
    ///
    /// Every press is logged with the state that decides whether the call can work, so a press that still does
    /// nothing says why: not initialised, no launcher, no salvos, every slot empty.
    /// </summary>
    public class LpSmokeWiring : MonoBehaviour
    {
        private static readonly System.Reflection.BindingFlags Private =
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;

        private VehicleSmokeManager _manager;
        private bool _adopted;
        private float _next;

        private void Update()
        {
            // The game's own getters are not safe to call before a mission is running (IsCurrentUnit threw
            // from exactly that state and cost a whole mission setup), so one throw here retires the component
            // instead of repeating every frame.
            try
            {
                Tick();
            }
            catch (System.Exception error)
            {
                MelonLogger.Warning("[L2A7V][smoke] smoke key handler stopped: " + error.Message);
                enabled = false;
            }
        }

        private void Tick()
        {
            if (_manager == null)
            {
                _manager = GetComponentInChildren<VehicleSmokeManager>(true);
                if (_manager == null) return;
            }

            if (!_adopted)
            {
                if (Time.unscaledTime < _next) return;
                _next = Time.unscaledTime + 0.5f;
                if (!Adopt()) return;
            }

            if (!Pressed()) return;
            Fire();
        }

        /// <summary>
        /// True once this vehicle is the player's. Taking the manager out of the input cache is deliberate: the
        /// game's own handler would otherwise fire the same salvo a second time on the same press.
        /// </summary>
        private bool Adopt()
        {
            GHPC.Player.PlayerInput input = GHPC.Player.PlayerInput.Instance;
            if (input == null) return false;

            GHPC.Unit player = LpSmoke.CurrentPlayerUnit(input);
            if (player == null || player.gameObject != gameObject) return false;

            var field = input.GetType().GetField("_smokeLauncherManager", Private);
            object cached = field == null ? null : field.GetValue(input);
            if (field != null && !ReferenceEquals(cached, _manager)) field.SetValue(input, (object)null);

            _adopted = true;
            MelonLogger.Msg("[L2A7V][smoke] this vehicle is the player's; the smoke key now fires its launcher " +
                            "directly (donor FireSmokes, force)");
            return true;
        }

        private static System.Reflection.PropertyInfo _mainPlayer;
        private static System.Reflection.MethodInfo _buttonDown;

        /// <summary>
        /// The game's own "Smoke Grenades" binding, which is the button the player is already pressing.
        ///
        /// Reached through reflection rather than a direct call because that property's type is Rewired.Player
        /// and this assembly does not reference Rewired: naming the member in source would make the whole mod
        /// depend on that assembly, while reflection only needs it to exist at run time, which it does.
        /// </summary>
        private static bool Pressed()
        {
            try
            {
                if (_mainPlayer == null)
                    _mainPlayer = typeof(GHPC.Utility.InputUtil).GetProperty("MainPlayer",
                        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public);
                object player = _mainPlayer == null ? null : _mainPlayer.GetValue(null);
                if (player == null) return Input.GetKeyDown(KeyCode.G);
                if (_buttonDown == null)
                    _buttonDown = player.GetType().GetMethod("GetButtonDown", new[] { typeof(string) });
                if (_buttonDown == null) return Input.GetKeyDown(KeyCode.G);
                return (bool)_buttonDown.Invoke(player, new object[] { "Smoke Grenades" });
            }
            catch (System.Exception)
            {
                // Rewired unavailable (main menu, no player). Legacy input is the fallback the rest of this mod
                // already uses for its own keys.
                return Input.GetKeyDown(KeyCode.G);
            }
        }

        private void Fire()
        {
            GHPC.Player.PlayerInput input = GHPC.Player.PlayerInput.Instance;
            GHPC.Unit player = input == null ? null : LpSmoke.CurrentPlayerUnit(input);
            if (player == null || player.gameObject != gameObject) return;

            ForceInitialised();

            int salvos = _manager.SmokeGroups == null ? 0 : _manager.SmokeGroups.Length;
            int rounds = Rounds();
            MelonLogger.Msg("[L2A7V][smoke] smoke key: initialised=" + _manager.IsInitialized +
                            " hasLauncher=" + _manager.HasSmokeLauncher + " salvos=" + salvos +
                            " rounds=" + rounds);
            if (!_manager.IsInitialized || !_manager.HasSmokeLauncher) return;

            try
            {
                _manager.FireSmokes(-1, true, true);
                MelonLogger.Msg("[L2A7V][smoke] FireSmokes called (donor salvo, forced)");
            }
            catch (System.Exception error)
            {
                MelonLogger.Error("[L2A7V][smoke] FireSmokes threw: " + error);
            }
        }

        /// <summary>
        /// A manager that woke up after the game's initialisation state ran never sets this, and every call
        /// then returns at its first line.
        /// </summary>
        private void ForceInitialised()
        {
            if (_manager.IsInitialized) return;
            var field = typeof(VehicleSmokeManager).GetField("<IsInitialized>k__BackingField", Private);
            if (field == null) return;
            field.SetValue(_manager, true);
            MelonLogger.Warning("[L2A7V][smoke] the launcher was never initialised; set it so FireSmokes answers");
        }

        private int Rounds()
        {
            var field = typeof(VehicleSmokeManager).GetField("_smokeSlots", Private);
            var slots = field == null ? null : field.GetValue(_manager) as System.Array;
            if (slots == null) return 0;
            int total = 0;
            foreach (object slot in slots)
            {
                if (slot == null) continue;
                var rounds = slot.GetType().GetField("Rounds");
                if (rounds != null) total += (int)rounds.GetValue(slot);
            }
            return total;
        }
    }
}


