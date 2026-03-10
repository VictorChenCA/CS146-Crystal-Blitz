using UnityEngine;

[System.Serializable]
public class MinionSettings
{
    public int   spawnCount           = 3;
    public float spawnInterval        = 30f;
    public float moveSpeed            = 4f;
    public float attackDamage         = 15f;
    public float maxHealth            = 100f;
    public float attackRange          = 1.5f;
    public float attackCooldown       = 1.2f;
    public float aggroRange           = 8f;
    public float targetUpdateInterval = 0.3f;
    public float navStoppingDistance  = 0.5f;
    public GameObject minionPrefab;
}
