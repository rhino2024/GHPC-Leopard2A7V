using System;
using System.Collections.Generic;
using System.Text;
using GHPC.Camera;
using GHPC.Equipment.Optics;
using MelonLoader;
using Reticle;
using UnityEngine;

namespace Leopard2A7V;

public class LpReticleWatch : MonoBehaviour
{
	private const string Tag = "[L2A7V][reticle]";

	private sealed class Entry
	{
		internal PostMeshComp Post;

		internal UsableOptic Optic;

		internal Renderer Renderer;

		internal ReticleMesh Mesh;

		internal SkinnedMeshRenderer Skin;
	}

	private Entry[] _posts = Array.Empty<Entry>();

	private float _nextRebuild;

	private CameraSlot _activeSlot;

	private void OnEnable()
	{
		CameraSlot.ActiveInstanceChanged += HandleSlotChanged;
		HandleSlotChanged(CameraSlot.ActiveInstance);
	}

	private void OnDisable()
	{
		CameraSlot.ActiveInstanceChanged -= HandleSlotChanged;
		_activeSlot = null;
	}

	private void HandleSlotChanged(CameraSlot slot)
	{
		_activeSlot = ((slot != null && slot.transform.IsChildOf(base.transform)) ? slot : null);
		if (_activeSlot != null)
		{
			Collect();
			for (int i = 0; i < _posts.Length; i++)
			{
				Entry entry = _posts[i];
				if (entry.Optic != null && (object)entry.Optic.slot == _activeSlot)
				{
					if (entry.Renderer != null)
					{
						entry.Renderer.enabled = true;
					}
					Repair(entry);
				}
			}
		}
	}

	private void LateUpdate()
	{
		CameraSlot activeInstance = _activeSlot;
		if (activeInstance == null)
		{
			return;
		}
		for (int i = 0; i < _posts.Length; i++)
		{
			Entry entry = _posts[i];
			if (entry.Post == null || entry.Optic == null || (object)entry.Optic.slot != activeInstance)
			{
				continue;
			}
			if (entry.Renderer != null && !entry.Renderer.enabled)
			{
				entry.Renderer.enabled = true;
			}
			Repair(entry);
		}
	}

	private void Collect()
	{
		PostMeshComp[] componentsInChildren = GetComponentsInChildren<PostMeshComp>(includeInactive: true);
		_posts = new Entry[componentsInChildren.Length];
		for (int i = 0; i < componentsInChildren.Length; i++)
		{
			PostMeshComp post = componentsInChildren[i];
			_posts[i] = new Entry
			{
				Post = post,
				Optic = ((post == null) ? null : post.GetComponentInParent<UsableOptic>()),
				Renderer = ((post == null) ? null : post.GetComponent<Renderer>()),
				Mesh = ((post == null) ? null : post.GetComponent<ReticleMesh>()),
				Skin = ((post == null) ? null : post.GetComponent<SkinnedMeshRenderer>())
			};
		}
	}

	private void Repair(Entry entry)
	{
		if (!(entry.Mesh == null) && !(entry.Skin == null) && (!(entry.Skin.sharedMesh != null) || entry.Skin.sharedMesh.vertexCount <= 0) && !(Time.unscaledTime < _nextRebuild))
		{
			_nextRebuild = Time.unscaledTime + 1f;
			entry.Skin.sharedMesh = null;
			entry.Mesh.Load();
			Mesh sharedMesh = entry.Skin.sharedMesh;
			MelonLogger.Msg("[L2A7V][reticle] rebuilt the empty graticule '" + entry.Post.transform.name + "': " + ((sharedMesh == null) ? "<still empty>" : (sharedMesh.vertexCount + " verts")));
		}
	}

}
