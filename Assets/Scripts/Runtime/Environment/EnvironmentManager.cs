using UnityEngine;

public class EnvironmentManager : MonoBehaviour
{
    // Standard shader uses _Color for albedo; URP uses _BaseColor.
    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private MaterialPropertyBlock _propertyBlock;

    [SerializeField] private Renderer[] _renderers;

    [SerializeField] private Color _normalLevelColor;
    [SerializeField] private Color _hardLevelColor;

    private void Awake()
    {
        ServiceLocator.Register(this);
    }

    private void OnDestroy()
    {
        ServiceLocator.Unregister<EnvironmentManager>();
    }

    public void UpdateMaterial(bool isHardLevel)
    {
        if (_renderers == null) return;

        Color color = isHardLevel ? _hardLevelColor : _normalLevelColor;

        if (_propertyBlock == null)
        {
            _propertyBlock = new MaterialPropertyBlock();
        }

        for (int i = 0; i < _renderers.Length; i++)
        {
            Renderer r = _renderers[i];
            if (r == null) continue;

            r.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetColor(ColorId, color);
            r.SetPropertyBlock(_propertyBlock);
        }
    }
}
