using System;
using System.Reflection;
using UnityEngine;

namespace Leopard2A7V;

public class LpHatchMotion : MonoBehaviour
{
	private Transform _pivot;

	private Vector3 _axis;

	private float _degrees;

	private Component _controller;

	private string _parameter;

	internal void Begin(Transform pivot, Vector3 axis, float degrees, Component controller, string parameter)
	{
		_pivot = pivot;
		_axis = axis;
		_degrees = degrees;
		_controller = controller;
		_parameter = parameter;
	}

	private void Update()
	{
		if (_pivot == null)
		{
			return;
		}
		float num = 0f;
		if (_controller != null && !string.IsNullOrEmpty(_parameter))
		{
			try
			{
				PropertyInfo property = _controller.GetType().GetProperty("parameters");
				Array array = ((property == null) ? null : (property.GetValue(_controller) as Array));
				if (array != null)
				{
					foreach (object item in array)
					{
						FieldInfo field = item.GetType().GetField("name");
						if (!(field == null) && !((string)field.GetValue(item) != _parameter))
						{
							MethodInfo method = _controller.GetType().GetMethod("GetFloat", new Type[1] { typeof(string) });
							if (method != null)
							{
								num = Mathf.Clamp01((float)method.Invoke(_controller, new object[1] { _parameter }));
							}
							break;
						}
					}
				}
			}
			catch (Exception)
			{
			}
		}
		_pivot.localRotation = Quaternion.AngleAxis(num * _degrees, _axis);
	}
}
