using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace KRHa.NewAssetsViewer
{
    public class AssetTreeViewItem : TreeViewItem
    {
        public string AssetPath { get; set; }

        public AssetTreeViewItem(int id, int depth, string assetPath)
            : base(id, depth, Path.GetFileName(assetPath))
        {
            AssetPath = assetPath;
        }
    }

    public class AssetTreeView : TreeView
    {
        private List<string> assets;

        public SortOrder SortOrder { get; set; }

        public AssetTreeView(List<string> assets)
            : base(new TreeViewState())
        {
            this.assets = assets;
            Reload();
        }

        public void SetAssets(List<string> newAssets)
        {
            assets = newAssets;
            Reload();
        }

        protected override TreeViewItem BuildRoot()
        {
            var root = new TreeViewItem { id = 0, depth = -1, displayName = "Root" };
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
            var labelRect = new Rect(
                iconRect.xMax + 4,
                args.rowRect.y,
                args.rowRect.width - iconRect.width - 4,
                args.rowRect.height
            );

            DrawAssetIcon(iconRect, assetPath);
            DrawAssetLabel(labelRect, assetPath);
            HandleAssetClick(labelRect, assetPath);
        }

        private void DrawAssetIcon(Rect iconRect, string assetPath)
        {
            var icon = AssetDatabase.GetCachedIcon(assetPath);
            if (icon != null)
                GUI.DrawTexture(iconRect, icon, ScaleMode.ScaleToFit);
        }

        private void DrawAssetLabel(Rect labelRect, string assetPath)
        {
            GUI.Label(labelRect, Path.GetFileName(assetPath));
        }

        private void HandleAssetClick(Rect labelRect, string assetPath)
        {
            if (
                Event.current.type == EventType.MouseDown
                && Event.current.button == 0
                && labelRect.Contains(Event.current.mousePosition)
            )
            {
                var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
                Selection.activeObject = obj;
                EditorGUIUtility.PingObject(obj);
            }
        }

        protected override bool CanStartDrag(CanStartDragArgs args) => true;

        protected override void SetupDragAndDrop(SetupDragAndDropArgs args)
        {
            var draggedItems = args
                .draggedItemIDs.Select(id => FindItem(id, rootItem) as AssetTreeViewItem)
                .Where(item => item != null)
                .ToList();

            if (draggedItems.Count == 0)
                return;

            DragAndDrop.PrepareStartDrag();

            DragAndDrop.objectReferences = draggedItems
                .Select(item => AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(item.AssetPath))
                .Where(obj => obj != null)
                .ToArray();

            DragAndDrop.paths = draggedItems.Select(item => item.AssetPath).ToArray();
            DragAndDrop.StartDrag("Dragging Assets");
        }
    }
}
