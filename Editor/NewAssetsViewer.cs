using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

/// <summary>
/// 新しいアセットを表示するためのエディタウィンドウ
/// 指定した期間内に作成されたアセットを一覧表示し、並べ替えや検索が可能
/// </summary>
public class NewAssetsViewer : EditorWindow
{
    // 表示する期間の選択肢
    private enum TimeRange
    {
        Last30Minutes,
        Last24Hours,
        SinceYesterday,
        SinceLastWeek,
    }

    // 並べ替え基準
    public enum SortOrder
    {
        CreationDate,
        Name,
    }

    // 並べ替え方向
    public enum SortDirection
    {
        Ascending,
        Descending,
    }

    private SortOrder selectedSortOrder = SortOrder.CreationDate;
    private TimeRange selectedTimeRange = TimeRange.Last24Hours;
    private SortDirection selectedSortDirection = SortDirection.Descending;
    private AssetTreeView treeView; // アセットを表示するツリービュー
    private SearchField searchField; // 検索フィールド

    // 各種ラベルの定義
    private static readonly Dictionary<TimeRange, string> TimeRangeLabels = new Dictionary<
        TimeRange,
        string
    >
    {
        { TimeRange.Last30Minutes, "直近30分" },
        { TimeRange.Last24Hours, "直近24時間" },
        { TimeRange.SinceYesterday, "昨日から" },
        { TimeRange.SinceLastWeek, "先週から" },
    };

    private static readonly Dictionary<SortOrder, string> SortOrderLabels = new Dictionary<
        SortOrder,
        string
    >
    {
        { SortOrder.CreationDate, "作成日時" },
        { SortOrder.Name, "名前" },
    };

    private static readonly Dictionary<SortDirection, string> SortDirectionLabels = new Dictionary<
        SortDirection,
        string
    >
    {
        { SortDirection.Ascending, "昇順" },
        { SortDirection.Descending, "降順" },
    };

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

    /// <summary>
    /// GUIを描画
    /// </summary>
    private void OnGUI()
    {
        DrawTimeRangeSelector(); // 期間選択UI
        DrawSortOrderSelector(); // 並べ替えUI
        DrawSearchField(); // 検索フィールド
        DrawAssetTreeView(); // アセットツリービュー
    }

    /// <summary>
    /// 並べ替えオプションを描画
    /// </summary>
    private void DrawSortOrderSelector()
    {
        EditorGUILayout.BeginHorizontal();

        // 並べ替え基準の選択
        var sortOrderOptions = SortOrderLabels.Values.ToArray();
        int selectedSortOrderIndex = Array.IndexOf(
            SortOrderLabels.Keys.ToArray(),
            selectedSortOrder
        );
        int newSortOrderIndex = EditorGUILayout.Popup(
            "並べ替え",
            selectedSortOrderIndex,
            sortOrderOptions
        );
        SortOrder newSortOrder = SortOrderLabels.Keys.ElementAt(newSortOrderIndex);

        // 並べ替え方向の選択
        var sortDirectionOptions = SortDirectionLabels.Values.ToArray();
        int selectedSortDirectionIndex = Array.IndexOf(
            SortDirectionLabels.Keys.ToArray(),
            selectedSortDirection
        );
        int newSortDirectionIndex = EditorGUILayout.Popup(
            selectedSortDirectionIndex,
            sortDirectionOptions,
            GUILayout.Width(60)
        );
        SortDirection newSortDirection = SortDirectionLabels.Keys.ElementAt(newSortDirectionIndex);

        EditorGUILayout.EndHorizontal();

        // 並べ替え設定が変更された場合、ツリービューを更新
        if (newSortOrder != selectedSortOrder || newSortDirection != selectedSortDirection)
        {
            selectedSortOrder = newSortOrder;
            selectedSortDirection = newSortDirection;
            UpdateTreeView();
        }

        EditorGUILayout.Space();
    }

    /// <summary>
    /// 表示する期間の選択UIを描画
    /// </summary>
    private void DrawTimeRangeSelector()
    {
        var timeRangeOptions = TimeRangeLabels.Values.ToArray();
        int selectedTimeRangeIndex = Array.IndexOf(
            TimeRangeLabels.Keys.ToArray(),
            selectedTimeRange
        );
        int newTimeRangeIndex = EditorGUILayout.Popup(
            "表示する範囲",
            selectedTimeRangeIndex,
            timeRangeOptions
        );
        TimeRange newTimeRange = TimeRangeLabels.Keys.ElementAt(newTimeRangeIndex);

        // 期間が変更された場合、ツリービューを更新
        if (newTimeRange != selectedTimeRange)
        {
            selectedTimeRange = newTimeRange;
            UpdateTreeView();
        }

        // 手動更新ボタン
        if (GUILayout.Button("更新！"))
        {
            UpdateTreeView();
        }

        EditorGUILayout.Space();
    }

    /// <summary>
    /// 検索フィールドを描画
    /// </summary>
    private void DrawSearchField()
    {
        string searchString = searchField.OnGUI(
            EditorGUILayout.GetControlRect(GUILayout.ExpandWidth(true)),
            treeView.searchString
        );

        EditorGUILayout.Space();
        treeView.searchString = searchString;
    }

    /// <summary>
    /// アセットツリービューを描画
    /// </summary>
    private void DrawAssetTreeView()
    {
        treeView.OnGUI(
            GUILayoutUtility.GetRect(0, position.height - 80, GUILayout.ExpandHeight(true))
        );
    }

