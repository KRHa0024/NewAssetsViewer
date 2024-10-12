using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

public class NewAssetsViewer : EditorWindow
{
    private enum TimeRange
    {
        直近30分,
        直近24時間,
        昨日から,
        先週から,
    }

    public enum SortOrder
    {
        作成日時,
        名前,
    }

    public enum SortDirection
    {
        昇順,
        降順,
    }

    private SortOrder selectedSortOrder = SortOrder.作成日時;
    private TimeRange selectedTimeRange = TimeRange.直近24時間;
    private SortDirection selectedSortDirection = SortDirection.降順;
    private AssetTreeView treeView;
    private SearchField searchField;

    [MenuItem("くろ～は/NewAssetsViewer")]
    public static void ShowWindow()
    {
        GetWindow<NewAssetsViewer>("NewAssetsViewer");
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
        EditorGUILayout.BeginHorizontal();

        SortOrder newSortOrder = (SortOrder)
            EditorGUILayout.EnumPopup("並べ替え", selectedSortOrder);
        SortDirection newSortDirection = (SortDirection)
            EditorGUILayout.EnumPopup(selectedSortDirection, GUILayout.Width(60));

        EditorGUILayout.EndHorizontal();

        if (newSortOrder != selectedSortOrder || newSortDirection != selectedSortDirection)
        {
            selectedSortOrder = newSortOrder;
            selectedSortDirection = newSortDirection;
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

        if (selectedSortOrder == SortOrder.作成日時)
        {
            newAssets =
                selectedSortDirection == SortDirection.昇順
                    ? newAssets.OrderBy(GetAssetCreationTime).ToList()
                    : newAssets.OrderByDescending(GetAssetCreationTime).ToList();
        }
        else if (selectedSortOrder == SortOrder.名前)
        {
            newAssets =
                selectedSortDirection == SortDirection.昇順
                    ? newAssets.OrderBy(Path.GetFileName).ToList()
                    : newAssets.OrderByDescending(Path.GetFileName).ToList();
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
            case TimeRange.直近30分:
                referenceTime = referenceTime.AddMinutes(-30);
                break;
            case TimeRange.直近24時間:
                referenceTime = referenceTime.AddHours(-24);
                break;
            case TimeRange.昨日から:
                referenceTime = referenceTime.AddDays(-1);
                break;
            case TimeRange.先週から:
                referenceTime = referenceTime.AddDays(-7);
                break;
        }

        return referenceTime;
    }

    private List<string> GetNewAssets(DateTime referenceTime)
    {
        string[] allAssets = AssetDatabase.GetAllAssetPaths();

        return allAssets.Where(assetPath => IsAssetNewerThan(assetPath, referenceTime)).ToList();
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

    public NewAssetsViewer.SortOrder SortOrder { get; set; }

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
