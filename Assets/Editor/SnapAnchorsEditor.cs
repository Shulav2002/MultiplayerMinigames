using UnityEngine;
using UnityEditor;

public class SnapAnchorsTool : EditorWindow
{
    [MenuItem("Tools/Snap Anchors to Edges")]
    private static void SnapAnchorsToEdges()
    {
        foreach (var obj in Selection.gameObjects)
        {
            RectTransform rectTransform = obj.GetComponent<RectTransform>();
            if (rectTransform != null)
            {
                Undo.RecordObject(rectTransform, "Snap Anchors"); // Enable Undo
                SnapAnchors(rectTransform);
            }
        }
    }

    private static void SnapAnchors(RectTransform rectTransform)
    {
        if (rectTransform.parent == null) return;

        RectTransform parentRect = rectTransform.parent as RectTransform;
        if (parentRect == null) return;

        // Convert local corners to world space
        Vector3[] worldCorners = new Vector3[4];
        rectTransform.GetWorldCorners(worldCorners);

        // Convert world corners to parent's local space
        Vector3[] parentCorners = new Vector3[4];
        parentRect.GetWorldCorners(parentCorners);

        float parentWidth = parentCorners[2].x - parentCorners[0].x;
        float parentHeight = parentCorners[2].y - parentCorners[0].y;

        Vector2 minAnchor = new Vector2(
            (worldCorners[0].x - parentCorners[0].x) / parentWidth,
            (worldCorners[0].y - parentCorners[0].y) / parentHeight
        );

        Vector2 maxAnchor = new Vector2(
            (worldCorners[2].x - parentCorners[0].x) / parentWidth,
            (worldCorners[2].y - parentCorners[0].y) / parentHeight
        );

        // Apply the new anchor values
        rectTransform.anchorMin = minAnchor;
        rectTransform.anchorMax = maxAnchor;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;

        Undo.FlushUndoRecordObjects(); // Ensure Undo is properly recorded
        EditorUtility.SetDirty(rectTransform); // Mark as changed
    }
}
