using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(LevelBlockSetup))]
public class LevelBlockSetupEditor : Editor
{
    private const int MaxTier = 5;
    private const int CellWidth = 60;
    private const int CellHeight = 42;
    private const int RowLabelWidth = 22;
    private Vector2 _scrollPosition;
    private BlockColorData _paintColorA;
    private BlockColorData _paintColorB;
    private int _paintTier = 1;
    private int _fillRow;
    private int _fillColumn;
    private int _rangeColStart;
    private int _rangeRowStart;
    private int _rangeColEnd;
    private int _rangeRowEnd;

    private SerializedProperty _isHardLevelProp;
    private SerializedProperty _widthProp;
    private SerializedProperty _heightProp;
    private SerializedProperty _cellsProp;
    private SerializedProperty _shooterCountProp;
    private SerializedProperty _shooterPlatformActiveCountProp;
    private SerializedProperty _shooterGridWidthProp;
    private SerializedProperty _shooterGridDepthProp;
    private SerializedProperty _shooterColorEntriesProp;

    private bool _showGridSize = true;
    private bool _showShooterSettings = true;
    private bool _showCells = true;
    private readonly Dictionary<BlockColorData, Color> _cellTintByColor = new();
    private string _cachedCountWarningMessage;
    private bool _cachedCountMeetsMin;

