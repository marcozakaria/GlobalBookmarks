using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.Video;
using Object = UnityEngine.Object;

//Refrences
// https://docs.unity3d.com/2019.3/Documentation/ScriptReference/GlobalObjectId.html
namespace GlobalBookmarks
{
	public class GlobalBookmarks : EditorWindow
	{
		private static readonly string _creationPath = $"Assets/{nameof(GlobalBookmarks)}.asset";

		private GlobalBookmarksData _data;
		private Vector2 _scrollPosition;
		private string _searchText = string.Empty;
		private BookmarkSortMode _sortMode = BookmarkSortMode.NameAscending;

		private enum BookmarkSortMode
		{
			NameAscending,
			NameDescending,
			TypeThenName
		}

		List<Bookmark> Bookmarks
		{
			get
			{
				if (!_data) _data = FindAssetByType<GlobalBookmarksData>();

				if (!_data)
				{
					Debug.LogWarning($"Can't find config file for {typeof(GlobalBookmarksData)}, creating one!");
					var asset = CreateInstance<GlobalBookmarksData>();
					AssetDatabase.CreateAsset(asset, _creationPath);
					AssetDatabase.SaveAssets();
					_data = asset;
				}

				return _data.bookmarks;
			}
		}

		[MenuItem("Tools/Bookmarks")]
		private static void ShowWindow()
		{
			GetWindow<GlobalBookmarks>("Bookmarks");
		}

