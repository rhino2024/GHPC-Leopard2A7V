using MelonLoader;
using Reticle;
using UnityEngine;

namespace Leopard2A7V;

public static class LpReticleOrder
{
	private const string Tag = "[L2A7V][reticle]";

	private static float _next = 200f;

	public static void Apply(GameObject unitRoot)
	{
		if (unitRoot == null)
		{
			return;
		}
		int num = 0;
		PostMeshComp[] componentsInChildren = unitRoot.GetComponentsInChildren<PostMeshComp>(includeInactive: true);
		foreach (PostMeshComp postMeshComp in componentsInChildren)
		{
			if (!(postMeshComp == null))
			{
				Renderer component = postMeshComp.GetComponent<Renderer>();
				bool num2 = component != null && component.enabled;
				postMeshComp.renderOrder = _next;
				_next += 1f;
				num++;
				if (postMeshComp.isActiveAndEnabled)
				{
					postMeshComp.enabled = false;
					postMeshComp.enabled = true;
				}
				if (num2 && component != null)
				{
					component.enabled = true;
				}
				MelonLogger.Msg("[L2A7V][reticle] '" + postMeshComp.transform.name + "' order=" + postMeshComp.renderOrder + " active=" + postMeshComp.isActiveAndEnabled + " renderer=" + (component != null && component.enabled) + " registered=" + (component != null && PostMeshRenderer.renderers.ContainsValue(component)) + " (list holds " + PostMeshRenderer.renderers.Count + ")");
			}
		}
		if (num > 0)
		{
			MelonLogger.Msg("[L2A7V][reticle] " + num + " reticle(s) given render orders, next free key " + _next);
		}
	}
}
