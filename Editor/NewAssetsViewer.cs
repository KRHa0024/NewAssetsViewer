using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace KRHa.NewAssetsViewer
{
    public enum SortOrder
    {
        CreationDate,
        Name,
    }

    public enum SortDirection
    {
        Ascending,
        Descending,
    }

    public class NewAssetsViewer : EditorWindow
    {
        private enum TimeRange
        {
            Last30Minutes,
            Last24Hours,
            SinceYesterday,
            SinceLastWeek,
        }

        private const float HeaderHeight = 80f;

        // OnGUIから毎フレーム呼ばれるためToArrayを避けてstatic readonlyで確保
        private static readonly string[] TimeRangeLabels = { "直近30分", "直近24時間", "昨日から", "先週から" };
        private static readonly string[] SortOrderLabels = { "作成日時", "名前" };
        private static readonly string[] SortDirectionLabels = { "昇順", "降順" };

        private SortOrder selectedSortOrder = SortOrder.CreationDate;
        private TimeRange selectedTimeRange = TimeRange.Last24Hours;
        private SortDirection selectedSortDirection = SortDirection.Descending;
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

            int newSortOrderIndex = EditorGUILayout.Popup("並べ替え", (int)selectedSortOrder, SortOrderLabels);
            int newSortDirectionIndex = EditorGUILayout.Popup(
                (int)selectedSortDirection,
                SortDirectionLabels,
                GUILayout.Width(60)
            );

            EditorGUILayout.EndHorizontal();

            var newSortOrder = (SortOrder)newSortOrderIndex;
            var newSortDirection = (SortDirection)newSortDirectionIndex;

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
            int newTimeRangeIndex = EditorGUILayout.Popup("表示する範囲", (int)selectedTimeRange, TimeRangeLabels);
            var newTimeRange = (TimeRange)newTimeRangeIndex;

            if (newTimeRange != selectedTimeRange)
            {
                selectedTimeRange = newTimeRange;
                UpdateTreeView();
            }

            if (GUILayout.Button("更新！"))
                UpdateTreeView();

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
                GUILayoutUtility.GetRect(0, position.height - HeaderHeight, GUILayout.ExpandHeight(true))
            );
        }

        private void UpdateTreeView()
        {
            var creationTimeCache = new Dictionary<string, DateTime>();
            var newAssets = GetNewAssets(GetReferenceTime(), creationTimeCache);

            if (selectedSortOrder == SortOrder.CreationDate)
            {
                newAssets = selectedSortDirection == SortDirection.Ascending
                    ? newAssets.OrderBy(p => creationTimeCache[p]).ToList()
                    : newAssets.OrderByDescending(p => creationTimeCache[p]).ToList();
            }
            else if (selectedSortOrder == SortOrder.Name)
            {
                newAssets = selectedSortDirection == SortDirection.Ascending
                    ? newAssets.OrderBy(Path.GetFileName).ToList()
                    : newAssets.OrderByDescending(Path.GetFileName).ToList();
            }

            if (treeView == null)
            {
                treeView = new AssetTreeView(newAssets);
                treeView.SortOrder = selectedSortOrder;
            }
            else
            {
                var expandedIDs = treeView.GetExpanded();
                treeView.SortOrder = selectedSortOrder;
                treeView.SetAssets(newAssets);
                treeView.SetExpanded(expandedIDs);
            }
        }

        private DateTime GetReferenceTime()
        {
            switch (selectedTimeRange)
            {
                case TimeRange.Last30Minutes: return DateTime.Now.AddMinutes(-30);
                case TimeRange.Last24Hours:   return DateTime.Now.AddHours(-24);
                case TimeRange.SinceYesterday: return DateTime.Today.AddDays(-1); // 昨日0時
                case TimeRange.SinceLastWeek:  return DateTime.Today.AddDays(-7); // 7日前0時
                default:                      return DateTime.Now;
            }
        }

        // referenceTime以降のアセットを返しつつ、creationTimeCacheにCreationTimeを積む
        private List<string> GetNewAssets(DateTime referenceTime, Dictionary<string, DateTime> creationTimeCache)
        {
            var result = new List<string>();

            foreach (var assetPath in AssetDatabase.GetAllAssetPaths())
            {
                if (!File.Exists(assetPath))
                    continue;

                var creationTime = new FileInfo(assetPath).CreationTime;
                creationTimeCache[assetPath] = creationTime;

                if (creationTime > referenceTime)
                    result.Add(assetPath);
            }

            return result;
        }
    }
}