		private void OnGUI()
		{
			const float rowHeight = 20;
			const float spacing = 4;
			var addRect = new Rect(spacing, spacing, rowHeight, rowHeight);
			var sortWidth = 130f;
			var sortRect = new Rect(position.width - sortWidth - spacing, spacing, sortWidth, rowHeight);
			var searchRect = new Rect(addRect.xMax + spacing, spacing, sortRect.xMin - addRect.xMax - (spacing * 2), rowHeight);

			if (GUI.Button(addRect, new GUIContent(" + ", "Add selected asset as bookmark")))
			{
				var sel = Selection.activeObject;
				if (!sel) return;

				TryAddBookmark(sel, true);
			}

			_searchText = GUI.TextField(searchRect, _searchText ?? string.Empty);
			if(string.IsNullOrEmpty(_searchText))
			{
				GUI.Label(searchRect, new GUIContent("  Search...", "Search by name or type"), EditorStyles.miniLabel);
            }
			_sortMode = (BookmarkSortMode)EditorGUI.EnumPopup(sortRect, _sortMode);

			var filteredBookmarks = BuildFilteredBookmarks();

			var listRect = new Rect(
				spacing,
				rowHeight + (spacing * 2),
				position.width - (spacing * 2),
				position.height - rowHeight - (spacing * 3));

			HandleDragAndDrop(listRect);

			if (listRect.height <= 0f)
				return;

			var contentHeight = Mathf.Max(listRect.height, spacing + (filteredBookmarks.Count * (rowHeight + spacing)));
			var contentRect = new Rect(0, 0, listRect.width - 16f, contentHeight);
			_scrollPosition = GUI.BeginScrollView(listRect, _scrollPosition, contentRect);

			var totalHeight = spacing;

			if (filteredBookmarks.Count == 0)
			{
				var emptyText = string.IsNullOrWhiteSpace(_searchText)
					? "No bookmarks yet. Select an object and press +"
					: "No bookmarks match your search.";
				GUI.Label(new Rect(spacing, totalHeight, contentRect.width - (spacing * 2), rowHeight), emptyText, EditorStyles.centeredGreyMiniLabel);
			}

			foreach (var bookmark in filteredBookmarks)
			{
				var iconWidth = 18f;
				var staticWidth = 50f;
				var currentOffsetX = spacing;
				var removeButtonWidth = rowHeight;
				var openButtonWidth = bookmark.type == BookmarkType.Scene ? staticWidth + spacing : 0f;
				var buttonWidth = contentRect.width - (spacing * 2) - iconWidth - spacing - openButtonWidth - removeButtonWidth;
				buttonWidth = Mathf.Max(60f, buttonWidth);

				var bookmarkObject = bookmark.GetObject();
				var tooltip = GetBookmarkTooltip(bookmark, bookmarkObject);
				var iconTexture = GetBookmarkIcon(bookmarkObject, bookmark.type);
				var iconContent = new GUIContent(iconTexture) { tooltip = tooltip };
				GUI.Label(new Rect(currentOffsetX, totalHeight, iconWidth, rowHeight), iconContent);
				currentOffsetX += iconWidth + spacing;

				var nameContent = new GUIContent(bookmark.name, tooltip);
				if (GUI.Button(new Rect(currentOffsetX, totalHeight, buttonWidth, rowHeight), nameContent))
					bookmark.Select();

				currentOffsetX += buttonWidth + spacing;

				if (bookmark.type == BookmarkType.Scene)
				{

					if (GUI.Button(new Rect(currentOffsetX, totalHeight, staticWidth, rowHeight), "Open")
						&& EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
					{
						EditorSceneManager.OpenScene(
							AssetDatabase.GetAssetPath((bookmark.GetObject() as SceneAsset)));
					}

					currentOffsetX += staticWidth + spacing;
				}

				if (GUI.Button(new Rect(currentOffsetX, totalHeight, rowHeight, rowHeight), new GUIContent("-", "Remove this bookmark")))
				{
					if (EditorUtility.DisplayDialog(nameof(GlobalBookmarks),
						   "Are you sure?", "Yes", "Cancel"))
					{
						Bookmarks.Remove(bookmark);
						EditorUtility.SetDirty(_data);
						break;
					}
				}

				totalHeight += rowHeight + spacing;
			}

			GUI.EndScrollView();

			/*if (!string.IsNullOrEmpty(GUI.tooltip))
			{
				var hintRect = new Rect(spacing, position.height - rowHeight - spacing, position.width - (spacing * 2), rowHeight);
				GUI.Label(hintRect, GUI.tooltip, EditorStyles.helpBox);
			}*/
		}

		private bool TryAddBookmark(Object asset, bool showDuplicateNotification)
		{
			if (!asset)
				return false;

			var bookmarkToAdd = new Bookmark(asset);
			if (Bookmarks.Exists(bookmark => bookmark.globalObjectIdString == bookmarkToAdd.globalObjectIdString))
			{
				if (showDuplicateNotification)
					ShowNotification(new GUIContent("Bookmark already exists"));
				return false;
			}

			Bookmarks.Add(bookmarkToAdd);
			EditorUtility.SetDirty(_data);
			return true;
		}

		private void HandleDragAndDrop(Rect listRect)
		{
			var currentEvent = Event.current;
			if (!listRect.Contains(currentEvent.mousePosition))
				return;

			if (currentEvent.type != EventType.DragUpdated && currentEvent.type != EventType.DragPerform)
				return;

			DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

			if (currentEvent.type == EventType.DragPerform)
			{
				DragAndDrop.AcceptDrag();
				foreach (var draggedObject in DragAndDrop.objectReferences)
				{
					TryAddBookmark(draggedObject, false);
				}
			}

			currentEvent.Use();
		}

		private List<Bookmark> BuildFilteredBookmarks()
		{
			var filteredBookmarks = new List<Bookmark>();

			foreach (var bookmark in Bookmarks)
			{
				if (!string.IsNullOrWhiteSpace(_searchText)
					&& bookmark.name.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) < 0
					&& bookmark.type.ToString().IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) < 0)
				{
					continue;
				}

				filteredBookmarks.Add(bookmark);
			}

			filteredBookmarks.Sort(CompareBookmarks);
			return filteredBookmarks;
		}

		private int CompareBookmarks(Bookmark left, Bookmark right)
		{
			switch (_sortMode)
			{
				case BookmarkSortMode.NameDescending:
					return string.Compare(right.name, left.name, StringComparison.OrdinalIgnoreCase);
				case BookmarkSortMode.TypeThenName:
					var typeComparison = left.type.CompareTo(right.type);
					if (typeComparison != 0)
						return typeComparison;
					return string.Compare(left.name, right.name, StringComparison.OrdinalIgnoreCase);
				default:
					return string.Compare(left.name, right.name, StringComparison.OrdinalIgnoreCase);
			}
		}

		private static string GetBookmarkTooltip(Bookmark bookmark, Object bookmarkObject)
		{
			var path = bookmarkObject ? AssetDatabase.GetAssetPath(bookmarkObject) : string.Empty;
			var pathLine = string.IsNullOrWhiteSpace(path) ? "Path: (not available)" : $"Path: {path}";
			return $"{bookmark.name}\nType: {bookmark.type}\n{pathLine}";
		}

