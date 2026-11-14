using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace KRHa.NewAssetsViewer
{
    public sealed class AssetEntry
    {
        private int? stableId;

        public string AssetPath { get; }
        public string FileName { get; }
        public string FolderPath { get; }
        public DateTime AddedTime { get; }

        // 並び順が変わっても同じアセットは同じIDになるよう、GUIDから求める
        public int StableId => stableId ??= ComputeStableId(AssetPath);

        public AssetEntry(string assetPath, DateTime addedTime)
        {
            AssetPath = assetPath;
            FileName = Path.GetFileName(assetPath);
            FolderPath = Path.GetDirectoryName(assetPath)?.Replace('\\', '/') ?? string.Empty;
            AddedTime = addedTime;
        }

        // string.GetHashCodeは実行環境によって変わり得るので、FNV-1aで決定的に計算する
        // （TreeViewStateはシリアライズされるため、ドメインリロード後も同じIDである必要がある）
        private static int ComputeStableId(string assetPath)
        {
            var key = AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrEmpty(key))
                key = assetPath;

            unchecked
            {
                uint hash = 2166136261;
                foreach (char c in key)
                {
                    hash ^= c;
                    hash *= 16777619;
                }
                return (int)hash;
            }
        }
    }

    public class AssetTreeViewItem : TreeViewItem
    {
        public AssetEntry Entry { get; }

        public AssetTreeViewItem(int id, AssetEntry entry)
            : base(id, 0, entry.FileName)
        {
            Entry = entry;
        }
    }

    public class AssetTreeView : TreeView
    {
        private const float IconLabelSpacing = 4f;
        private const float NameFolderSpacing = 6f;

        private static GUIStyle folderLabelStyle;

        private IReadOnlyList<AssetEntry> assets = Array.Empty<AssetEntry>();
        private readonly Dictionary<int, AssetEntry> entriesById = new Dictionary<int, AssetEntry>();

        private static GUIStyle FolderLabelStyle =>
            folderLabelStyle ??= new GUIStyle(GUI.skin.label)
            {
                normal = { textColor = new Color(0.5f, 0.5f, 0.5f) },
            };

        public AssetTreeView(TreeViewState state)
            : base(state)
        {
            showAlternatingRowBackgrounds = true;
            Reload();
        }

        public void SetAssets(IReadOnlyList<AssetEntry> newAssets)
        {
            assets = newAssets;
            Reload();
        }

        protected override TreeViewItem BuildRoot()
        {
            entriesById.Clear();

            var root = new TreeViewItem { id = 0, depth = -1, displayName = "Root" };
            var allItems = new List<TreeViewItem>(assets.Count);

            foreach (var entry in assets)
            {
                int id = entry.StableId;
                // root(0)との衝突やハッシュ衝突をずらして回避する（実際にはほぼ起きない）
                while (id == 0 || entriesById.ContainsKey(id))
                    id++;

                entriesById[id] = entry;
                allItems.Add(new AssetTreeViewItem(id, entry));
            }

            SetupParentsAndChildrenFromDepths(root, allItems);
            return root;
        }

        protected override void RowGUI(RowGUIArgs args)
        {
            var entry = ((AssetTreeViewItem)args.item).Entry;
            var rowRect = args.rowRect;

            var iconRect = new Rect(rowRect.x + GetContentIndent(args.item), rowRect.y, rowRect.height, rowRect.height);
            var icon = AssetDatabase.GetCachedIcon(entry.AssetPath);
            if (icon != null)
                GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit);

            // 行の右端からはみ出さないよう、インデント込みで残り幅を計算する
            float nameX = iconRect.xMax + IconLabelSpacing;
            var nameContent = new GUIContent(entry.FileName, entry.AssetPath);
            float nameWidth = Mathf.Min(GUI.skin.label.CalcSize(nameContent).x, Mathf.Max(0f, rowRect.xMax - nameX));
            GUI.Label(new Rect(nameX, rowRect.y, nameWidth, rowRect.height), nameContent);

            // 同名ファイルを見分けられるよう、名前の後ろにフォルダパスを薄く表示する
            float folderX = nameX + nameWidth + NameFolderSpacing;
            float folderWidth = rowRect.xMax - folderX;
            if (folderWidth > 0f)
            {
                GUI.Label(
                    new Rect(folderX, rowRect.y, folderWidth, rowRect.height),
                    new GUIContent(entry.FolderPath, entry.AssetPath),
                    FolderLabelStyle
                );
            }
        }

        // キーボード操作・複数選択も含めて、TreeViewの選択をUnityの選択に反映する
        protected override void SelectionChanged(IList<int> selectedIds)
        {
            var objects = selectedIds.Select(LoadAsset).Where(obj => obj != null).ToArray();
            Selection.objects = objects;

            var pingTarget = LoadAsset(state.lastClickedID) ?? objects.LastOrDefault();
            if (pingTarget != null)
                EditorGUIUtility.PingObject(pingTarget);
        }

        // 選択済みの行をもう一度クリックしたときもハイライトし直す
        protected override void SingleClickedItem(int id)
        {
            var obj = LoadAsset(id);
            if (obj != null)
                EditorGUIUtility.PingObject(obj);
        }

        protected override void DoubleClickedItem(int id)
        {
            var obj = LoadAsset(id);
            if (obj != null)
                AssetDatabase.OpenAsset(obj);
        }

        protected override void ContextClickedItem(int id)
        {
            if (!entriesById.TryGetValue(id, out var clickedEntry))
                return;

            // 選択中の行を右クリックしたときは選択中のもの全部、そうでなければその行だけを対象にする
            var targetIds = IsSelected(id) ? GetSelection() : new List<int> { id };
            var targetPaths = targetIds
                .Where(entriesById.ContainsKey)
                .Select(targetId => entriesById[targetId].AssetPath)
                .ToList();

            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("開く"), false, () => DoubleClickedItem(id));
            menu.AddItem(
                new GUIContent("エクスプローラーで表示"),
                false,
                () => EditorUtility.RevealInFinder(Path.GetFullPath(clickedEntry.AssetPath))
            );
            menu.AddItem(
                new GUIContent(targetPaths.Count > 1 ? $"パスをコピー（{targetPaths.Count}件）" : "パスをコピー"),
                false,
                () => EditorGUIUtility.systemCopyBuffer = string.Join("\n", targetPaths)
            );
            menu.ShowAsContext();
        }

        protected override bool CanStartDrag(CanStartDragArgs args) => true;

        protected override void SetupDragAndDrop(SetupDragAndDropArgs args)
        {
            var draggedEntries = args
                .draggedItemIDs.Where(entriesById.ContainsKey)
                .Select(id => entriesById[id])
                .ToList();

            if (draggedEntries.Count == 0)
                return;

            DragAndDrop.PrepareStartDrag();

            DragAndDrop.objectReferences = draggedEntries
                .Select(entry => AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(entry.AssetPath))
                .Where(obj => obj != null)
                .ToArray();

            DragAndDrop.paths = draggedEntries.Select(entry => entry.AssetPath).ToArray();
            DragAndDrop.StartDrag(draggedEntries.Count == 1 ? draggedEntries[0].FileName : "Dragging Assets");
        }

        private UnityEngine.Object LoadAsset(int id)
        {
            return entriesById.TryGetValue(id, out var entry)
                ? AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(entry.AssetPath)
                : null;
        }
    }
}