    private void OnEnable()
    {
        _isHardLevelProp = serializedObject.FindProperty("_isHardLevel");
        _widthProp = serializedObject.FindProperty("_width");
        _heightProp = serializedObject.FindProperty("_height");
        _cellsProp = serializedObject.FindProperty("_cells");
        _shooterCountProp = serializedObject.FindProperty("_shooterCount");
        _shooterPlatformActiveCountProp = serializedObject.FindProperty("_shooterPlatformActiveCount");
        _shooterGridWidthProp = serializedObject.FindProperty("_shooterGridWidth");
        _shooterGridDepthProp = serializedObject.FindProperty("_shooterGridDepth");
        _shooterColorEntriesProp = serializedObject.FindProperty("_shooterColorEntries");
        _cellTintByColor.Clear();
        _cachedCountWarningMessage = null;
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        EditorGUILayout.Space(2);
        EditorGUILayout.LabelField("Level Creator", EditorStyles.boldLabel);
        EditorGUILayout.Space(4);

        _showGridSize = EditorGUILayout.Foldout(_showGridSize, "Grid size", true);
        if (_showGridSize)
        {
            EditorGUILayout.PropertyField(_isHardLevelProp);
            EditorGUILayout.PropertyField(_widthProp);
            EditorGUILayout.PropertyField(_heightProp);
        }

        EditorGUILayout.Space(4);

        _showShooterSettings = EditorGUILayout.Foldout(_showShooterSettings, "Shooter (per level)", true);
        if (_showShooterSettings)
        {
            if (_shooterCountProp != null) EditorGUILayout.PropertyField(_shooterCountProp);
            if (_shooterPlatformActiveCountProp != null) EditorGUILayout.PropertyField(_shooterPlatformActiveCountProp);
            if (_shooterGridWidthProp != null) EditorGUILayout.PropertyField(_shooterGridWidthProp);
            if (_shooterGridDepthProp != null) EditorGUILayout.PropertyField(_shooterGridDepthProp);
            if (_shooterColorEntriesProp != null)
            {
                EditorGUILayout.PropertyField(_shooterColorEntriesProp, new GUIContent("Shooter Color Entries"), true);
            }
        }

        int width = Mathf.Max(1, _widthProp.intValue);
        int height = Mathf.Max(1, _heightProp.intValue);
        int expectedLength = width * height;

        EnsureCellsArraySize(expectedLength);

        EditorGUILayout.Space(4);

        _showCells = EditorGUILayout.Foldout(_showCells, "Cells (row 0 = front, Z=0)", true);
        if (_showCells)
        {
            DrawPresetButtons(width, height);
            EditorGUILayout.Space(4);

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition, GUILayout.MaxHeight(580f));
            {
                DrawColumnHeaderRow(width);
                for (int r = height - 1; r >= 0; r--)
                {
                    EditorGUILayout.BeginHorizontal();
                    {
                        DrawRowLabel($"R{r:00}");
                        for (int c = 0; c < width; c++)
                        {
                            int index = c + r * width;
                            DrawCell(index, c, r);
                        }
                        DrawRowLabel($"R{r:00}");
                    }
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.Space(8);
                DrawColumnHeaderRow(width);
            }
            EditorGUILayout.EndScrollView();
        }

        EditorGUILayout.Space(4);
        DrawBlockCountAndWarning();

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawRowLabel(string text)
    {
        EditorGUILayout.BeginVertical(GUILayout.Width(RowLabelWidth), GUILayout.Height(CellHeight));
        {
            GUILayout.FlexibleSpace();
            GUILayout.Label(text, EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
        }
        EditorGUILayout.EndVertical();
    }

    // Shooter color entries are now the single source of truth for per-level shooter configuration.

    private void DrawColumnHeaderRow(int width)
    {
        EditorGUILayout.BeginHorizontal();
        {
            GUILayout.Space(RowLabelWidth / 2f + CellWidth / 2f);
            for (int c = 0; c < width; c++)
            {
                EditorGUILayout.BeginVertical(GUILayout.Width(CellWidth));
                GUILayout.Label($"C{c:00}", EditorStyles.miniLabel);
                EditorGUILayout.EndVertical();
            }
            GUILayout.Space(RowLabelWidth);
        }
        EditorGUILayout.EndHorizontal();
    }

    private void EnsureCellsArraySize(int expectedLength)
    {
        if (_cellsProp.arraySize != expectedLength)
        {
            _cellsProp.arraySize = expectedLength;
            for (int i = 0; i < expectedLength; i++)
            {
                SerializedProperty cell = _cellsProp.GetArrayElementAtIndex(i);
                SerializedProperty tierProp = cell.FindPropertyRelative("_tier");
                if (tierProp != null && tierProp.intValue < 1)
                    tierProp.intValue = 1;
            }
        }
    }

    private void DrawCell(int index, int col, int row)
    {
        if (index >= _cellsProp.arraySize) return;

        SerializedProperty cellProp = _cellsProp.GetArrayElementAtIndex(index);
        SerializedProperty colorProp = cellProp.FindPropertyRelative("_color");
        SerializedProperty tierProp = cellProp.FindPropertyRelative("_tier");

        // Tint everything inside the cell based on the assigned BlockColorData (if any).
        Color previousColor = GUI.color;
        Color tint = Color.white;

        if (colorProp != null && colorProp.objectReferenceValue != null)
        {
            var colorAsset = colorProp.objectReferenceValue as BlockColorData;
            if (colorAsset != null)
            {
                tint = GetCellTint(colorAsset);
            }
        }

        GUI.color = tint;

        EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(CellWidth), GUILayout.MaxWidth(CellWidth), GUILayout.Height(CellHeight));
        {
            if (colorProp != null)
            {
                EditorGUILayout.PropertyField(colorProp, GUIContent.none, GUILayout.Height(18));
            }

            if (tierProp != null)
            {
                int tier = EditorGUILayout.IntField(Mathf.Clamp(tierProp.intValue, 1, MaxTier), GUILayout.Height(18));
                tierProp.intValue = Mathf.Clamp(tier, 1, MaxTier);
            }
        }
        EditorGUILayout.EndVertical();

        GUI.color = previousColor;
    }

    private Color GetCellTint(BlockColorData colorAsset)
    {
        if (colorAsset == null) return Color.white;

        if (_cellTintByColor.TryGetValue(colorAsset, out Color tint))
            return tint;

        // Use the asset's public color directly and cache the softened tint to avoid per-cell SerializedObject allocations.
        tint = Color.Lerp(Color.white, colorAsset.BlockColor, 0.35f);
        _cellTintByColor[colorAsset] = tint;
        return tint;
    }

    private void DrawPresetButtons(int width, int height)
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        EditorGUILayout.LabelField("Presets", EditorStyles.miniBoldLabel);
        _paintColorA = (BlockColorData)EditorGUILayout.ObjectField("Color A", _paintColorA, typeof(BlockColorData), false);
        _paintColorB = (BlockColorData)EditorGUILayout.ObjectField("Color B", _paintColorB, typeof(BlockColorData), false);
        _paintTier = EditorGUILayout.IntSlider("Tier", _paintTier, 1, MaxTier);

        EditorGUILayout.BeginHorizontal();
        {
            if (GUILayout.Button("Fill all (Color A)")) FillAll(_paintColorA);
            if (GUILayout.Button("Fill all (Color B)")) FillAll(_paintColorB);
            if (GUILayout.Button("Checkerboard (A/B)")) CheckerboardAB(width, height);
            if (GUILayout.Button("Clear all")) ClearAll();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Fill row / column", EditorStyles.miniBoldLabel);
        _fillRow = Mathf.Clamp(EditorGUILayout.IntField("Row", _fillRow), 0, height - 1);
        _fillColumn = Mathf.Clamp(EditorGUILayout.IntField("Column", _fillColumn), 0, width - 1);

        EditorGUILayout.BeginHorizontal();
        {
            if (GUILayout.Button("Fill row (A)")) FillRow(_fillRow, width, _paintColorA);
            if (GUILayout.Button("Fill row (B)")) FillRow(_fillRow, width, _paintColorB);
            if (GUILayout.Button("Clear row")) ClearRow(_fillRow, width);
            if (GUILayout.Button("Fill column (A)")) FillColumn(_fillColumn, width, height, _paintColorA);
            if (GUILayout.Button("Fill column (B)")) FillColumn(_fillColumn, width, height, _paintColorB);
            if (GUILayout.Button("Clear column")) ClearColumn(_fillColumn, width, height);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Single cell", EditorStyles.miniBoldLabel);
        EditorGUILayout.BeginHorizontal();
        {
            if (GUILayout.Button("Set cell (A)")) SetOneCell(_fillColumn, _fillRow, width, _paintColorA);
            if (GUILayout.Button("Set cell (B)")) SetOneCell(_fillColumn, _fillRow, width, _paintColorB);
            if (GUILayout.Button("Clear cell")) ClearOneCell(_fillColumn, _fillRow, width);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Apply to range", EditorStyles.miniBoldLabel);

        EditorGUILayout.BeginHorizontal();
        {
            _rangeColStart = Mathf.Clamp(EditorGUILayout.IntField("Col from", _rangeColStart), 0, width - 1);
            _rangeRowStart = Mathf.Clamp(EditorGUILayout.IntField("Row from", _rangeRowStart), 0, height - 1);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        {
            _rangeColEnd = Mathf.Clamp(EditorGUILayout.IntField("Col to", _rangeColEnd), 0, width - 1);
            _rangeRowEnd = Mathf.Clamp(EditorGUILayout.IntField("Row to", _rangeRowEnd), 0, height - 1);
        }

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();
        {
            if (GUILayout.Button("Apply range (A)")) ApplyToRange(width, height, _paintColorA);
            if (GUILayout.Button("Apply range (B)")) ApplyToRange(width, height, _paintColorB);
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();
    }

    private void FillAll(BlockColorData paintColor)
    {
        if (paintColor == null) Debug.LogWarning("Assign Color B first.");
        else
        {
            for (int i = 0; i < _cellsProp.arraySize; i++)
            {
                SerializedProperty cell = _cellsProp.GetArrayElementAtIndex(i);
                cell.FindPropertyRelative("_color").objectReferenceValue = paintColor;
                cell.FindPropertyRelative("_tier").intValue = _paintTier;
            }
        }
    }

    private void CheckerboardAB(int width, int height)
    {
        if (_paintColorA == null || _paintColorB == null) Debug.LogWarning("Assign Color A and Color B first.");
        else
        {
            for (int r = 0; r < height; r++)
                for (int c = 0; c < width; c++)
                {
                    int index = c + r * width;
                    SerializedProperty cell = _cellsProp.GetArrayElementAtIndex(index);
                    bool useA = (c + r) % 2 == 0;
                    cell.FindPropertyRelative("_color").objectReferenceValue = useA ? _paintColorA : _paintColorB;
                    cell.FindPropertyRelative("_tier").intValue = _paintTier;
                }
        }
    }

    private void ClearAll()
    {
        for (int i = 0; i < _cellsProp.arraySize; i++)
        {
            SerializedProperty cell = _cellsProp.GetArrayElementAtIndex(i);
            SerializedProperty colorProp = cell.FindPropertyRelative("_color");
            SerializedProperty tierProp = cell.FindPropertyRelative("_tier");
            if (colorProp != null) colorProp.objectReferenceValue = null;
            if (tierProp != null) tierProp.intValue = 1;
        }
    }

    private void SetOneCell(int col, int row, int width, BlockColorData color)
    {
        if (color == null) { Debug.LogWarning("Assign the color first."); return; }
        int index = col + row * width;
        if (index < 0 || index >= _cellsProp.arraySize) { Debug.LogWarning("Cell index out of range."); return; }
        SerializedProperty cell = _cellsProp.GetArrayElementAtIndex(index);
        cell.FindPropertyRelative("_color").objectReferenceValue = color;
        cell.FindPropertyRelative("_tier").intValue = _paintTier;
    }

    private void ClearOneCell(int col, int row, int width)
    {
        int index = col + row * width;
        if (index < 0 || index >= _cellsProp.arraySize) { Debug.LogWarning("Cell index out of range."); return; }
        SerializedProperty cell = _cellsProp.GetArrayElementAtIndex(index);
        cell.FindPropertyRelative("_color").objectReferenceValue = null;
        cell.FindPropertyRelative("_tier").intValue = 1;
    }

    private void FillRow(int row, int width, BlockColorData color)
    {
        if (color == null) { Debug.LogWarning("Assign the color first."); return; }
        for (int c = 0; c < width; c++)
        {
            int index = c + row * width;
            if (index >= _cellsProp.arraySize) return;
            SerializedProperty cell = _cellsProp.GetArrayElementAtIndex(index);
            cell.FindPropertyRelative("_color").objectReferenceValue = color;
            cell.FindPropertyRelative("_tier").intValue = _paintTier;
        }
    }

    private void FillColumn(int column, int width, int height, BlockColorData color)
    {
        if (color == null) { Debug.LogWarning("Assign the color first."); return; }
        for (int r = 0; r < height; r++)
        {
            int index = column + r * width;
            if (index >= _cellsProp.arraySize) return;
            SerializedProperty cell = _cellsProp.GetArrayElementAtIndex(index);
            cell.FindPropertyRelative("_color").objectReferenceValue = color;
            cell.FindPropertyRelative("_tier").intValue = _paintTier;
        }
    }

    private void ClearRow(int row, int width)
    {
        for (int c = 0; c < width; c++)
        {
            int index = c + row * width;
            if (index >= _cellsProp.arraySize) return;
            SerializedProperty cell = _cellsProp.GetArrayElementAtIndex(index);
            cell.FindPropertyRelative("_color").objectReferenceValue = null;
            cell.FindPropertyRelative("_tier").intValue = 1;
        }
    }

    private void ClearColumn(int column, int width, int height)
    {
        for (int r = 0; r < height; r++)
        {
            int index = column + r * width;
            if (index >= _cellsProp.arraySize) return;
            SerializedProperty cell = _cellsProp.GetArrayElementAtIndex(index);
            cell.FindPropertyRelative("_color").objectReferenceValue = null;
            cell.FindPropertyRelative("_tier").intValue = 1;
        }
    }

    private void ApplyToRange(int width, int height, BlockColorData color)
    {
        if (color == null) { Debug.LogWarning("Assign the color first."); return; }
        int cMin = Mathf.Min(_rangeColStart, _rangeColEnd);
        int cMax = Mathf.Max(_rangeColStart, _rangeColEnd);
        int rMin = Mathf.Min(_rangeRowStart, _rangeRowEnd);
        int rMax = Mathf.Max(_rangeRowStart, _rangeRowEnd);
        for (int r = rMin; r <= rMax; r++)
        {
            for (int c = cMin; c <= cMax; c++)
            {
                int index = c + r * width;
                if (index < 0 || index >= _cellsProp.arraySize) continue;
                SerializedProperty cell = _cellsProp.GetArrayElementAtIndex(index);
                cell.FindPropertyRelative("_color").objectReferenceValue = color;
                cell.FindPropertyRelative("_tier").intValue = _paintTier;
            }
        }
    }

    private void DrawBlockCountAndWarning()
    {
        // IMGUI calls this method multiple times per frame (Layout + Repaint). Rebuild only when needed.
        bool shouldRebuild = string.IsNullOrEmpty(_cachedCountWarningMessage) || Event.current.type == EventType.Repaint;
        if (shouldRebuild)
        {
            BuildCountWarningCache();
        }

        EditorGUILayout.HelpBox(_cachedCountWarningMessage, _cachedCountMeetsMin ? MessageType.Info : MessageType.Warning);
    }

    private void BuildCountWarningCache()
    {
        var setup = (LevelBlockSetup)target;
        int count = GetSerializedBlockCount();
        bool meetsMin = count >= LevelBlockSetup.MinBlocksPerLevel;

        var colorsUsed = setup.GetColorsUsed();
        var serializedColorCounts = GetSerializedColorCounts();
        string colorsText = colorsUsed == null || colorsUsed.Count == 0
            ? "None"
            : string.Join(", ", System.Linq.Enumerable.Select(colorsUsed, c => c != null ? $"{c.name} ({GetCountForColor(serializedColorCounts, c)})" : "(null)"));

        var shooterColors = setup.ShooterColors;
        int shooterCount = setup.ShooterCount;
        string shooterColorsText = shooterColors == null || shooterColors.Count == 0
            ? "None (will infer from block colors)"
            : string.Join(", ", System.Linq.Enumerable.Select(shooterColors, c => c != null ? c.name : "(null)"));
        string shooterColorTotalsText = "N/A";

        // Preview how many projectiles each shooter would get for explicit shooter colors (no randomness from null entries).
        string shooterBulletsText = "N/A";
        var shooterColorSequence = setup.ShooterColorSequence;
        var resolvedSequence = shooterColorSequence;
        if (shooterCount > 0 && resolvedSequence != null && resolvedSequence.Count > 0)
        {
            var shootersPerColor = new System.Collections.Generic.Dictionary<BlockColorData, int>();
            int nullColorShooterCount = 0;
            for (int i = 0; i < resolvedSequence.Count; i++)
            {
                BlockColorData color = resolvedSequence[i];
                if (color == null)
                {
                    nullColorShooterCount++;
                    continue;
                }

                if (!shootersPerColor.TryGetValue(color, out int countForColor))
                    shootersPerColor[color] = 1;
                else
                    shootersPerColor[color] = countForColor + 1;
            }

            var shooterColorTotals = new System.Collections.Generic.List<string>();
            foreach (var kvp in shootersPerColor)
            {
                shooterColorTotals.Add($"{kvp.Key.name} x{kvp.Value}");
            }
            if (nullColorShooterCount > 0)
            {
                shooterColorTotals.Add($"(null) x{nullColorShooterCount}");
            }
            shooterColorTotalsText = shooterColorTotals.Count > 0 ? string.Join(", ", shooterColorTotals) : "N/A";

            var shooterEntries = new System.Text.StringBuilder();
            for (int i = 0; i < resolvedSequence.Count; i++)
            {
                BlockColorData color = resolvedSequence[i];
                int blocksForColor = color != null ? GetCountForColor(serializedColorCounts, color) : 0;
                int shootersWithThisColor = (color != null && shootersPerColor.TryGetValue(color, out int shootersCount))
                    ? shootersCount
                    : 1;
                int projectilesPerShooter = shootersWithThisColor > 0
                    ? Mathf.Max(1, (blocksForColor + shootersWithThisColor - 1) / shootersWithThisColor)
                    : 1;

                shooterEntries.Append($"\n\tS{i}: {(color != null ? color.name : "(null)")}, {projectilesPerShooter}");
            }

            shooterBulletsText = shooterEntries.ToString();
        }

        string message = $"Block count: {count} (min {LevelBlockSetup.MinBlocksPerLevel}). {(meetsMin ? "OK." : "Add more blocks or increase tiers.")}\n\n" +
                         $"Colors used ({colorsUsed?.Count ?? 0}): {colorsText}\n\n" +
                         $"Shooters: {shooterCount},\nShooter colors ({shooterColors?.Count ?? 0}): {shooterColorsText}\nShooter totals per color: {shooterColorTotalsText}\n\n" +
                         $"Shooter bullets (index: color, bullets): {shooterBulletsText}";

        _cachedCountWarningMessage = message;
        _cachedCountMeetsMin = meetsMin;
    }

    private int GetSerializedBlockCount()
    {
        if (_cellsProp == null) return 0;

        int count = 0;
        for (int i = 0; i < _cellsProp.arraySize; i++)
        {
            SerializedProperty cell = _cellsProp.GetArrayElementAtIndex(i);
            SerializedProperty colorProp = cell.FindPropertyRelative("_color");
            if (colorProp == null || colorProp.objectReferenceValue == null) continue;

            SerializedProperty tierProp = cell.FindPropertyRelative("_tier");
            int tier = tierProp != null ? Mathf.Max(1, tierProp.intValue) : 1;
            count += tier;
        }

        return count;
    }

    private Dictionary<BlockColorData, int> GetSerializedColorCounts()
    {
        var result = new System.Collections.Generic.Dictionary<BlockColorData, int>();
        if (_cellsProp == null) return result;

        for (int i = 0; i < _cellsProp.arraySize; i++)
        {
            SerializedProperty cell = _cellsProp.GetArrayElementAtIndex(i);
            SerializedProperty colorProp = cell.FindPropertyRelative("_color");
            BlockColorData color = colorProp != null ? colorProp.objectReferenceValue as BlockColorData : null;
            if (color == null) continue;

            SerializedProperty tierProp = cell.FindPropertyRelative("_tier");
            int tier = tierProp != null ? Mathf.Max(1, tierProp.intValue) : 1;

            if (!result.TryGetValue(color, out int current))
                result[color] = tier;
            else
                result[color] = current + tier;
        }

        return result;
    }

    private static int GetCountForColor(System.Collections.Generic.Dictionary<BlockColorData, int> counts, BlockColorData color)
    {
        if (counts == null || color == null) return 0;
        return counts.TryGetValue(color, out int count) ? count : 0;
    }
}