		private static Texture GetBookmarkIcon(Object bookmarkObject, BookmarkType bookmarkType)
		{
			if (bookmarkObject)
			{
				var objectContent = EditorGUIUtility.ObjectContent(bookmarkObject, bookmarkObject.GetType());
				if (objectContent?.image)
					return objectContent.image;
			}

			return bookmarkType switch
			{
				BookmarkType.Scene => EditorGUIUtility.IconContent("SceneAsset Icon").image,
				BookmarkType.Prefab => EditorGUIUtility.IconContent("Prefab Icon").image,
				BookmarkType.Material => EditorGUIUtility.IconContent("Material Icon").image,
				BookmarkType.Texture => EditorGUIUtility.IconContent("Texture Icon").image,
				BookmarkType.Folder => EditorGUIUtility.IconContent("Folder Icon").image,
				BookmarkType.Video => EditorGUIUtility.IconContent("VideoClip Icon").image,
				BookmarkType.Audio => EditorGUIUtility.IconContent("AudioClip Icon").image,
				BookmarkType.Script => EditorGUIUtility.IconContent("cs Script Icon").image,
				BookmarkType.ScriptableObject => EditorGUIUtility.IconContent("ScriptableObject Icon").image,
				_ => EditorGUIUtility.IconContent("DefaultAsset Icon").image
			};
		}

		private static T FindAssetByType<T>() where T : Object
		{
			var typeName = typeof(T).Name;
			var guids = AssetDatabase.FindAssets($"t:{typeName}");
			if (guids.Length == 0)
			{
				Debug.LogWarning($"Project contains no {typeName}");
				return null;
			}

			var path = AssetDatabase.GUIDToAssetPath(guids[0]);

			if (guids.Length > 1)
				Debug.LogWarning(
					$"Found more than one {typeName} in project. Choosing first one ({path}) as instance.");

			return AssetDatabase.LoadAssetAtPath<T>(path);
		}
	}

	[Serializable]
	public class Bookmark
	{
		public string name;
		public string globalObjectIdString;
		public BookmarkType type;

		public Bookmark(Object sel)
		{
			name = sel.name;
			globalObjectIdString = GlobalObjectId.GetGlobalObjectIdSlow(sel).ToString();
			type = ResolveBookmarkType(sel);
		}

		private static BookmarkType ResolveBookmarkType(Object sel)
		{
			if (sel is SceneAsset)
				return BookmarkType.Scene;

			if (sel is GameObject gameObject && PrefabUtility.IsPartOfPrefabAsset(gameObject))
				return BookmarkType.Prefab;

			if (sel is Material)
				return BookmarkType.Material;

			if (sel is Texture)
				return BookmarkType.Texture;

			if (sel is MonoScript)
				return BookmarkType.Script;

			if (sel is VideoClip)
				return BookmarkType.Video;

			if (sel is AudioClip)
				return BookmarkType.Audio;

			if (sel is ScriptableObject)
				return BookmarkType.ScriptableObject;

			var path = AssetDatabase.GetAssetPath(sel);
			if (!string.IsNullOrWhiteSpace(path) && AssetDatabase.IsValidFolder(path))
				return BookmarkType.Folder;

			return BookmarkType.Other;
		}

		public void Select()
		{
			/*if(!GlobalObjectId.TryParse(GlobalObjectIdString, out var globalObjectId))
			{
				Debug.LogError("Can't parse!");
				return;
			}*/

			var obj = GetObject();
			Assert.IsTrue(obj, "Can't find object!");
			Selection.activeObject = obj;
		}

		public Object GetObject()
		{

			if (!GlobalObjectId.TryParse(globalObjectIdString, out var globalObjectId))
			{
				Debug.LogError("Can't parse!");
				return null;
			}

			Object obj;
			switch (globalObjectId.identifierType)
			{
				case 1 or 3:
					{
						var path = AssetDatabase.GUIDToAssetPath(globalObjectId.assetGUID);
						obj = AssetDatabase.LoadAssetAtPath<Object>(path);
						break;
					}
				default:
					obj = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(globalObjectId);
					break;
			}

			return obj;
		}
	}

	public enum BookmarkType
	{
		Scene, Prefab, Material, Texture, Folder, Video, Audio, Script, ScriptableObject, Other
	}
}


