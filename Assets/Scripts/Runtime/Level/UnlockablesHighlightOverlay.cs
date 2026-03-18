using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Highlight overlay used on the level unlockables screen.
/// Inherits shared scale/offset + material logic from HighlightOverlayBase, with no click behaviour.
/// </summary>
[RequireComponent(typeof(Graphic))]
public class UnlockablesHighlightOverlay : HighlightOverlayBase
{
}
