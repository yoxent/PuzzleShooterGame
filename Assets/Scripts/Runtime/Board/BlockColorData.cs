using UnityEngine;

/// <summary>
/// Defines one color type for matching. Holds materials for block and shooter (inactive/active) so block and shooter visuals stay in sync.
/// Block color is applied via one shared material plus per-object color using MaterialPropertyBlock (shader property _Color, case-sensitive).
/// </summary>
[CreateAssetMenu(menuName = "Demo/Block Color Data", fileName = "BlockColorData")]
public class BlockColorData : ScriptableObject
{
    /// <summary>Shader property name for base color. Case-sensitive; must match the material's shader.</summary>
    public static readonly int ColorPropertyId = Shader.PropertyToID("_BaseColor");

    [Header("Colors")]
    [Tooltip("Color applied to blocks via _Color property block.")]
    [SerializeField] private Color _blockColor = Color.white;
    [Tooltip("Color applied to active shooters via _Color property block.")]
    [SerializeField] private Color _activeShootersColor = Color.white;
    [Tooltip("Color applied to inactive shooters via _Color property block.")]
    [SerializeField] private Color _inactiveShootersColor = Color.white;

    [Header("Block")]
    [Tooltip("Shared material for blocks (e.g. white base). Per-block color is applied via MaterialPropertyBlock.")]
    [SerializeField] private Material _blockMaterial;
    [SerializeField] private bool _isSpecialTarget;

    [Header("Shooter")]
    [Tooltip("Material for the shooter when not placed / inactive.")]
    [SerializeField] private Material _inactiveShooterMaterial;
    [Tooltip("Material for the shooter when placed and active.")]
    [SerializeField] private Material _activeShooterMaterial;

    /// <summary>Material for blocks of this color (shared; use with MaterialPropertyBlock for per-object color).</summary>
    public Material BlockMaterial => _blockMaterial;

    public bool IsSpecialTarget => _isSpecialTarget;

    /// <summary>Material for the shooter when inactive (e.g. in the tray).</summary>
    public Material InactiveShooterMaterial => _inactiveShooterMaterial;

    /// <summary>Material for the shooter when placed and active.</summary>
    public Material ActiveShooterMaterial => _activeShooterMaterial;

    /// <summary>Base color used for blocks of this type.</summary>
    public Color BlockColor => _blockColor;

    /// <summary>Color used for active shooters of this type.</summary>
    public Color ActiveShooterColor => _activeShootersColor;

    /// <summary>Color used for inactive shooters of this type.</summary>
    public Color InactiveShooterColor => _inactiveShootersColor;

    public void ApplyBlockColorTo(MaterialPropertyBlock block)
    {
        if (block == null) return;
        block.SetColor(ColorPropertyId, _blockColor);
    }

    public void ApplyActiveShooterColor(MaterialPropertyBlock block)
    {
        if (block == null) return;
        block.SetColor(ColorPropertyId, _activeShootersColor);
    }
    public void ApplyInactiveShooterColor(MaterialPropertyBlock block)
    {
        if (block == null) return;
        block.SetColor(ColorPropertyId, _inactiveShootersColor);
    }
}
