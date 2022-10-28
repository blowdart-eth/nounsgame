﻿namespace NGE.Engine;

public abstract class UpdateContext
{
    public Definitions? Definitions => GameState?.definitions;

    #region Non-Retained Data

    public GameState? GameState { get; set; }
    public IRandomProvider? random;

    public void Reset()
    {
        GameState = null!;
        random = null;
    }

    #endregion
}