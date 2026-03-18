using UnityEngine;

[CreateAssetMenu(menuName = "Demo/Unlockables Catalog", fileName = "UnlockablesCatalog")]
public class UnlockablesCatalog : ScriptableObject
{
    [SerializeField] private UnlockableFeature[] _entries;
    public UnlockableFeature[] Entries => _entries;
}