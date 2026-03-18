using System;
using UnityEngine;
using UnityEngine.UI;

[Serializable]
public struct ParticleEffectConfig
{
    [Tooltip("Particle system prefab to instantiate when the event fires.")]
    public ParticleSystem Prefab;

    [Tooltip("Lifetime in seconds before the instance is destroyed. " +
             "If <= 0, falls back to the particle system's main.duration.")]

    public Transform SpawnPoint;
}

[Serializable]
public struct ShooterColorEntry
{
    public BlockColorData Color;
    public bool IsHidden;
    public bool IsLinked;
    public int LinkedToIndex;
}

[Serializable]
public struct ShooterLink
{
    public Shooter Owner;
    public Shooter Linked;
}

[Serializable]
public struct HighlightOverlaySettings
{
    [Tooltip("Sprite used for the highlight overlay mask.")]
    public Sprite SpriteOverlay;

    [Tooltip("Scale (X,Y) applied to the highlight mask around the target.")]
    public Vector2 HighlightScale;

    [Tooltip("Additional offset (X,Y) in UV space for fine-tuning the highlight center.")]
    public Vector2 HighlightOffset;
}

[Serializable]
public struct TutorialData
{
    [TextArea]
    [Tooltip("Flavor text displayed during the tutorial step.")]
    public string FlavorText;

    [TextArea]
    [Tooltip("Instruction text telling the player what to do.")]
    public string InstructionText;

    [Tooltip("Overlay highlight scale and offset.")]
    public HighlightOverlaySettings OverlaySettings;

    [Tooltip("The position of the hand pointer")]
    public Vector2 PointerPosition;

    [Tooltip("How many clicks on the highligh mask before closing the tutorial?")]
    public int ClicksAllowed;

    public float ExitDelay;
}

[Serializable]
public struct LevelTutorialEntry
{
    [Tooltip("1-based level index this tutorial applies to.")]
    public int LevelIndex;

    public TutorialData Tutorial;
}

/// <summary>Per-cell data: color (null = empty) and tier (cubes stacked).</summary>
[Serializable]
public struct LevelCell
{
    [SerializeField] private BlockColorData _color;
    [SerializeField] private int _tier;

    public BlockColorData Color
    {
        get => _color;
        set => _color = value;
    }

    public int Tier
    {
        get => _tier < 1 ? 1 : _tier;
        set => _tier = value < 1 ? 1 : value;
    }

    public static LevelCell Empty => new LevelCell { _tier = 1 };
}

[Serializable]
public struct UnlockableFeature
{
    //What level does unlocking this feature start?
    public int LevelUnlockFeatureStart;
    //What level does the feature unlocks?
    public int LevelUnlockFeatureEnd;
    //What level does the feature gets showcased?
    public int LevelFeatureShowcase;
    //Sprite representation of what will be unlocked
    public Sprite UnlockableImage;
    public string FlavorText;

    [Tooltip("Overlay highlight scale and offset.")]
    public HighlightOverlaySettings OverlaySettings;
}

[Serializable]
public struct EnvironmentMaterial
{
    public int Level;
    public Color MaterialColor;
}