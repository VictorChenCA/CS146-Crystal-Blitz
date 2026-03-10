public interface IDamageable
{
    float MaxHealth { get; }
    float CurrentHealth { get; }
    bool IsImmuneTo(ulong attackerClientId);
    void TakeDamage(float amount, ulong attackerClientId);
}