    /// <summary>
    /// ツリービューを更新
    /// </summary>
    private void UpdateTreeView()
    {
        DateTime referenceTime = GetReferenceTime();
        var newAssets = GetNewAssets(referenceTime);

        // 並べ替え処理
        if (selectedSortOrder == SortOrder.CreationDate)
        {
            newAssets =
                selectedSortDirection == SortDirection.Ascending
                    ? newAssets.OrderBy(GetAssetCreationTime).ToList()
                    : newAssets.OrderByDescending(GetAssetCreationTime).ToList();
        }
        else if (selectedSortOrder == SortOrder.Name)
        {
            newAssets =
                selectedSortDirection == SortDirection.Ascending
                    ? newAssets.OrderBy(Path.GetFileName).ToList()
                    : newAssets.OrderByDescending(Path.GetFileName).ToList();
        }

        // ツリービューのデータを更新
        treeView = new AssetTreeView(newAssets);
        treeView.SortOrder = selectedSortOrder;
        Reload();
    }

    /// <summary>
    /// 指定したアセットの作成日時を取得
    /// </summary>
    private DateTime GetAssetCreationTime(string assetPath)
    {
        if (System.IO.File.Exists(assetPath))
        {
            System.IO.FileInfo fileInfo = new System.IO.FileInfo(assetPath);
            return fileInfo.CreationTime;
        }
        return DateTime.MinValue;
    }

    /// <summary>
    /// 現在の期間設定に基づいて基準日時を取得
    /// </summary>
    private DateTime GetReferenceTime()
    {
        DateTime referenceTime = DateTime.Now;

        switch (selectedTimeRange)
        {
            case TimeRange.Last30Minutes:
                referenceTime = referenceTime.AddMinutes(-30);
                break;
            case TimeRange.Last24Hours:
                referenceTime = referenceTime.AddHours(-24);
                break;
            case TimeRange.SinceYesterday:
                referenceTime = referenceTime.AddDays(-1);
                break;
            case TimeRange.SinceLastWeek:
                referenceTime = referenceTime.AddDays(-7);
                break;
        }

        return referenceTime;
    }

    /// <summary>
    /// 指定した基準日時以降に作成されたアセットを取得
    /// </summary>
    private List<string> GetNewAssets(DateTime referenceTime)
    {
        string[] allAssets = AssetDatabase.GetAllAssetPaths();

        return allAssets.Where(assetPath => IsAssetNewerThan(assetPath, referenceTime)).ToList();
    }

    /// <summary>
    /// アセットが基準日時より新しいかどうかを判定
    /// </summary>
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

    /// <summary>
    /// ツリービューをリロード
    /// </summary>
    private void Reload()
    {
        var expandedIDs = treeView.GetExpanded();
        treeView.Reload();
        treeView.SetExpanded(expandedIDs);
    }
}

/// <summary>
/// アセットをツリービュー形式で表示するクラス
/// </summary>
public class AssetTreeView : TreeView
{
    private List<string> assets; // 表示するアセットのリスト

    public NewAssetsViewer.SortOrder SortOrder { get; set; }

    /// <summary>
    /// アセットリストを受け取り、初期化
    /// </summary>
    public AssetTreeView(List<string> assets)
        : base(new TreeViewState())
    {
        this.assets = assets;
        Reload();
    }

    /// <summary>
    /// ツリービューのルートアイテムを構築
    /// </summary>
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

    /// <summary>
    /// 各行のGUIを描画
    /// </summary>
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

    /// <summary>
    /// アセットのアイコンを描画
    /// </summary>
    private void DrawAssetIcon(Rect iconRect, string assetPath)
    {
        var icon = AssetDatabase.GetCachedIcon(assetPath);
        if (icon != null)
        {
            GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit);
        }
    }

    /// <summary>
    /// アセットのラベルを描画
    /// </summary>
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

    /// <summary>
    /// アセットがクリックされた際の処理
    /// </summary>
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

    /// <summary>
    /// プロジェクトウィンドウでアセットをハイライトする処理
    /// </summary>
    private void HighlightAssetInProjectWindow(string assetPath)
    {
        UnityEngine.Object obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
        Selection.activeObject = obj;
        EditorGUIUtility.PingObject(obj);
    }

    /// <summary>
    /// ドラッグ操作が可能かどうかを判定
    /// </summary>
    protected override bool CanStartDrag(CanStartDragArgs args)
    {
        return true;
    }

    /// <summary>
    /// ドラッグ＆ドロップのセットアップ
    /// </summary>
    protected override void SetupDragAndDrop(SetupDragAndDropArgs args)
    {
        var draggedItems = args
            .draggedItemIDs.Select(id => FindItem(id, rootItem) as AssetTreeViewItem)
            .Where(item => item != null)
            .ToList();

        if (draggedItems.Count > 0)
        {
            DragAndDrop.PrepareStartDrag();

            DragAndDrop.objectReferences = draggedItems
                .Select(item => AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(item.AssetPath))
                .ToArray();

            DragAndDrop.paths = draggedItems.Select(item => item.AssetPath).ToArray();

            DragAndDrop.StartDrag("Dragging Assets");
        }
    }
}

/// <summary>
/// アセットを表すツリービューアイテム
/// </summary>
public class AssetTreeViewItem : TreeViewItem
{
    public string AssetPath { get; set; } // アセットのパス

    /// <summary>
    /// アセットの情報を初期化
    /// </summary>
    public AssetTreeViewItem(int id, int depth, string assetPath)
        : base(id, depth, System.IO.Path.GetFileName(assetPath))
    {
        AssetPath = assetPath;
    }
}
