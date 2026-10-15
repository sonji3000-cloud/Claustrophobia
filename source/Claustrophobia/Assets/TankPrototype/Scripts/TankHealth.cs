using UnityEngine;

[DisallowMultipleComponent]
public sealed class TankHealth : MonoBehaviour
{
    [Min(0.01f)] public float MaxHP = 100f;
    public float CurrentHP { get; private set; }
    public bool Destroyed => CurrentHP <= 0f;
    public int DamageHitCount { get; private set; }
    public int DestructionCount { get; private set; }
    public int LastProjectileId { get; private set; }
    public float LastDamage { get; private set; }
    public float LastHPBefore { get; private set; }
    public float LastHPAfter { get; private set; }
    public string LastHitResult { get; private set; } = "No hit";

    private void Awake() => CurrentHP = MaxHP;

    // Only the colliding projectile calls this damage entry point.
    public bool ApplyDamage(float damage, int projectileId,
        out float before, out float after, out string result)
    {
        before = CurrentHP;
        after = CurrentHP;
        LastProjectileId = projectileId;
        LastDamage = damage;
        LastHPBefore = before;
        if (Destroyed) result = "Rejected: already destroyed";
        else if (float.IsNaN(damage) || float.IsInfinity(damage) || damage <= 0f)
            result = "Rejected: positive finite damage required";
        else
        {
            CurrentHP = Mathf.Max(0f, CurrentHP - damage);
            after = CurrentHP;
            DamageHitCount++;
            result = "Damage applied";
            if (Destroyed)
            {
                DestructionCount++;
                var block = new MaterialPropertyBlock();
                foreach (Renderer visual in GetComponentsInChildren<Renderer>(true))
                {
                    visual.GetPropertyBlock(block);
                    Color dark = new Color(0.10f, 0.12f, 0.13f, 1f);
                    block.SetColor("_BaseColor", dark);
                    block.SetColor("_Color", dark);
                    visual.SetPropertyBlock(block);
                    block.Clear();
                }
                Debug.Log($"Projectile #{projectileId}: {name} destroyed; HP {before:F1} -> 0", this);
            }
        }
        LastHPAfter = after;
        LastHitResult = result;
        Debug.Log($"Projectile #{projectileId}: target={name}; damage={damage:F1}; HP {before:F1} -> {after:F1}; {result}", this);
        return result == "Damage applied";
    }
}
