using System.Collections.Generic;
using System.Text;
using GHPC.Camera;
using GHPC.Equipment.Optics;
using MelonLoader;
using Reticle;
using UnityEngine;

namespace Leopard2A7V
{
    /// <summary>
    /// Keeps the graticule of the sight the player is looking through switched on, and reports what the game's
    /// reticle pass holds.
    ///
    /// GHPC draws reticles through PostMeshComp, which registers its renderer in a static sorted list and then
    /// switches the renderer itself off - the post pass is what draws it. That arrangement leaves the renderer
    /// flag as a second gate that the game only opens for some reticles: with several vehicles in the scene the
    /// backup sight's plate and the cupola gun's plate sit in the list with their renderer off, and a renderer
    /// that is off is a graticule that is not drawn.
    ///
    /// So the plate of whichever optic is the active view is asserted on every frame. Nothing else is touched:
    /// the list itself, the render order and every other vehicle stay exactly as they are.
    /// </summary>
    public class LpReticleWatch : MonoBehaviour
    {
        private const string Tag = "[L2A7V][reticle]";

        private PostMeshComp[] _posts = new PostMeshComp[0];
        private float _nextCollect;
        private float _nextReport;
        private float _nextRebuild;
        private string _reportedView;

        private void LateUpdate()
        {
            if (Time.unscaledTime >= _nextCollect)
            {
                _nextCollect = Time.unscaledTime + 2f;
                _posts = GetComponentsInChildren<PostMeshComp>(true);
            }

            CameraSlot active = CameraSlot.ActiveInstance;
            if (active == null) return;

            for (int i = 0; i < _posts.Length; i++)
            {
                PostMeshComp post = _posts[i];
                if (post == null) continue;

                UsableOptic optic = post.GetComponentInParent<UsableOptic>();
                if (optic == null || !ReferenceEquals(optic.slot, active)) continue;

                Renderer renderer = post.GetComponent<Renderer>();
                if (renderer != null && !renderer.enabled) renderer.enabled = true;

                Repair(post);
            }

            Report(active);
        }

        /// <summary>
        /// A reticle whose mesh has no vertices draws nothing however healthy its renderer and list entry look -
        /// which is exactly what the backup sight and the imager were doing: registered, switched on, and empty.
        /// ReticleMesh.Load stops immediately when the renderer already holds a mesh, so an empty one is never
        /// replaced by itself; dropping it and loading again rebuilds the plate's geometry.
        /// </summary>
        private void Repair(PostMeshComp post)
        {
            ReticleMesh mesh = post.GetComponent<ReticleMesh>();
            SkinnedMeshRenderer smr = post.GetComponent<SkinnedMeshRenderer>();
            if (mesh == null || smr == null) return;
            if (smr.sharedMesh != null && smr.sharedMesh.vertexCount > 0) return;
            if (Time.unscaledTime < _nextRebuild) return;

            _nextRebuild = Time.unscaledTime + 1f;
            smr.sharedMesh = null;
            mesh.Load();
            Mesh built = smr.sharedMesh;
            MelonLogger.Msg(Tag + " rebuilt the empty graticule '" + post.transform.name + "': " +
                            (built == null ? "<still empty>" : built.vertexCount + " verts"));
        }

        /// <summary>What the pass holds while this vehicle's sight is up, every few seconds.</summary>
        private void Report(CameraSlot active)
        {
            if (Time.unscaledTime < _nextReport) return;
            _nextReport = Time.unscaledTime + 3f;
            if (active.name == _reportedView) return;
            _reportedView = active.name;

            var list = new StringBuilder();
            foreach (var entry in PostMeshRenderer.renderers)
            {
                Renderer renderer = entry.Value;
                list.Append(' ').Append(entry.Key.ToString("0")).Append('=')
                    .Append(renderer == null ? "<null>" : renderer.name)
                    .Append(renderer != null && renderer.enabled ? "(on)" : "(off)");
            }

            MelonLogger.Msg(Tag + " view '" + active.name + "': game list holds " +
                            PostMeshRenderer.renderers.Count + ":" + list);

            for (int i = 0; i < _posts.Length; i++)
            {
                PostMeshComp post = _posts[i];
                if (post == null) continue;
                UsableOptic optic = post.GetComponentInParent<UsableOptic>();
                if (optic == null || !ReferenceEquals(optic.slot, active)) continue;

                Renderer renderer = post.GetComponent<Renderer>();
                string where = renderer == null
                    ? "<no renderer>"
                    : "bounds c=" + renderer.bounds.center + " s=" + renderer.bounds.size;
                Camera camera = CameraManager.MainCam;
                if (camera != null) where += " camera=" + camera.transform.position;
                SkinnedMeshRenderer skinned = post.GetComponent<SkinnedMeshRenderer>();
                if (skinned != null)
                {
                    where += " verts=" + (skinned.sharedMesh == null
                                  ? "<null>"
                                  : skinned.sharedMesh.vertexCount.ToString()) +
                             " bones=" + (skinned.bones == null ? 0 : skinned.bones.Length) +
                             " rootBone=" + (skinned.rootBone == null ? "<null>" : skinned.rootBone.name);
                }

                MelonLogger.Msg(Tag + "   active sight's reticle '" + post.transform.name + "' order=" +
                                post.renderOrder + " active=" + post.isActiveAndEnabled +
                                " gameObject=" + post.gameObject.activeInHierarchy +
                                " renderer=" + (renderer != null && renderer.enabled) +
                                " registered=" + (renderer != null &&
                                                  PostMeshRenderer.renderers.ContainsValue(renderer)) +
                                " " + where);
            }
        }
    }
}
