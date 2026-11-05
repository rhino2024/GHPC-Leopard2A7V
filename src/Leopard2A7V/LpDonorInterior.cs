using UnityEngine;

namespace Leopard2A7V;

public static class LpDonorInterior
{
	public static void Attach(GameObject unitRoot)
	{
		if (!(unitRoot == null) && !(unitRoot.GetComponent<LpDonorInteriorHider>() != null))
		{
			unitRoot.AddComponent<LpDonorInteriorHider>();
		}
	}
}
