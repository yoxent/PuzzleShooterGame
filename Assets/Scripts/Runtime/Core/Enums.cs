/// <summary>How the shooter picks a target from the list of attackable blocks.</summary>
public enum ShooterTargetingMode
{
    Random,
    /// <summary>Cycle through targets in list order (0, 1, 2, ...). Per-shooter index.</summary>
    RoundRobin,
    /// <summary>Cycle through targets in list order; index is shared by all shooters of the same color.</summary>
    SharedRoundRobin,
    /// <summary>Always target the first block in the list (e.g. leftmost/front if list order is stable).</summary>
    First,
    ClosestFirst
}

/// <summary>How many projectiles the shooter can have in flight at once.</summary>
public enum ShooterFireMode
{
    Single,
    Multiple
}

public enum GameState
{
    None,
    Win,
    Lose
}
