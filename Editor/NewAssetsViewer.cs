using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

/// <summary>
/// Unityエディター用のカスタムウィンドウ。新しいアセットをフィルタリングして表示します。
/// </summary>
public class NewAssetsViewer : EditorWindow
{
    // 表示するアセットの時間範囲
    private enum TimeRange
    {
        Last30Minutes,
        Last24Hours,
        SinceYesterday,
        SinceLastWeek,
    }

    // アセットの並べ替え基準
    public enum SortOrder
    {
        CreationDate,
        Name,
    }

    // 並べ替えの方向
    public enum SortDirection
    {
        Ascending,
        Descending,
    }

    // 現在選択されている並べ替え基準、時間範囲、並べ替え方向
    private SortOrder selectedSortOrder = SortOrder.CreationDate;
    private TimeRange selectedTimeRange = TimeRange.Last24Hours;
    private SortDirection selectedSortDirection = SortDirection.Descending;

    // ツリービューと検索フィールド
    private AssetTreeView treeView;
    private SearchField searchField;

    // 各UI要素のラベル
    private static readonly Dictionary<TimeRange, string> TimeRangeLabels = new Dictionary<TimeRange, string>
    {
        { TimeRange.Last30Minutes, "直近30分" },
        { TimeRange.Last24Hours, "直近24時間" },
        { TimeRange.SinceYesterday, "昨日から" },
        { TimeRange.SinceLastWeek, "先週から" },
    };

    private static readonly Dictionary<SortOrder, string> SortOrderLabels = new Dictionary<SortOrder, string>
    {
        { SortOrder.CreationDate, "作成日時" },
        { SortOrder.Name, "名前" },
    };

    private static readonly Dictionary<SortDirection, string> SortDirectionLabels = new Dictionary<SortDirection, string>
    {
        { SortDirection.Ascending, "昇順" },
        { SortDirection.Descending, "降順" },
    };

    /// <summary>
    /// メニューからウィンドウを開くためのエントリーポイント。
    /// </summary>
    [MenuItem("くろ～は/NewAssetsViewer")]
    public static void ShowWindow()
    {
        GetWindow<NewAssetsViewer>("NewAssetsViewer");
    }

    /// <summary>
    /// ウィンドウが有効化された際に呼び出される。
    /// </summary>
    private void OnEnable()
    {
        searchField = new SearchField();
        UpdateTreeView();
    }

    /// <summary>
    /// ウィンドウのGUIを描画する。
    /// </summary>
    private void OnGUI()
    {
        DrawTimeRangeSelector();
        DrawSortOrderSelector();
        DrawSearchField();
        DrawAssetTreeView();
    }

    /// <summary>
    /// 並べ替えオプションのUIを描画する。
    /// </summary>
    private void DrawSortOrderSelector()
    {
        EditorGUILayout.BeginHorizontal();

        // 並べ替え基準の選択
        var sortOrderOptions = SortOrderLabels.Values.ToArray();
        int selectedSortOrderIndex = Array.IndexOf(SortOrderLabels.Keys.ToArray(), selectedSortOrder);
        int newSortOrderIndex = EditorGUILayout.Popup("並べ替え", selectedSortOrderIndex, sortOrderOptions);
        SortOrder newSortOrder = SortOrderLabels.Keys.ElementAt(newSortOrderIndex);

        // 並べ替え方向の選択
        var sortDirectionOptions = SortDirectionLabels.Values.ToArray();
        int selectedSortDirectionIndex = Array.IndexOf(SortDirectionLabels.Keys.ToArray(), selectedSortDirection);
        int newSortDirectionIndex = EditorGUILayout.Popup(selectedSortDirectionIndex, sortDirectionOptions, GUILayout.Width(60));
        SortDirection newSortDirection = SortDirectionLabels.Keys.ElementAt(newSortDirectionIndex);

        EditorGUILayout.EndHorizontal();

        // 設定が変更された場合はツリービューを更新
        if (newSortOrder != selectedSortOrder || newSortDirection != selectedSortDirection)
        {
            selectedSortOrder = newSortOrder;
            selectedSortDirection = newSortDirection;
            UpdateTreeView();
        }

        EditorGUILayout.Space();
    }

    /// <summary>
    /// 時間範囲選択のUIを描画する。
    /// </summary>
    private void DrawTimeRangeSelector()
    {
        var timeRangeOptions = TimeRangeLabels.Values.ToArray();
        int selectedTimeRangeIndex = Array.IndexOf(TimeRangeLabels.Keys.ToArray(), selectedTimeRange);
        int newTimeRangeIndex = EditorGUILayout.Popup("表示する範囲", selectedTimeRangeIndex, timeRangeOptions);
        TimeRange newTimeRange = TimeRangeLabels.Keys.ElementAt(newTimeRangeIndex);

        // 設定が変更された場合はツリービューを更新
        if (newTimeRange != selectedTimeRange)
        {
            selectedTimeRange = newTimeRange;
            UpdateTreeView();
        }

        // 更新ボタン
        if (GUILayout.Button("更新！"))
        {
            UpdateTreeView();
        }

        EditorGUILayout.Space();
    }

    /// <summary>
    /// 検索フィールドのUIを描画する。
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
    /// アセットのツリービューを描画する。
    /// </summary>
    private void DrawAssetTreeView()
    {
        treeView.OnGUI(
            GUILayoutUtility.GetRect(0, position.height - 80, GUILayout.ExpandHeight(true))
        );
    }

    /// <summary>
    /// ツリービューを更新する。
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

        // ツリービューを再生成
        treeView = new AssetTreeView(newAssets);
        treeView.SortOrder = selectedSortOrder;
        Reload();
    }

    /// <summary>
    /// アセットの作成日時を取得する。
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
    /// 選択された時間範囲に基づいて基準日時を取得する。
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
    /// 指定した日時以降に作成されたアセットを取得する。
    /// </summary>
    private List<string> GetNewAssets(DateTime referenceTime)
    {
        string[] allAssets = AssetDatabase.GetAllAssetPaths();

        return allAssets.Where(assetPath => IsAssetNewerThan(assetPath, referenceTime)).ToList();
    }

    /// <summary>
    /// アセットが指定日時より新しいかどうかを判定する。
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
    /// ツリービューをリロードする。
    /// </summary>
    private void Reload()
    {
        var expandedIDs = treeView.GetExpanded();
        treeView.Reload();
        treeView.SetExpanded(expandedIDs);
    }
}
