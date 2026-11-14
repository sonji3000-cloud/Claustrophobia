using System;
using Fusion;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class TankHealth : NetworkBehaviour
{
	[Min(0.01f)] public float MaxHP = 100f;

	[Networked, OnChangedRender(nameof(ApplyAppearance))]
	private float NetworkHP { get; set; }
	[Networked] private float NetworkMaxHP { get; set; }
	[Networked] private int NetworkHits { get; set; }

	private NetworkObject networkObject;
	private bool spawned;
	private float localHP;
	private int localHits;
	private Renderer[] damagedRenderers;
	private MaterialPropertyBlock[] originalBlocks;

	public bool IsReady => networkObject == null || spawned;
	public float CurrentHP => spawned ? NetworkHP : localHP;
	public float Capacity => spawned ? NetworkMaxHP : MaxHP;
	public int DamageHitCount => spawned ? NetworkHits : localHits;
	public bool Destroyed => CurrentHP <= 0f;
	public int DestructionCount => Destroyed ? 1 : 0;
	public int LastProjectileId { get; private set; }
	public float LastDamage { get; private set; }
	public float LastHPBefore { get; private set; }
	public float LastHPAfter { get; private set; }
	public string LastHitResult { get; private set; } = "No hit";
	public event Action Died;

	private void Awake()
	{
		networkObject = GetComponentInParent<NetworkObject>();
		ValidateMaxHP();
		localHP = MaxHP;
	}

	public override void Spawned()
	{
		spawned = true;
		if (HasStateAuthority)
		{
			NetworkMaxHP = MaxHP;
			NetworkHP = MaxHP;
			NetworkHits = 0;
		}

		ApplyAppearance();
	}

	public override void Despawned(NetworkRunner runner, bool hasState)
	{
		spawned = false;
		RestoreAppearance();
	}

	public bool ApplyDamage(float damage, int projectileId,
		out float before, out float after, out string result)
	{
		before = after = CurrentHP;
		if (networkObject != null && (!spawned || !networkObject.IsValid || !HasStateAuthority
			|| Runner == null || !Runner.IsRunning || !Runner.IsServer))
		{
			result = "Rejected: host authority required";
			return false;
		}

		LastProjectileId = projectileId;
		LastDamage = damage;
		LastHPBefore = before;
		bool applied = false;
		if (Destroyed) result = "Rejected: already destroyed";
		else if (!isActiveAndEnabled) result = "Rejected: target inactive";
		else if (float.IsNaN(damage) || float.IsInfinity(damage) || damage <= 0f)
			result = "Rejected: positive finite damage required";
		else
		{
			after = Mathf.Max(0f, before - damage);
			if (spawned)
			{
				NetworkHP = after;
				NetworkHits++;
			}
			else
			{
				localHP = after;
				localHits++;
			}
			applied = true;
			result = "Damage applied";
		}

		LastHPAfter = after;
		LastHitResult = result;
		if (applied && Destroyed)
		{
			Died?.Invoke();
			ApplyAppearance();
			Debug.Log($"Projectile #{projectileId}: {name} destroyed; HP {before:F1} -> 0", this);
		}

		Debug.Log($"Projectile #{projectileId}: target={name}; damage={damage:F1}; HP {before:F1} -> {after:F1}; {result}", this);
		return applied;
	}

	private void ApplyAppearance()
	{
		if (!Destroyed)
		{
			RestoreAppearance();
			return;
		}
		if (damagedRenderers != null) return;

		damagedRenderers = GetComponentsInChildren<Renderer>(true);
		originalBlocks = new MaterialPropertyBlock[damagedRenderers.Length];
		var block = new MaterialPropertyBlock();
		Color dark = new Color(0.10f, 0.12f, 0.13f, 1f);
		for (int i = 0; i < damagedRenderers.Length; i++)
		{
			Renderer visual = damagedRenderers[i];
			var original = new MaterialPropertyBlock();
			visual.GetPropertyBlock(original);
			originalBlocks[i] = original;
			visual.GetPropertyBlock(block);
			block.SetColor("_BaseColor", dark);
			block.SetColor("_Color", dark);
			visual.SetPropertyBlock(block);
		}
	}

	private void RestoreAppearance()
	{
		if (damagedRenderers == null) return;
		for (int i = 0; i < damagedRenderers.Length; i++)
			if (damagedRenderers[i] != null) damagedRenderers[i].SetPropertyBlock(originalBlocks[i]);

		damagedRenderers = null;
		originalBlocks = null;
	}

	private void OnValidate() => ValidateMaxHP();
	private void ValidateMaxHP()
	{
		if (float.IsNaN(MaxHP) || float.IsInfinity(MaxHP) || MaxHP <= 0f) MaxHP = 100f;
	}
}
