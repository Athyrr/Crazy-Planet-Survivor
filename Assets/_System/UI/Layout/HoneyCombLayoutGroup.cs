using UnityEngine;
using UnityEngine.UI;

public class HoneycombLayoutGroup : LayoutGroup
{
    public Vector2 CellSize = new Vector2(100, 100);
    public Vector2 Spacing = new Vector2(0, 0);

    [Tooltip("Max rows per column")]
    public int ColumnConstraintCount = 2;

    public override void CalculateLayoutInputHorizontal()
    {
        base.CalculateLayoutInputHorizontal();

        rectChildren.Clear();
        for (int i = 0; i < rectTransform.childCount; i++)
        {
            var rect = rectTransform.GetChild(i) as RectTransform;
            if (rect == null || !rect.gameObject.activeInHierarchy) continue;
            rectChildren.Add(rect);
        }

        int rows = 0;
        int columns = 0;

        if (rectChildren.Count > 0)
        {
            int constraint = ColumnConstraintCount > 0 ? ColumnConstraintCount : 1;
            rows = Mathf.Min(rectChildren.Count, constraint);
            columns = Mathf.CeilToInt(rectChildren.Count / (float)constraint);
        }

        float requiredWidth = (columns * CellSize.x) + (Mathf.Max(0, columns - 1) * Spacing.x);
        if (rows > 1) requiredWidth += (CellSize.x + Spacing.x) / 2f;


        float requiredHeight = (rows * CellSize.y) + (Mathf.Max(0, rows - 1) * Spacing.y);
        
        SetLayoutInputForAxis(padding.horizontal + requiredWidth, padding.horizontal + requiredWidth, -1, 0);
        SetLayoutInputForAxis(padding.vertical + requiredHeight, padding.vertical + requiredHeight, -1, 1);
    }

    public override void CalculateLayoutInputVertical()
    {
    }

    public override void SetLayoutHorizontal()
    {
        SetCells();
    }

    public override void SetLayoutVertical()
    {
        SetCells();
    }

    private void SetCells()
    {
        int constraint = ColumnConstraintCount > 0 ? ColumnConstraintCount : 1;
        int rows = Mathf.Min(rectChildren.Count, constraint);
        int columns = Mathf.CeilToInt(rectChildren.Count / (float)constraint);

        float contentWidth = (columns * CellSize.x) + (Mathf.Max(0, columns - 1) * Spacing.x);
        if (rows > 1) contentWidth += (CellSize.x + Spacing.x) / 2f;

        float contentHeight = (rows * CellSize.y) + (Mathf.Max(0, rows - 1) * Spacing.y);


        float startX = GetStartOffset(0, contentWidth);
        float startY = GetStartOffset(1, contentHeight);

        for (int i = 0; i < rectChildren.Count; i++)
        {
            int column = i / constraint;
            int row = i % constraint;

            float xPos = startX + (column * (CellSize.x + Spacing.x));
            float yPos = startY + (row * (CellSize.y + Spacing.y));


            if (row % 2 == 1)
            {
                xPos += (CellSize.x + Spacing.x) / 2f;
            }

            SetChildAlongAxis(rectChildren[i], 0, xPos, CellSize.x);
            SetChildAlongAxis(rectChildren[i], 1, yPos, CellSize.y);
        }
    }
}
