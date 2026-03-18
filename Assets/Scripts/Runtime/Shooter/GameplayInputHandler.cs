using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Handles pointer input for shooter placement: raycasts to detect shooter clicks and drops, raises ShooterPressed and ShooterDropped on the event bus.
/// Subscribers (e.g. ShooterContainer) react and may call SetDragging/ClearDragging to track drag state.
/// Add to a GameObject in the scene; register with ServiceLocator so ShooterContainer can call SetDragging/ClearDragging.
/// </summary>
public class GameplayInputHandler : MonoBehaviour
{
    private Camera _camera;

    [Tooltip("Layers that can be clicked to select shooters. Leave empty to use all layers.")]
    [SerializeField] private LayerMask _clickLayerMask;

    private GameEventBus _eventBus;

    private void Awake()
    {
        ServiceLocator.Register(this);
    }

    private void Start()
    {
        _camera = Camera.main;
        _eventBus = ServiceLocator.Resolve<GameEventBus>();
    }

    private void OnDestroy()
    {
        ServiceLocator.Unregister<GameplayInputHandler>();
    }

    private void Update()
    {
        if (_camera == null) return;

        if (Input.GetMouseButtonUp(0))
        {
            // Do not interact with shooters when pointer is over UI.
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                return;

            Ray ray = _camera.ScreenPointToRay(Input.mousePosition);
            bool hitSomething;
            RaycastHit hit;

            // If no layers specified, raycast against everything; otherwise, restrict to the mask.
            if (_clickLayerMask.value == 0)
            {
                hitSomething = Physics.Raycast(ray, out hit);
            }
            else
            {
                hitSomething = Physics.Raycast(ray, out hit, Mathf.Infinity, _clickLayerMask);
            }

            if (hitSomething)
            {
                var shooter = hit.collider.GetComponentInParent<Shooter>();
                if (shooter != null)
                {
                    _eventBus?.RaiseShooterSelected(shooter);
                }
            }
        }
    }
}
