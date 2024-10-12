using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

public class ShowNewAssets : EditorWindow
{
    private enum TimeRange
    {
        Last30Minutes,
        Today,
        SinceYesterday,
        LastWeek,
    }

    public enum SortOrder
    {
        CreationTime,
        Name
    }

    private SortOrder selectedSortOrder = SortOrder.CreationTime;
    private TimeRange selectedTimeRange = TimeRange.Today;
    private AssetTreeView treeView;
    private SearchField searchField;

    [MenuItem("くろ～は/新規アセットを表示するヤツ")]
    public static void ShowWindow()
    {
        GetWindow<ShowNewAssets>("新規アセットを表示するヤツ");
    }

    private void OnEnable()
    {
        searchField = new SearchField();
        UpdateTreeView();
    }

    private void OnGUI()
    {
        DrawTimeRangeSelector();
        DrawSortOrderSelector();
        DrawSearchField();
        DrawAssetTreeView();
    }

    private void DrawSortOrderSelector()
    {
        selectedSortOrder = (SortOrder)EditorGUILayout.EnumPopup("並び順", selectedSortOrder);

        if (selectedSortOrder != treeView.SortOrder)
        {
            UpdateTreeView();
        }

        EditorGUILayout.Space();
    }

    private void DrawTimeRangeSelector()
    {
        EditorGUILayout.LabelField("表示する範囲を指定", EditorStyles.boldLabel);
        selectedTimeRange = (TimeRange)EditorGUILayout.EnumPopup("範囲", selectedTimeRange);

        if (GUILayout.Button("更新！"))
        {
            UpdateTreeView();
        }

        EditorGUILayout.Space();
    }

    private void DrawSearchField()
    {
        string searchString = searchField.OnGUI(
            EditorGUILayout.GetControlRect(GUILayout.ExpandWidth(true)),
            treeView.searchString
        );

        EditorGUILayout.Space();
        treeView.searchString = searchString;
    }

    private void DrawAssetTreeView()
    {
        treeView.OnGUI(
            GUILayoutUtility.GetRect(0, position.height - 80, GUILayout.ExpandHeight(true))
        );
    }

    private void UpdateTreeView()
    {
        DateTime referenceTime = GetReferenceTime();
        var newAssets = GetNewAssets(referenceTime);

        if (selectedSortOrder == SortOrder.CreationTime)
        {
            newAssets = newAssets.OrderBy(GetAssetCreationTime).ToList();
        }
        else if (selectedSortOrder == SortOrder.Name)
        {
            newAssets = newAssets.OrderBy(Path.GetFileName).ToList();
        }

        treeView = new AssetTreeView(newAssets);
        treeView.SortOrder = selectedSortOrder;
        Reload();
    }

    private DateTime GetAssetCreationTime(string assetPath)
    {
        if (System.IO.File.Exists(assetPath))
        {
            System.IO.FileInfo fileInfo = new System.IO.FileInfo(assetPath);
            return fileInfo.CreationTime;
        }
        return DateTime.MinValue;
    }

    private DateTime GetReferenceTime()
    {
        DateTime referenceTime = DateTime.Now;

        switch (selectedTimeRange)
        {
            case TimeRange.Last30Minutes:
                referenceTime = referenceTime.AddMinutes(-30);
                break;
            case TimeRange.Today:
                referenceTime = DateTime.Today;
                break;
            case TimeRange.SinceYesterday:
                referenceTime = referenceTime.AddDays(-1);
                break;
            case TimeRange.LastWeek:
                referenceTime = referenceTime.AddDays(-7);
                break;
        }

        return referenceTime;
    }

    private List<string> GetNewAssets(DateTime referenceTime)
    {
        string[] allAssets = AssetDatabase.GetAllAssetPaths();

        return allAssets
            .Where(assetPath => IsAssetNewerThan(assetPath, referenceTime))
            .ToList();
    }

    private bool IsAssetNewerThan(string assetPath, DateTime referenceTime)
    {
        if (System.IO.File.Exists(assetPath))
        {
            System.IO.FileInfo fileInfo = new System.IO.FileInfo(assetPath);
            DateTime creationTime = fileInfo.CreationTime;
            return creationTime > referenceTime;
        }
        return false;
    }

    private void Reload()
    {
        var expandedIDs = treeView.GetExpanded();
        treeView.Reload();
        treeView.SetExpanded(expandedIDs);
    }
}

public class AssetTreeView : TreeView
{
    private List<string> assets;

    public ShowNewAssets.SortOrder SortOrder { get; set; }

    public AssetTreeView(List<string> assets)
        : base(new TreeViewState())
    {
        this.assets = assets;
        Reload();
    }

    protected override TreeViewItem BuildRoot()
    {
        var root = new TreeViewItem
        {
            id = 0,
            depth = -1,
            displayName = "Root",
        };
        var allItems = new List<TreeViewItem>();
        allItems.AddRange(assets.Select((x, i) => new AssetTreeViewItem(i + 1, 0, x)));

        SetupParentsAndChildrenFromDepths(root, allItems);
        return root;
    }

    protected override void RowGUI(RowGUIArgs args)
    {
        var item = (AssetTreeViewItem)args.item;
        var assetPath = item.AssetPath;
        var iconRect = new Rect(
            args.rowRect.x + GetContentIndent(item),
            args.rowRect.y,
            args.rowRect.height,
            args.rowRect.height
        );

        DrawAssetIcon(iconRect, assetPath);
        DrawAssetLabel(args.rowRect, iconRect, assetPath);
        HandleAssetClick(args.rowRect, iconRect, assetPath);
    }

    private void DrawAssetIcon(Rect iconRect, string assetPath)
    {
        var icon = AssetDatabase.GetCachedIcon(assetPath);
        if (icon != null)
        {
            GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit);
        }
    }

    private void DrawAssetLabel(Rect rowRect, Rect iconRect, string assetPath)
    {
        var labelRect = new Rect(
            iconRect.xMax + 4,
            rowRect.y,
            rowRect.width - iconRect.width - 4,
            rowRect.height
        );
        GUI.Label(labelRect, System.IO.Path.GetFileName(assetPath));
    }

    private void HandleAssetClick(Rect rowRect, Rect iconRect, string assetPath)
    {
        var labelRect = new Rect(
            iconRect.xMax + 4,
            rowRect.y,
            rowRect.width - iconRect.width - 4,
            rowRect.height
        );

        if (
            Event.current.type == EventType.MouseDown
            && Event.current.button == 0
            && labelRect.Contains(Event.current.mousePosition)
        )
        {
            HighlightAssetInProjectWindow(assetPath);
        }
    }

    private void HighlightAssetInProjectWindow(string assetPath)
    {
        UnityEngine.Object obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
        Selection.activeObject = obj;
        EditorGUIUtility.PingObject(obj);
    }
}

public class AssetTreeViewItem : TreeViewItem
{
    public string AssetPath { get; set; }

    public AssetTreeViewItem(int id, int depth, string assetPath)
        : base(id, depth, System.IO.Path.GetFileName(assetPath))
    {
        AssetPath = assetPath;
    }
}