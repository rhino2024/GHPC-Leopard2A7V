using MelonLoader;
using Reticle;
using UnityEngine;

namespace Leopard2A7V
{
    /// <summary>
    /// Gives every reticle on this unit a render order of its own.
    ///
    /// GHPC draws reticles through PostMeshComp, which registers its renderer in a sorted list keyed by
    /// renderOrder. The game's plates all ship with the same order, and the registration only walks 0.001 steps
    /// sixty-four times before it throws - a throw its own catch swallows, so the reticle silently stops
    /// drawing. With several vehicles (and several reticles each) competing for that tiny key window, whole
    /// graticules drop out: the gunner's thermal, the backup sight, the cupola gun.
    ///
    /// Every reticle here is given its own integer order instead, far enough from the authored ones that the
    /// game's own sort first, and is re-registered by cycling the component. Unique keys cannot collide, so a
    /// reticle of this vehicle can no longer be lost whatever else is on the field.
    /// </summary>
    public static class LpReticleOrder
    {
        private const string Tag = "[L2A7V][reticle]";

        /// <summary>Where the allocation starts: above every authored order, so the game's own sort first.</summary>
        private static float _next = 200f;

        public static void Apply(GameObject unitRoot)
        {
            if (unitRoot == null) return;

            int count = 0;
            foreach (PostMeshComp post in unitRoot.GetComponentsInChildren<PostMeshComp>(true))
            {
                if (post == null) continue;

                Renderer renderer = post.GetComponent<Renderer>();
                // PostMeshComp.OnEnable switches its renderer off and hands the mesh to the post pass. The
                // re-registration below therefore has to remember what the renderer was before, because the
                // game only switches the ones it owns back on when their sight opens: a reticle left off by
                // the cycle stays off, and reads on screen as a missing graticule.
                bool wasEnabled = renderer != null && renderer.enabled;

                post.renderOrder = _next;
                _next += 1f;
                count++;

                // OnValidate is editor-only, so the entry is re-registered the runtime way: disabling runs
                // OnDisable (which removes the old key), enabling runs OnEnable (which adds the new one and
                // switches the renderer back off for the post pass).
                if (post.isActiveAndEnabled)
                {
                    post.enabled = false;
                    post.enabled = true;
                }

                if (wasEnabled && renderer != null) renderer.enabled = true;

                MelonLogger.Msg(Tag + " '" + post.transform.name + "' order=" + post.renderOrder +
                                " active=" + post.isActiveAndEnabled +
                                " renderer=" + (renderer != null && renderer.enabled) +
                                " registered=" + (renderer != null &&
                                                  PostMeshRenderer.renderers.ContainsValue(renderer)) +
                                " (list holds " + PostMeshRenderer.renderers.Count + ")");
            }

            if (count > 0)
                MelonLogger.Msg(Tag + " " + count + " reticle(s) given render orders, next free key " + _next);
        }
    }
}
