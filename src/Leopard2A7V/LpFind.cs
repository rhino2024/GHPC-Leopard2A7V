using GHPC;
using UnityEngine;

namespace Leopard2A7V;

public static class LpFind
{
	internal static VehicleInfo VehicleInfo(GameObject unitRoot)
	{
		if (unitRoot == null)
		{
			return null;
		}
		VehicleInfo componentInChildren = unitRoot.GetComponentInChildren<VehicleInfo>(includeInactive: true);
		if (componentInChildren != null)
		{
			return componentInChildren;
		}
		Unit unit = unitRoot.GetComponent<Unit>();
		if (unit == null)
		{
			unit = unitRoot.GetComponentInChildren<Unit>(includeInactive: true);
		}
		// Never search a parent's whole subtree: it can contain sibling vehicles.
		// A donor without VehicleInfo in its own hierarchy remains uninitialized.
		return null;
	}

	internal static Unit Unit(GameObject unitRoot)
	{
		if (unitRoot == null)
		{
			return null;
		}
		Unit component = unitRoot.GetComponent<Unit>();
		if (component != null)
		{
			return component;
		}
		component = unitRoot.GetComponentInChildren<Unit>(includeInactive: true);
		if (component != null)
		{
			return component;
		}
		VehicleInfo vehicleInfo = VehicleInfo(unitRoot);
		if (vehicleInfo != null)
		{
			component = vehicleInfo.GetComponent<Unit>();
			if (component != null)
			{
				return component;
			}
			component = vehicleInfo.GetComponentInParent<Unit>();
		}
		return component;
	}
}
