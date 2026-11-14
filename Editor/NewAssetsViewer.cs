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
        AddedDate,
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
            Today,
            SinceYesterday,
            SinceLastWeek,
            CustomMinutes,
        }

        private const string AssetsPrefix = "Assets/";
        private const string PackagesPrefix = "Packages/";

        // OnGUIから毎フレーム呼ばれるためToArrayを避けてstatic readonlyで確保
        private static readonly string[] TimeRangeLabels =
        {
            "直近30分",
            "直近24時間",
            "今日",
            "昨日から",
            "先週から",
            "直近N分（指定）",
        };
        private static readonly string[] SortOrderLabels = { "追加日時", "名前" };
        private static readonly string[] SortDirectionLabels = { "昇順", "降順" };

        // ドメインリロードやウィンドウの開き直しで設定が消えないようにシリアライズする
        [SerializeField] private TimeRange selectedTimeRange = TimeRange.Last24Hours;
        [SerializeField] private int customMinutes = 60;
        [SerializeField] private SortOrder selectedSortOrder = SortOrder.AddedDate;
        [SerializeField] private SortDirection selectedSortDirection = SortDirection.Descending;
        [SerializeField] private bool includePackages;
        [SerializeField] private TreeViewState treeViewState;

        // アセットパス → エントリ。ディスクを読むのはRescanAssetsと差分更新のときだけ
        private readonly Dictionary<string, AssetEntry> scannedAssets = new Dictionary<string, AssetEntry>();

        private AssetTreeView treeView;
        private SearchField searchField;

        [MenuItem("くろ～は/NewAssetsViewer")]
        public static void ShowWindow()
        {
            GetWindow<NewAssetsViewer>("NewAssetsViewer");
        }

        private void OnEnable()
        {
            treeViewState ??= new TreeViewState();
            treeView = new AssetTreeView(treeViewState);

            searchField = new SearchField();
            searchField.downOrUpArrowKeyPressed += treeView.SetFocusAndEnsureSelectedItem;

            AssetChangeNotifier.AssetsChanged += OnAssetsChanged;

            RescanAssets();
            RefreshTreeView();
        }

        private void OnDisable()
        {
            AssetChangeNotifier.AssetsChanged -= OnAssetsChanged;
        }

        private void OnGUI()
        {
            DrawTimeRangeSelector();
            DrawSortOrderSelector();
            DrawToolbar();
            DrawSearchField();
            DrawAssetTreeView();
        }

        private void DrawTimeRangeSelector()
        {
            // Popupの値が変わったイベント内でコントロール数が変わるとレイアウトエラーになるため、
            // 追加フィールドを出すかどうかは変更前の値で判定する
            bool showCustomMinutes = selectedTimeRange == TimeRange.CustomMinutes;

            EditorGUI.BeginChangeCheck();

            selectedTimeRange = (TimeRange)EditorGUILayout.Popup("表示する範囲", (int)selectedTimeRange, TimeRangeLabels);

            if (showCustomMinutes)
            {
                using (new EditorGUI.IndentLevelScope())
                    customMinutes = Mathf.Max(1, EditorGUILayout.DelayedIntField("分数", customMinutes));
            }

            if (EditorGUI.EndChangeCheck())
                RefreshTreeView();
        }

        private void DrawSortOrderSelector()
        {
            EditorGUI.BeginChangeCheck();

            using (new EditorGUILayout.HorizontalScope())
            {
                selectedSortOrder = (SortOrder)EditorGUILayout.Popup("並べ替え", (int)selectedSortOrder, SortOrderLabels);
                selectedSortDirection = (SortDirection)EditorGUILayout.Popup(
                    (int)selectedSortDirection,
                    SortDirectionLabels,
                    GUILayout.Width(60)
                );
            }

            if (EditorGUI.EndChangeCheck())
                RefreshTreeView();
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUI.BeginChangeCheck();
                includePackages = EditorGUILayout.ToggleLeft("Packages も含める", includePackages);
                bool includePackagesChanged = EditorGUI.EndChangeCheck();

                bool refreshClicked = GUILayout.Button("更新！", GUILayout.Width(80));

                if (includePackagesChanged || refreshClicked)
                {
                    RescanAssets();
                    RefreshTreeView();
                }
            }

            EditorGUILayout.Space();
        }

        private void DrawSearchField()
        {
            treeView.searchString = searchField.OnGUI(EditorGUILayout.GetControlRect(), treeView.searchString);
            EditorGUILayout.Space();
        }

        private void DrawAssetTreeView()
        {
            // 残りの高さを全部使う（ヘッダーの高さを決め打ちしない）
            var rect = GUILayoutUtility.GetRect(
                GUIContent.none,
                GUIStyle.none,
                GUILayout.ExpandWidth(true),
                GUILayout.ExpandHeight(true)
            );
            treeView.OnGUI(rect);

            if (Event.current.type == EventType.Repaint && treeView.GetRows().Count == 0)
                GUI.Label(rect, "該当するアセットはありません", EditorStyles.centeredGreyMiniLabel);
        }

        // 全アセットを読み直す。重いので起動時・更新ボタン・Packagesトグル変更時のみ
        private void RescanAssets()
        {
            scannedAssets.Clear();
            foreach (var assetPath in AssetDatabase.GetAllAssetPaths())
                UpdateEntry(assetPath);
        }

        // 手元のキャッシュから期間で絞り込んで並べ替える。ディスクは読まない
        private void RefreshTreeView()
        {
            var referenceTime = GetReferenceTime();
            var assets = scannedAssets.Values.Where(e => e.AddedTime > referenceTime).ToList();
            assets.Sort(CreateComparison());

            treeView.SetAssets(assets);
            Repaint();
        }

        private Comparison<AssetEntry> CreateComparison()
        {
            Comparison<AssetEntry> primary = selectedSortOrder == SortOrder.Name
                ? (a, b) => EditorUtility.NaturalCompare(a.FileName, b.FileName)
                : (a, b) => a.AddedTime.CompareTo(b.AddedTime);
            int sign = selectedSortDirection == SortDirection.Descending ? -1 : 1;

            return (a, b) =>
            {
                int result = primary(a, b);
                // 同値のときに順序がぶれないようパスで決着をつける
                if (result == 0)
                    result = string.CompareOrdinal(a.AssetPath, b.AssetPath);
                return sign * result;
            };
        }

        private DateTime GetReferenceTime()
        {
            switch (selectedTimeRange)
            {
                case TimeRange.Last30Minutes:  return DateTime.Now.AddMinutes(-30);
                case TimeRange.Last24Hours:    return DateTime.Now.AddHours(-24);
                case TimeRange.Today:          return DateTime.Today;             // 今日0時
                case TimeRange.SinceYesterday: return DateTime.Today.AddDays(-1); // 昨日0時
                case TimeRange.SinceLastWeek:  return DateTime.Today.AddDays(-7); // 7日前0時
                case TimeRange.CustomMinutes:  return DateTime.Now.AddMinutes(-customMinutes);
                default:                       return DateTime.Now;
            }
        }

        private bool IsTargetPath(string assetPath)
        {
            return assetPath.StartsWith(AssetsPrefix, StringComparison.Ordinal)
                || (includePackages && assetPath.StartsWith(PackagesPrefix, StringComparison.Ordinal));
        }

        // 対象のファイルならキャッシュに登録・更新し、そうでなければキャッシュから外す
        private void UpdateEntry(string assetPath)
        {
            if (!IsTargetPath(assetPath))
            {
                scannedAssets.Remove(assetPath);
                return;
            }

            // Path.GetFullPathはUnityがPackages/をLibrary/PackageCache等の実体パスに解決してくれる
            var fullPath = Path.GetFullPath(assetPath);
            if (!File.Exists(fullPath))
            {
                scannedAssets.Remove(assetPath);
                return;
            }

            scannedAssets[assetPath] = new AssetEntry(assetPath, GetAddedTime(fullPath));
        }

        // .metaはUnityが取り込んだ瞬間に作られるので「プロジェクトに追加された日時」として使う。
        // 本体の作成日時だと、同じドライブ内から移動してきたファイルが古い日時のままになってしまう
        private static DateTime GetAddedTime(string fullPath)
        {
            var metaPath = fullPath + ".meta";
            return File.Exists(metaPath) ? File.GetCreationTime(metaPath) : File.GetCreationTime(fullPath);
        }

        private void OnAssetsChanged(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (var path in deleted)
                RemoveEntry(path);
            foreach (var path in movedFrom)
                RemoveEntry(path);

            bool folderMoved = false;
            foreach (var path in imported.Concat(moved))
            {
                if (AssetDatabase.IsValidFolder(path))
                {
                    // フォルダの移動は中身が通知に含まれない場合があるので全体を読み直す
                    if (Array.IndexOf(moved, path) >= 0)
                        folderMoved = true;
                    continue;
                }
                UpdateEntry(path);
            }

            if (folderMoved)
                RescanAssets();

            RefreshTreeView();
        }

        private void RemoveEntry(string assetPath)
        {
            if (scannedAssets.Remove(assetPath))
                return;

            // キャッシュに無い＝フォルダの可能性があるので、配下をまとめて消す
            var prefix = assetPath + "/";
            var children = scannedAssets.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList();
            foreach (var child in children)
                scannedAssets.Remove(child);
        }
    }

    // アセットの追加・削除・移動を開いているウィンドウに伝える
    internal sealed class AssetChangeNotifier : AssetPostprocessor
    {
        public static event Action<string[], string[], string[], string[]> AssetsChanged;

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths
        )
        {
            AssetsChanged?.Invoke(importedAssets, deletedAssets, movedAssets, movedFromAssetPaths);
        }
    }
}
