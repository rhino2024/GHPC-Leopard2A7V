using UnityEngine;

namespace Leopard2A7V;

public sealed class ObjGeometry
{
	public string ObjectName;

	public int MaterialIndex;

	public LpGroup Group;

	public Vector3[] Positions;

	public Vector2[] Uvs;

	public int[] Triangles;

	public Vector3[] Normals;

	public Vector4[] Tangents;

	public Bounds Bounds;
}
