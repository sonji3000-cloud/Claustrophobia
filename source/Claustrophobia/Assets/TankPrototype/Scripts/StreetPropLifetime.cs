using UnityEngine;

[DisallowMultipleComponent]
public sealed class StreetPropLifetime : MonoBehaviour
{
	[SerializeField] private CarentanDrive drive;
	[SerializeField] private Collider[] colliders;
	[SerializeField, Min(0f)] private float passAfter = 1f;
	[SerializeField, Min(0f)] private float removeAfter = 5f;

	private bool hit;
	private bool passThrough;

	private void OnCollisionEnter(Collision collision)
	{
		if (hit || !isActiveAndEnabled || drive == null) return;

		var tank = collision.rigidbody;
		if (tank == null || !tank.TryGetComponent(out TankMotor motor) || tank.isKinematic) return;

		hit = true;
		drive.RegisterTank(motor.gameObject);
		Invoke(nameof(OpenToTanks), passAfter);
		Destroy(gameObject, Mathf.Max(passAfter, removeAfter));
	}

	private void OpenToTanks()
	{
		passThrough = true;
		drive.OpenProp(this);
	}

	internal void IgnoreTank(Collider tank)
	{
		if (tank == null || !tank.enabled || !tank.gameObject.activeInHierarchy) return;

		foreach (Collider collider in colliders)
			if (collider != null && collider.enabled && collider.gameObject.activeInHierarchy)
				Physics.IgnoreCollision(collider, tank);
	}

	internal void RefreshTankContacts()
	{
		if (passThrough && drive != null) drive.OpenProp(this);
	}

	private void OnDestroy()
	{
		if (passThrough && drive != null) drive.ForgetProp(this);
	}
}
