using UnityEngine;

/// <summary>Blends the seated crew's hands onto the device's grip targets.</summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Animator))]
public sealed class CrewHandIK : MonoBehaviour
{
	[SerializeField] private Animator anim;
	[SerializeField] private CrewInteract interaction;
	[SerializeField] private CrewDevice device;

	[SerializeField] private Transform leftHand;
	[SerializeField] private Transform rightHand;
	[SerializeField] private Transform leftElbow;
	[SerializeField] private Transform rightElbow;

	[SerializeField, Min(0.01f)] private float blendTime = .25f;
	[SerializeField, Range(0f, 1f)] private float rotationWeight = 1f;
	[SerializeField, Range(0f, 1f)] private float elbowWeight = .65f;
	[SerializeField] private string gripLayer = "Lever Grip";

	private bool holding;
	private int layerIndex = -1;

	public float Weight { get; private set; }

	private void Awake()
	{
		if (anim == null) anim = GetComponent<Animator>();
		if (anim != null) layerIndex = anim.GetLayerIndex(gripLayer);
	}

	private void OnEnable()
	{
		if (interaction != null)
		{
			interaction.UseChanged += OnUseChanged;
			holding = interaction.Active == device && device != null;
		}
	}

	private void Update()
	{
		if (holding && (interaction == null || !interaction.isActiveAndEnabled
			|| device == null || device.User != interaction || !device.CanUse(interaction.Crew)))
			holding = false;

		float target = holding ? 1f : 0f;
		float weight = Mathf.MoveTowards(Weight, target, Time.deltaTime / Mathf.Max(.01f, blendTime));
		if (weight == Weight) return;

		Weight = weight;
		if (anim != null && layerIndex >= 0) anim.SetLayerWeight(layerIndex, weight);
	}

	private void OnUseChanged(CrewDevice used, bool active)
	{
		if (used == device) holding = active;
	}

	private void OnAnimatorIK(int layer)
	{
		if (anim == null || !anim.isHuman) return;

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
		if (interaction != null) interaction.UseChanged -= OnUseChanged;

		holding = false;
		Weight = 0f;
		if (anim != null && layerIndex >= 0) anim.SetLayerWeight(layerIndex, 0f);
	}
}
