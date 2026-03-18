using UnityEngine;

/// <summary>
/// Holds the mapping of levels to their tutorial sequences.
/// Create via Assets > Create > Demo/Tutorial Catalog.
/// </summary>
[CreateAssetMenu(menuName = "Demo/Tutorial Catalog", fileName = "TutorialCatalog")]
public class TutorialCatalog : ScriptableObject
{
    [SerializeField] private LevelTutorialEntry[] _entries;

    public LevelTutorialEntry[] Entries => _entries;
}
