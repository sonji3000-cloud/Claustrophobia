using System;
using UnityEngine;

/// <summary>Blends the seated crew's hands onto the device's grip targets.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Animator))]
public sealed class CrewHandIK : MonoBehaviour
{
	[Serializable]
	private sealed class Grip
	{
		public CrewDevice Device;
		public Transform LeftHand;
		public Transform RightHand;
		public Transform LeftElbow;
		public Transform RightElbow;
		public string Layer = "Lever Grip";

		[NonSerialized] public int LayerIndex = -1;
	}

	[SerializeField] private Animator anim;
	[SerializeField] private CrewInteract interaction;
	[SerializeField] private Grip[] grips = Array.Empty<Grip>();

	[SerializeField, Min(0.01f)] private float blendTime = .25f;
	[SerializeField, Range(0f, 1f)] private float rotationWeight = 1f;
	[SerializeField, Range(0f, 1f)] private float elbowWeight = .65f;

	private Grip active;

	public float Weight { get; private set; }

	private void Awake()
	{
		if (anim == null) anim = GetComponent<Animator>();
		if (anim == null) return;

		for (int i = 0; i < grips.Length; i++)
			if (grips[i] != null) grips[i].LayerIndex = anim.GetLayerIndex(grips[i].Layer);
	}

	private void Update()
	{
		Grip next = GetGrip();
		float step = Time.deltaTime / Mathf.Max(.01f, blendTime);
		if (active != next)
		{
			SetWeight(Mathf.MoveTowards(Weight, 0f, step));
			if (Weight > 0f) return;

			active = next;
		}

		if (active != null) SetWeight(Mathf.MoveTowards(Weight, 1f, step));
	}

	private Grip GetGrip()
	{
		if (interaction == null || !interaction.isActiveAndEnabled) return null;

		CrewDevice device = interaction.Active;
		if (device == null || device.User != interaction || !device.CanUse(interaction.Crew)) return null;

		for (int i = 0; i < grips.Length; i++)
			if (grips[i] != null && grips[i].Device == device) return grips[i];

		return null;
	}

	private void SetWeight(float weight)
	{
		if (weight == Weight) return;

		Weight = weight;
		if (anim != null && active != null && active.LayerIndex >= 0)
			anim.SetLayerWeight(active.LayerIndex, weight);
	}

	private void OnAnimatorIK(int layer)
	{
		if (anim == null || !anim.isHuman) return;

		Transform leftHand = active != null ? active.LeftHand : null;
		Transform rightHand = active != null ? active.RightHand : null;
		Transform leftElbow = active != null ? active.LeftElbow : null;
		Transform rightElbow = active != null ? active.RightElbow : null;

		ApplyHand(AvatarIKGoal.LeftHand, leftHand);
		ApplyHand(AvatarIKGoal.RightHand, rightHand);
		ApplyElbow(AvatarIKHint.LeftElbow, leftElbow, leftHand);
		ApplyElbow(AvatarIKHint.RightElbow, rightElbow, rightHand);
	}

	private void ApplyHand(AvatarIKGoal goal, Transform target)
	{
		float weight = target != null && target.gameObject.activeInHierarchy ? Weight : 0f;
		anim.SetIKPositionWeight(goal, weight);
		anim.SetIKRotationWeight(goal, weight * rotationWeight);
		if (weight <= 0f) return;

		anim.SetIKPosition(goal, target.position);
		anim.SetIKRotation(goal, target.rotation);
	}

	private void ApplyElbow(AvatarIKHint hint, Transform target, Transform hand)
	{
		float weight = target != null && target.gameObject.activeInHierarchy
			&& hand != null && hand.gameObject.activeInHierarchy ? Weight * elbowWeight : 0f;
		anim.SetIKHintPositionWeight(hint, weight);
		if (weight <= 0f) return;

		anim.SetIKHintPosition(hint, target.position);
	}

	private void OnDisable()
	{
		SetWeight(0f);
		active = null;
	}
}
