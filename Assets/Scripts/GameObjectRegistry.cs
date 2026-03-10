using System.Collections.Generic;

public static class GameObjectRegistry
{
    public static readonly List<MinionHealth>     Minions    = new();
    public static readonly List<StructureHealth>  Structures = new();
    public static readonly List<PlayerController> Players    = new();
}
