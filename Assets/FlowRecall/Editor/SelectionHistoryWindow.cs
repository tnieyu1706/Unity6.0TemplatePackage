// FlowRecall Selection History
// Version: 1.3 (Added Category Rename/Delete)
// by: HyperLumin
// Date: [2025-05-25]

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FlowRecall
{
    public class SelectionHistoryWindow : EditorWindow
    {
        #region Nested Types

        private enum Size
        {
            Small,
            Medium
        }

        [Serializable]
        private class HistoryItem : IComparable<HistoryItem>
        {
            public Object Target;
            public bool IsPinned;
            public string Category; // Added Category field
            public string AssetGuid;
            public int InstanceID;
            public string CachedPath;
            public DateTime Timestamp;

            public HistoryItem(Object target)
            {
                Target = target;
                IsPinned = false;
                Category = "Default";
                AssetGuid = GetAssetGuid(target);
                InstanceID = target != null ? target.GetInstanceID() : 0;
                CachedPath = GetObjectPath(target);
                Timestamp = DateTime.UtcNow;
            }

            public HistoryItem(string guid, bool pinned)
            {
                Target = LoadAssetFromGuid(guid);
                IsPinned = pinned;
                Category = "Default";
                AssetGuid = guid;
                InstanceID = Target != null ? Target.GetInstanceID() : 0;
                CachedPath = GetObjectPath(Target);
                Timestamp = DateTime.UtcNow;
            }

            public bool IsValid()
            {
                if (Target == null) return false;
                if (IsSceneObject())
                {
                    if (Target is Component c && c == null)
                    {
                        Target = null;
                        return false;
                    }

                    if (Target is GameObject g && g == null)
                    {
                        Target = null;
                        return false;
                    }
                }

                if (!IsSceneObject() && !string.IsNullOrEmpty(AssetGuid))
                {
                    var path = AssetDatabase.GUIDToAssetPath(AssetGuid);
                    if (string.IsNullOrEmpty(path))
                    {
                        Target = null;
                        return false;
                    }

                    if (Target is DefaultAsset && AssetDatabase.IsValidFolder(path)) return true;
                    if (Target == null) return false;
                }

                return true;
            }

            public bool IsSceneObject()
            {
                return string.IsNullOrEmpty(AssetGuid);
            }

            public string GetDisplayName()
            {
                if (!IsValid()) return "[Deleted Object]";
                var n = Target.name;
                if (string.IsNullOrEmpty(n)) n = "[Unnamed Object]";
                var t = Target.GetType()?.Name ?? "Unknown";
                if (Target is DefaultAsset && AssetDatabase.IsValidFolder(CachedPath)) t = "Folder";
                return $"{n} ({t})";
            }

            public int CompareTo(HistoryItem other)
            {
                if (other == null) return 1;
                if (IsPinned && !other.IsPinned) return -1;
                if (!IsPinned && other.IsPinned) return 1;
                if (IsPinned && other.IsPinned)
                    return string.Compare(GetDisplayName(), other.GetDisplayName(), StringComparison.OrdinalIgnoreCase);
                return other.Timestamp.CompareTo(Timestamp);
            }
        }

        #endregion

        #region Constants

        private const string HistorySizePrefKey = "FlowRecal.SelectionHistory.MaxHistorySize";
        private const string ShowScenePrefKey = "FlowRecal.SelectionHistory.ShowScene";
        private const string ShowProjectPrefKey = "FlowRecal.SelectionHistory.ShowProject";
        private const string MoveSelectedToTopPrefKey = "FlowRecal.SelectionHistory.MoveSelectedTop";
        private const string SizePrefKey = "FlowRecal.SelectionHistory.Size";
        private const string ShowIndicatorIconsPrefKey = "FlowRecal.SelectionHistory.ShowIndicatorIcons";
        private const string ShowTypeNamePrefKey = "FlowRecal.SelectionHistory.ShowTypeName";
        private const string PinnedDataPrefKey = "FlowRecal.SelectionHistory.Pinned_GUIDs";
        private const string HistoryDataPrefKey = "FlowRecal.SelectionHistory.Data_GUIDs";
        private const string CategoriesPrefKey = "FlowRecal.SelectionHistory.Categories";
        private const string ActiveCategoryPrefKey = "FlowRecal.SelectionHistory.ActiveCategory";

        private const float SmallRowHeight = 22f;
        private const float MediumRowHeight = 38f;
        private const float SmallIndicatorIconSize = 16f;
        private const float MediumIndicatorIconSize = 24f;
        private const float SmallThumbnailSize = 16f;
        private const float MediumThumbnailSize = 32f;
        private const float SmallPinButtonSize = 18f;
        private const float MediumPinButtonSize = 24f;
        private const int SmallPinFontSize = 10;
        private const int MediumPinFontSize = 14;
        private const float IconPadding = 4f;
        private const float SmallReorderHandleIconSize = 16f;
        private const float MediumReorderHandleIconSize = 20f;
        private const float ReorderDragThreshold = 5f;


        private const int ItemsPerPage = 50;

        #endregion

        #region Fields & State

        private readonly List<HistoryItem> _selectionHistory = new();
        private int _maxHistorySize = 30;

        private Vector2 _scrollPosition;
        private string _searchQuery = "";
        private int _currentTab;
        private readonly string[] _tabLabels = { "History", "Favorites", "All" };
        private int _currentPage;
        private bool _isDragging;
        private HistoryItem _draggedItem;
        private Vector2 _dragStartPosition;
        private bool _ignoreNextSelectionChange;
        private HistoryItem _pendingClickItem;

        private bool _showSettings;
        private bool _showSceneObjects = true;
        private bool _showProjectAssets = true;
        private bool _moveSelectedToTopOnClick = true;
        private Size _size = Size.Small;
        private bool _showIndicatorIcons = true;
        private bool _showTypeName = true;

        // Categories
        private List<string> _categories = new List<string> { "Default" };
        private int _selectedCategoryFilterIndex = 0; // 0 = All Categories
        private bool _isCreatingCategory = false;
        private string _newCategoryName = "";

        // Rename Categories Variables
        private bool _isRenamingCategory = false;
        private string _categoryToRename = "";
        private string _renameCategoryNewName = "";

        private float _currentRowHeight = SmallRowHeight;
        private float _currentIndicatorIconSize = SmallIndicatorIconSize;
        private float _currentPinButtonSize = SmallPinButtonSize;
        private float _currentReorderHandleIconSize = SmallReorderHandleIconSize;

        private GUIStyle _toolbarSearchTextFieldStyle;
        private GUIStyle _toolbarSearchCancelButtonStyle;
        private GUIStyle _rowButtonStyle;
        private GUIStyle _rowButtonSelectedStyle;
        private GUIStyle _pinButtonStyle;
        private GUIStyle _tooltipStyle;
        private GUIStyle _centeredLabelStyle;
        private GUIStyle _settingsAreaStyle;
        private Texture2D _sceneIcon;
        private Texture2D _projectIcon;
        private Texture2D _reorderHandleIcon;
        private readonly Color _hoverColor = new(0.5f, 0.5f, 0.5f, 0.2f);
        private readonly Color _altRowColor = new(0.0f, 0.0f, 0.0f, 0.08f);
        private readonly Color _connectorLineColor = new(0.5f, 0.5f, 0.5f, 0.4f);
        private bool _stylesInitialized;

        private bool _isReordering;
        private HistoryItem _reorderingItem;
        private HistoryItem _reorderDropTargetItem;
        private Vector2 _reorderDragStartPosition;
        private bool _isActivelyDraggingReorderItem;
        private int _hoveredItemLoopIndex = -1;

        #endregion

        #region Static Setup & Helpers

        [MenuItem("Window/FlowRecall/Selection History")]
        public static void ShowWindow()
        {
            var window = GetWindow<SelectionHistoryWindow>("Selection History");
            window.minSize = new Vector2(350, 300);
        }

        public static string GetAssetGuid(Object obj)
        {
            if (obj == null) return null;
            var path = AssetDatabase.GetAssetPath(obj);
            if (!string.IsNullOrEmpty(path) && AssetDatabase.Contains(obj)) return AssetDatabase.AssetPathToGUID(path);
            return null;
        }


        public static Object LoadAssetFromGuid(string guid)
        {
            if (string.IsNullOrEmpty(guid)) return null;
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path)) return null;
            return AssetDatabase.LoadAssetAtPath<Object>(path);
        }

        public static string GetObjectPath(Object obj)
        {
            if (obj == null) return "N/A";

            var assetPath = AssetDatabase.GetAssetPath(obj);
            if (!string.IsNullOrEmpty(assetPath)) return assetPath;

            if (obj is GameObject go) return GetGameObjectPath(go.transform);
            if (obj is Component co && co != null && co.gameObject != null)
                return GetGameObjectPath(co.transform) + $": {co.GetType().Name}";

            return $"Scene: {obj.name} (Type: {obj.GetType().Name}, ID: {obj.GetInstanceID()})";
        }

        private static string GetGameObjectPath(Transform transform)
        {
            if (transform == null) return "[Unknown Transform]";
            var pathBuilder = new StringBuilder(transform.name);
            var current = transform;
            var depth = 0;
            while (current.parent != null && depth < 100)
            {
                current = current.parent;
                pathBuilder.Insert(0, current.name + "/");
                depth++;
            }

            var sceneName = transform.gameObject.scene.name;
            if (!string.IsNullOrEmpty(sceneName)) pathBuilder.Insert(0, $"[{sceneName}]/");
            else pathBuilder.Insert(0, "[Unsaved Scene?]/");

            return pathBuilder.ToString();
        }

        #endregion

        #region Unity Lifecycle Methods

        private void OnEnable()
        {
            LoadPrefs();
            LoadHistory();
            Selection.selectionChanged += OnSelectionChanged;
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
            UpdateSizingBasedOnSetting();
            if (_selectionHistory.Any()) Repaint();
        }

        private void OnDisable()
        {
            SavePrefs();
            Selection.selectionChanged -= OnSelectionChanged;
            EditorApplication.hierarchyChanged -= OnHierarchyChanged;
            SaveHistory();
        }

        private void OnHierarchyChanged()
        {
            var needsRepaint = false;
            for (var i = _selectionHistory.Count - 1; i >= 0; i--)
            {
                var item = _selectionHistory[i];

                if (item.Target == null && !item.IsSceneObject() && !string.IsNullOrEmpty(item.AssetGuid))
                {
                    item.Target = LoadAssetFromGuid(item.AssetGuid);
                    if (item.Target != null) item.CachedPath = GetObjectPath(item.Target);
                }

                if (!item.IsValid())
                {
                    if (!item.IsPinned)
                    {
                        _selectionHistory.RemoveAt(i);
                        needsRepaint = true;
                    }

                    continue;
                }

                var newPath = GetObjectPath(item.Target);
                if (item.CachedPath != newPath)
                {
                    if (!item.IsSceneObject() && !string.IsNullOrEmpty(item.AssetGuid))
                    {
                        var currentPathFromGuid = AssetDatabase.GUIDToAssetPath(item.AssetGuid);
                        if (item.CachedPath != currentPathFromGuid && !string.IsNullOrEmpty(currentPathFromGuid))
                        {
                            item.CachedPath = currentPathFromGuid;
                            needsRepaint = true;
                        }
                        else if (item.CachedPath != newPath)
                        {
                            item.CachedPath = newPath;
                            needsRepaint = true;
                        }
                    }
                    else if (item.IsSceneObject())
                    {
                        item.CachedPath = newPath;
                        needsRepaint = true;
                    }
                }
            }

            if (needsRepaint) Repaint();
        }


        private void OnGUI()
        {
            InitializeStyles();
            UpdateSizingBasedOnSetting();
            HandleDragAndDropPinning();

            var fullDisplayList = GetItemsForDisplay();
            var totalItems = fullDisplayList.Count;
            var totalPages = Mathf.Max(1, Mathf.CeilToInt((float)totalItems / ItemsPerPage));
            _currentPage = Mathf.Clamp(_currentPage, 0, totalPages - 1);

            DrawSearchBar();
            DrawControlsBar();
            if (_showSettings) DrawSettingsArea();
            DrawTabsBar();
            EditorGUILayout.Space(2);
            DrawHistoryList(fullDisplayList, totalItems);
            DrawPaginationControls(totalItems, totalPages);
        }

        #endregion

        #region Core Logic

        private string GetTargetCategoryForNewPin()
        {
            if (_selectedCategoryFilterIndex > 0 && _selectedCategoryFilterIndex <= _categories.Count)
                return _categories[_selectedCategoryFilterIndex - 1];
            return "Default";
        }

        private void OnSelectionChanged()
        {
            if (_ignoreNextSelectionChange)
            {
                _ignoreNextSelectionChange = false;
                return;
            }

            var selectedObject = Selection.activeObject;
            if (selectedObject == null) return;

            if (selectedObject.hideFlags != HideFlags.None && !AssetDatabase.Contains(selectedObject))
                if (!(selectedObject is GameObject || selectedObject is Component))
                    return;

            if (selectedObject is AssetImporter || selectedObject is EditorWindow || selectedObject is Editor) return;


            AddObjectToHistory(selectedObject);
            Repaint();
        }

        private void AddObjectToHistory(Object obj)
        {
            if (obj == null) return;

            var objGuid = GetAssetGuid(obj);
            var objInstanceId = obj.GetInstanceID();
            var isAddingSceneObject = string.IsNullOrEmpty(objGuid);

            HistoryItem existingItem = null;
            var existingItemIndex = -1;

            for (var i = 0; i < _selectionHistory.Count; i++)
            {
                var currentItem = _selectionHistory[i];
                if (!currentItem.IsValid() && !currentItem.IsPinned) continue;

                var isCurrentItemSceneObject = currentItem.IsSceneObject();
                var match = false;

                if (isAddingSceneObject && isCurrentItemSceneObject)
                    match = currentItem.InstanceID == objInstanceId && currentItem.Target == obj;
                else if (!isAddingSceneObject && !isCurrentItemSceneObject)
                    match = !string.IsNullOrEmpty(objGuid) && currentItem.AssetGuid == objGuid;

                if (match)
                {
                    existingItem = currentItem;
                    existingItemIndex = i;
                    break;
                }
            }

            if (existingItem != null)
            {
                var isEffectivelyFirst = false;
                if (existingItem.IsPinned)
                {
                    var firstPinned = _selectionHistory.FirstOrDefault(it => it.IsPinned);
                    isEffectivelyFirst = firstPinned == existingItem;
                }
                else
                {
                    var firstNonPinned = _selectionHistory.FirstOrDefault(it => !it.IsPinned);
                    isEffectivelyFirst = firstNonPinned == existingItem;
                }

                if (isEffectivelyFirst && _moveSelectedToTopOnClick)
                {
                    existingItem.Timestamp = DateTime.UtcNow;
                    Repaint();
                    return;
                }

                _selectionHistory.RemoveAt(existingItemIndex);
                existingItem.Timestamp = DateTime.UtcNow;
                if (existingItem.IsPinned)
                {
                    _selectionHistory.Insert(0, existingItem);
                }
                else
                {
                    var firstPinnedIdx = _selectionHistory.FindIndex(h => h.IsPinned);
                    var insertAtIndex = firstPinnedIdx != -1 && _selectionHistory.Count(h => !h.IsPinned) > 0
                        ? _selectionHistory.TakeWhile(h => h.IsPinned).Count()
                        : 0;
                    _selectionHistory.Insert(insertAtIndex, existingItem);
                }
            }
            else
            {
                var newItem = new HistoryItem(obj);
                var insertAtIndex = _selectionHistory.TakeWhile(h => h.IsPinned).Count();
                _selectionHistory.Insert(insertAtIndex, newItem);
            }

            RemoveInvalidEntries();
            TrimHistory();
        }


        private void RemoveInvalidEntries()
        {
            _selectionHistory.RemoveAll(item => !item.IsPinned && !item.IsValid());
        }

        private void TrimHistory()
        {
            var nonPinnedItems = _selectionHistory.Where(i => !i.IsPinned).ToList();
            var overflow = nonPinnedItems.Count - _maxHistorySize;
            if (overflow > 0)
            {
                var itemsToRemove = nonPinnedItems.OrderBy(i => i.Timestamp).Take(overflow).ToList();
                foreach (var itemToRemove in itemsToRemove) _selectionHistory.Remove(itemToRemove);
            }
        }

        private void SelectObject(Object obj)
        {
            if (obj == null) return;
            Selection.activeObject = obj;
            EditorGUIUtility.PingObject(obj);
        }

        #endregion

        #region Drag and Drop Pinning (External to Favorites)

        private bool IsValidForPinningDrag(Object obj)
        {
            if (obj == null) return false;
            return true;
        }

        private void HandleDragAndDropPinning()
        {
            var currentEvent = Event.current;
            var eventType = currentEvent.type;
            var dropArea = new Rect(0, 0, position.width, position.height);

            if (!dropArea.Contains(currentEvent.mousePosition) || _isReordering) return;

            if (eventType == EventType.DragUpdated)
            {
                var canPin = DragAndDrop.objectReferences.Any(o => o != null && IsValidForPinningDrag(o));
                DragAndDrop.visualMode = canPin ? DragAndDropVisualMode.Link : DragAndDropVisualMode.Rejected;
                currentEvent.Use();
            }
            else if (eventType == EventType.DragPerform)
            {
                DragAndDrop.AcceptDrag();
                var changesMade = false;

                foreach (var obj in DragAndDrop.objectReferences)
                {
                    if (obj == null || !IsValidForPinningDrag(obj)) continue;

                    var objGuid = GetAssetGuid(obj);
                    var objInstanceId = obj.GetInstanceID();
                    var isDraggedObjectSceneAsset = string.IsNullOrEmpty(objGuid);

                    var itemToPin = _selectionHistory.FirstOrDefault(h =>
                        isDraggedObjectSceneAsset
                            ? h.IsSceneObject() && h.InstanceID == objInstanceId
                            : !h.IsSceneObject() && h.AssetGuid == objGuid);

                    if (itemToPin != null)
                    {
                        if (!itemToPin.IsPinned)
                        {
                            itemToPin.IsPinned = true;
                            itemToPin.Category = GetTargetCategoryForNewPin();
                            changesMade = true;
                        }

                        itemToPin.Timestamp = DateTime.UtcNow;
                        _selectionHistory.Remove(itemToPin);
                        _selectionHistory.Insert(0, itemToPin);
                    }
                    else
                    {
                        var newItem = new HistoryItem(obj) { IsPinned = true, Category = GetTargetCategoryForNewPin() };
                        _selectionHistory.Insert(0, newItem);
                        changesMade = true;
                    }
                }

                if (changesMade)
                {
                    RemoveInvalidEntries();
                    TrimHistory();
                    SaveHistory();
                    Repaint();
                }

                currentEvent.Use();
            }
        }

        #endregion

        #region GUI Drawing Methods

        private void InitializeStyles()
        {
            if (_stylesInitialized && _rowButtonStyle != null && _sceneIcon != null &&
                _reorderHandleIcon != null) return;

            _toolbarSearchTextFieldStyle =
                GUI.skin.FindStyle("ToolbarSearchTextField") ?? new GUIStyle(EditorStyles.toolbarTextField);
            _toolbarSearchCancelButtonStyle = GUI.skin.FindStyle("ToolbarSearchCancelButton")
                                              ?? new GUIStyle(EditorStyles.miniButton)
                                              {
                                                  fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter,
                                                  padding = new RectOffset(0, 0, 1, 0)
                                              };

            _rowButtonStyle = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(4, 4, 2, 2),
                margin = new RectOffset(),
                fixedHeight = _currentRowHeight
            };
            _rowButtonSelectedStyle = new GUIStyle(_rowButtonStyle);
            var proTextColor = new Color(0.5f, 0.5f, 0.5f);
            _rowButtonSelectedStyle.normal.textColor = EditorGUIUtility.isProSkin ? proTextColor : Color.white;

            _pinButtonStyle = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleCenter,
                padding = new RectOffset(),
                fontSize = SmallPinFontSize
            };
            _pinButtonStyle.normal.textColor = Color.gray;

            _tooltipStyle = new GUIStyle(GUI.skin.box)
            {
                wordWrap = true,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(4, 4, 4, 4)
            };
            _tooltipStyle.normal.textColor = EditorStyles.label.normal.textColor;

            _centeredLabelStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter };
            _settingsAreaStyle = new GUIStyle(EditorStyles.helpBox)
                { padding = new RectOffset(8, 8, 8, 8), margin = new RectOffset(4, 4, 0, 4) };

            _sceneIcon = EditorGUIUtility.IconContent("d_SceneAsset Icon")?.image as Texture2D ??
                         EditorGUIUtility.IconContent("SceneAsset Icon")?.image as Texture2D;
            _projectIcon = EditorGUIUtility.IconContent("d_Project")?.image as Texture2D ??
                           EditorGUIUtility.IconContent("Project")?.image as Texture2D;
            _reorderHandleIcon = EditorGUIUtility.IconContent("d_align_vertically_center_active")?.image as Texture2D ??
                                 EditorGUIUtility.IconContent("GripVertical")?.image as Texture2D;
            if (_reorderHandleIcon == null)
                _reorderHandleIcon = EditorGUIUtility.IconContent("align_vertically_center")?.image as Texture2D;

            _stylesInitialized = true;
        }

        private void UpdateSizingBasedOnSetting()
        {
            int targetPinFontSize;
            switch (_size)
            {
                case Size.Medium:
                    _currentRowHeight = MediumRowHeight;
                    _currentIndicatorIconSize = MediumIndicatorIconSize;
                    _currentPinButtonSize = MediumPinButtonSize;
                    targetPinFontSize = MediumPinFontSize;
                    _currentReorderHandleIconSize = MediumReorderHandleIconSize;
                    break;
                case Size.Small:
                default:
                    _currentRowHeight = SmallRowHeight;
                    _currentIndicatorIconSize = SmallIndicatorIconSize;
                    _currentPinButtonSize = SmallPinButtonSize;
                    targetPinFontSize = SmallPinFontSize;
                    _currentReorderHandleIconSize = SmallReorderHandleIconSize;
                    break;
            }

            if (_rowButtonStyle != null) _rowButtonStyle.fixedHeight = _currentRowHeight;
            if (_rowButtonSelectedStyle != null) _rowButtonSelectedStyle.fixedHeight = _currentRowHeight;
            if (_pinButtonStyle != null) _pinButtonStyle.fontSize = targetPinFontSize;
        }

        private void DrawSearchBar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("Filter:", EditorStyles.miniLabel, GUILayout.Width(35));
            EditorGUI.BeginChangeCheck();
            GUI.SetNextControlName("SearchFilterField");
            var newQuery = EditorGUILayout.TextField(_searchQuery, _toolbarSearchTextFieldStyle,
                GUILayout.ExpandWidth(true),
                GUILayout.MinWidth(60));
            if (EditorGUI.EndChangeCheck())
            {
                _searchQuery = newQuery;
                _currentPage = 0;
                Repaint();
            }

            if (GUILayout.Button("✕", _toolbarSearchCancelButtonStyle, GUILayout.Width(18f)))
                if (!string.IsNullOrEmpty(_searchQuery))
                {
                    _searchQuery = "";
                    GUI.FocusControl(null);
                    _currentPage = 0;
                    Repaint();
                    GUIUtility.ExitGUI();
                }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawControlsBar()
        {
            var prefsChanged = false;
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            // --- Settings Button ---
            var settingsIcon = EditorGUIUtility.IconContent("Settings");
            if (settingsIcon == null || settingsIcon.image == null)
                settingsIcon = EditorGUIUtility.IconContent("_Popup");

            var settingsButtonContent = settingsIcon != null && settingsIcon.image != null
                ? new GUIContent(settingsIcon.image, "Show/Hide Settings")
                : new GUIContent("⚙", "Show/Hide Settings");
            if (GUILayout.Button(settingsButtonContent, EditorStyles.toolbarButton, GUILayout.Width(25)))
                _showSettings = !_showSettings;

            GUILayout.Space(2);

            // --- Category Combo Box / Rename Box ---
            if (_isCreatingCategory || _isRenamingCategory)
            {
                string textValue = _isCreatingCategory ? _newCategoryName : _renameCategoryNewName;
                textValue = EditorGUILayout.TextField(textValue, EditorStyles.toolbarTextField, GUILayout.Width(60));

                if (_isCreatingCategory) _newCategoryName = textValue;
                else _renameCategoryNewName = textValue;

                // Confirm Create / Rename
                if (GUILayout.Button("✓", EditorStyles.toolbarButton, GUILayout.Width(20)))
                {
                    string trimmed = textValue.Trim().Replace(",", ""); // Remove commas to prevent serialization issues
                    if (!string.IsNullOrEmpty(trimmed))
                    {
                        if (_isCreatingCategory && !_categories.Contains(trimmed))
                        {
                            _categories.Add(trimmed);
                            _selectedCategoryFilterIndex = _categories.IndexOf(trimmed) + 1;
                            prefsChanged = true;
                        }
                        else if (_isRenamingCategory && !_categories.Contains(trimmed) && trimmed != _categoryToRename)
                        {
                            int catIndex = _categories.IndexOf(_categoryToRename);
                            if (catIndex >= 0)
                            {
                                _categories[catIndex] = trimmed;
                                // Update all items that had the old category
                                foreach (var item in _selectionHistory)
                                {
                                    if (item.Category == _categoryToRename) item.Category = trimmed;
                                }

                                SaveHistory();
                                prefsChanged = true;
                            }
                        }
                    }

                    _isCreatingCategory = false;
                    _isRenamingCategory = false;
                    _currentPage = 0;
                    GUI.FocusControl(null);
                }

                // Cancel Create / Rename
                if (GUILayout.Button("✕", EditorStyles.toolbarButton, GUILayout.Width(20)))
                {
                    _isCreatingCategory = false;
                    _isRenamingCategory = false;
                    GUI.FocusControl(null);
                }
            }
            else
            {
                var options = new List<string> { "[All]" };
                options.AddRange(_categories);
                options.Add("-----------");
                options.Add("[+] Category");

                // Use GetRect to catch right click events on the popup itself
                GUIContent popupLabel =
                    new GUIContent(options[Mathf.Clamp(_selectedCategoryFilterIndex, 0, options.Count - 1)]);
                Rect popupRect = GUILayoutUtility.GetRect(popupLabel, EditorStyles.toolbarPopup, GUILayout.Width(80));

                // Context menu for the selected Category (Right click the Combo Box)
                if (Event.current.type == EventType.ContextClick && popupRect.Contains(Event.current.mousePosition))
                {
                    if (_selectedCategoryFilterIndex > 0 && _selectedCategoryFilterIndex <= _categories.Count)
                    {
                        string selectedCat = _categories[_selectedCategoryFilterIndex - 1];
                        if (selectedCat != "Default")
                        {
                            // Protect "Default" category
                            GenericMenu menu = new GenericMenu();
                            menu.AddItem(new GUIContent($"{selectedCat}/Rename Category"), false, () =>
                            {
                                _isRenamingCategory = true;
                                _categoryToRename = selectedCat;
                                _renameCategoryNewName = selectedCat;
                            });
                            menu.AddItem(new GUIContent($"{selectedCat}/Delete Category"), false, () =>
                            {
                                if (EditorUtility.DisplayDialog("Delete Category",
                                        $"Are you sure you want to delete the category '{selectedCat}'?\n\nItems in this category will be moved back to 'Default'.",
                                        "Yes, Delete", "Cancel"))
                                {
                                    _categories.Remove(selectedCat);
                                    foreach (var item in _selectionHistory)
                                    {
                                        if (item.Category == selectedCat) item.Category = "Default";
                                    }

                                    _selectedCategoryFilterIndex = 0; // Reset view to [All]
                                    SaveHistory();
                                    SavePrefs();
                                    Repaint();
                                }
                            });
                            menu.ShowAsContext();
                            Event.current.Use(); // Consume the event
                        }
                    }
                }

                EditorGUI.BeginChangeCheck();
                int newIndex = EditorGUI.Popup(popupRect, _selectedCategoryFilterIndex, options.ToArray(),
                    EditorStyles.toolbarPopup);

                if (EditorGUI.EndChangeCheck())
                {
                    if (newIndex == options.Count - 1)
                    {
                        // Create New Category
                        _isCreatingCategory = true;
                        _newCategoryName = "";
                    }
                    else if (newIndex != options.Count - 2)
                    {
                        // Ignore the "---" separator
                        if (_selectedCategoryFilterIndex != newIndex)
                        {
                            _selectedCategoryFilterIndex = newIndex;
                            _currentPage = 0;
                            prefsChanged = true;
                        }
                    }
                }
            }

            GUILayout.FlexibleSpace();

            // --- Max Input ---
            GUILayout.Label(new GUIContent("Max:", "Max non-pinned items"), EditorStyles.miniLabel,
                GUILayout.Width(30));
            EditorGUI.BeginChangeCheck();
            var newMax = EditorGUILayout.IntField(_maxHistorySize, EditorStyles.toolbarTextField, GUILayout.Width(35));
            if (EditorGUI.EndChangeCheck() && newMax != _maxHistorySize)
                if (newMax >= 0)
                {
                    _maxHistorySize = newMax;
                    TrimHistory();
                    prefsChanged = true;
                    _currentPage = 0;
                    Repaint();
                }

            GUILayout.Space(5);

            // --- Context-aware Clear Button logic ---
            string clearTooltip = "Clear items";
            string dialogTitle = "Clear History";
            string dialogMessage = "Are you sure you want to clear all non-pinned items from the history?";

            switch (_currentTab)
            {
                case 0:
                    clearTooltip = "Clear non-pinned history items";
                    dialogTitle = "Clear History";
                    dialogMessage = "Are you sure you want to clear all non-pinned items from the history?";
                    break;
                case 1:
                    if (_selectedCategoryFilterIndex > 0)
                    {
                        string activeCat = _categories[_selectedCategoryFilterIndex - 1];
                        clearTooltip = $"Clear pinned items in category '{activeCat}'";
                        dialogTitle = "Clear Category Favorites";
                        dialogMessage =
                            $"Are you sure you want to remove all pinned items from the category '{activeCat}'?";
                    }
                    else
                    {
                        clearTooltip = "Clear all pinned items (Favorites)";
                        dialogTitle = "Clear Favorites";
                        dialogMessage = "Are you sure you want to remove all pinned items from ALL categories?";
                    }

                    break;
                case 2:
                    clearTooltip = "Clear all items";
                    dialogTitle = "Clear All Items";
                    dialogMessage = "Are you sure you want to clear ALL items (both pinned and non-pinned)?";
                    break;
            }

            if (GUILayout.Button(new GUIContent("Clear", clearTooltip), EditorStyles.toolbarButton,
                    GUILayout.Width(45)))
            {
                if (EditorUtility.DisplayDialog(dialogTitle, dialogMessage, "Yes, Clear", "Cancel"))
                {
                    if (_currentTab == 0)
                    {
                        _selectionHistory.RemoveAll(i => !i.IsPinned);
                    }
                    else if (_currentTab == 1)
                    {
                        if (_selectedCategoryFilterIndex == 0)
                        {
                            _selectionHistory.RemoveAll(i => i.IsPinned);
                        }
                        else
                        {
                            string activeCat = _categories[_selectedCategoryFilterIndex - 1];
                            _selectionHistory.RemoveAll(i => i.IsPinned && i.Category == activeCat);
                        }
                    }
                    else
                    {
                        _selectionHistory.Clear();
                    }

                    SaveHistory();
                    _currentPage = 0;
                    Repaint();
                    GUIUtility.ExitGUI();
                }
            }

            EditorGUILayout.EndHorizontal();
            if (prefsChanged) SavePrefs();
        }

        private void DrawSettingsArea()
        {
            EditorGUILayout.BeginVertical(_settingsAreaStyle);
            var prefsChanged = false;
            EditorGUI.BeginChangeCheck();

            _showSceneObjects =
                EditorGUILayout.ToggleLeft(
                    new GUIContent(" Show Scene Objects", "Include objects from the current scene(s)"),
                    _showSceneObjects);
            _showProjectAssets =
                EditorGUILayout.ToggleLeft(
                    new GUIContent(" Show Project Assets", "Include assets from the Project window (includes folders)"),
                    _showProjectAssets);

            EditorGUILayout.Space(2);

            _showIndicatorIcons =
                EditorGUILayout.ToggleLeft(
                    new GUIContent(" Show Project/Scene Icon",
                        "Display icons indicating if item is from scene or project (not shown for folders). Connects to thumbnail with a line."),
                    _showIndicatorIcons);
            _moveSelectedToTopOnClick = EditorGUILayout.ToggleLeft(
                new GUIContent(" Move Clicked Item To Top",
                    "When clicking an item to select it, also move it to the top of its history section (pinned/unpinned)"),
                _moveSelectedToTopOnClick);
            _showTypeName =
                EditorGUILayout.ToggleLeft(
                    new GUIContent(" Show Type Name", "Display the (Type) next to the item name"),
                    _showTypeName);


            EditorGUILayout.Space(5);

            _size = (Size)EditorGUILayout.EnumPopup(
                new GUIContent("Display Size", "Adjust row height and icon/thumbnail size"),
                _size);

            if (EditorGUI.EndChangeCheck())
            {
                prefsChanged = true;
                _currentPage = 0;
                UpdateSizingBasedOnSetting();
                Repaint();
            }

            EditorGUILayout.EndVertical();
            if (prefsChanged) SavePrefs();
        }

        private void DrawTabsBar()
        {
            EditorGUILayout.BeginHorizontal();
            var newTab = GUILayout.Toolbar(_currentTab, _tabLabels, EditorStyles.toolbarButton,
                GUILayout.ExpandWidth(true));
            if (newTab != _currentTab)
            {
                _currentTab = newTab;
                _currentPage = 0;
                Repaint();
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawPaginationControls(int totalItems, int totalPages)
        {
            if (totalPages <= 1) return;

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            EditorGUI.BeginDisabledGroup(_currentPage == 0);
            if (GUILayout.Button("◄ Prev", EditorStyles.miniButtonLeft, GUILayout.Width(50)))
            {
                _currentPage--;
                _scrollPosition = Vector2.zero;
                Repaint();
            }

            EditorGUI.EndDisabledGroup();

            GUILayout.Label($"Page {_currentPage + 1} / {totalPages}", _centeredLabelStyle, GUILayout.Width(80));

            EditorGUI.BeginDisabledGroup(_currentPage >= totalPages - 1);
            if (GUILayout.Button("Next ►", EditorStyles.miniButtonRight, GUILayout.Width(50)))
            {
                _currentPage++;
                _scrollPosition = Vector2.zero;
                Repaint();
            }

            EditorGUI.EndDisabledGroup();

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(2);
        }

        private List<HistoryItem> GetItemsForDisplay()
        {
            bool IsMatch(HistoryItem i)
            {
                if (!i.IsValid() && !i.IsPinned) return false;
                if (!_showSceneObjects && i.IsSceneObject()) return false;
                if (!_showProjectAssets && !i.IsSceneObject()) return false;

                // Apply Category Filter only for pinned items if a specific category is chosen
                if (i.IsPinned && _selectedCategoryFilterIndex > 0 && _selectedCategoryFilterIndex <= _categories.Count)
                {
                    if (i.Category != _categories[_selectedCategoryFilterIndex - 1]) return false;
                }

                if (!string.IsNullOrWhiteSpace(_searchQuery))
                {
                    var lowerQuery = _searchQuery.ToLowerInvariant().Trim();
                    var nameMatch = i.GetDisplayName().ToLowerInvariant().Contains(lowerQuery);
                    var pathMatch = !string.IsNullOrEmpty(i.CachedPath) &&
                                    i.CachedPath.ToLowerInvariant().Contains(lowerQuery);
                    var typeMatch = i.IsValid() && i.Target != null &&
                                    i.Target.GetType().Name.ToLowerInvariant().Contains(lowerQuery);
                    if (!(nameMatch || pathMatch || typeMatch)) return false;
                }

                return true;
            }

            var source = _selectionHistory.Where(IsMatch);

            switch (_currentTab)
            {
                case 0:
                    return source.Where(i => !i.IsPinned).OrderByDescending(i => i.Timestamp).ToList();
                case 1:
                    return source.Where(i => i.IsPinned).ToList();
                case 2:
                default:
                    var pinnedItems = source.Where(i => i.IsPinned).ToList();
                    var nonPinnedItems = source.Where(i => !i.IsPinned).OrderByDescending(i => i.Timestamp).ToList();
                    return pinnedItems.Concat(nonPinnedItems).ToList();
            }
        }


        private void DrawHistoryList(List<HistoryItem> displayList, int totalItems)
        {
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            var currentEvent = Event.current;
            var eventType = currentEvent.type;
            var needsRepaint = false;

            var startIndex = _currentPage * ItemsPerPage;
            var endIndex = Mathf.Min(startIndex + ItemsPerPage, totalItems);

            if (eventType == EventType.MouseMove)
            {
                var oldHoveredLoopIndex = _hoveredItemLoopIndex;
                _hoveredItemLoopIndex = -1;

                for (var k = 0; k < endIndex - startIndex; ++k)
                {
                    var tempRect = new Rect(0, k * _currentRowHeight, position.width, _currentRowHeight);
                    if (tempRect.Contains(currentEvent.mousePosition - _scrollPosition))
                    {
                        _hoveredItemLoopIndex = k;
                        break;
                    }
                }

                if (_hoveredItemLoopIndex != oldHoveredLoopIndex) needsRepaint = true;
            }
            else if (eventType == EventType.MouseLeaveWindow)
            {
                if (_hoveredItemLoopIndex != -1)
                {
                    _hoveredItemLoopIndex = -1;
                    needsRepaint = true;
                }
            }


            if (eventType == EventType.MouseDrag && _isReordering && _isActivelyDraggingReorderItem &&
                currentEvent.button == 0) _reorderDropTargetItem = null;

            if (eventType == EventType.MouseUp && currentEvent.button == 0 && _isReordering)
            {
                if (_isActivelyDraggingReorderItem && _reorderingItem != null)
                {
                    _selectionHistory.Remove(_reorderingItem);

                    if (_reorderDropTargetItem != null)
                    {
                        var targetIndex = _selectionHistory.IndexOf(_reorderDropTargetItem);
                        if (targetIndex != -1)
                        {
                            _selectionHistory.Insert(targetIndex, _reorderingItem);
                        }
                        else
                        {
                            var firstNonPinnedIdx = _selectionHistory.FindIndex(h => !h.IsPinned);
                            if (firstNonPinnedIdx != -1) _selectionHistory.Insert(firstNonPinnedIdx, _reorderingItem);
                            else _selectionHistory.Add(_reorderingItem);
                        }
                    }
                    else
                    {
                        var firstNonPinnedIdx = _selectionHistory.FindIndex(h => !h.IsPinned);
                        if (firstNonPinnedIdx != -1) _selectionHistory.Insert(firstNonPinnedIdx, _reorderingItem);
                        else _selectionHistory.Add(_reorderingItem);
                    }

                    SaveHistory();
                }

                _isReordering = false;
                _reorderingItem = null;
                _reorderDropTargetItem = null;
                _isActivelyDraggingReorderItem = false;
                needsRepaint = true;
                currentEvent.Use();
            }
            else if (eventType == EventType.MouseUp && currentEvent.button == 0 && !_isReordering)
            {
                if (_pendingClickItem != null)
                    if (!_isDragging && _pendingClickItem.IsValid())
                    {
                        var shouldMoveToTop = _moveSelectedToTopOnClick;
                        if (_currentTab == 1 && _pendingClickItem.IsPinned) shouldMoveToTop = false;

                        if (shouldMoveToTop) AddObjectToHistory(_pendingClickItem.Target);
                        else _ignoreNextSelectionChange = true;

                        SelectObject(_pendingClickItem.Target);
                        needsRepaint = true;
                        currentEvent.Use();
                    }

                _isDragging = false;
                _draggedItem = null;
                _pendingClickItem = null;
                if (!needsRepaint && eventType != EventType.Used) needsRepaint = true;
            }


            if (totalItems == 0)
                GUILayout.Label("No items match current filters.", EditorStyles.centeredGreyMiniLabel);
            else
                for (var loopIdx = 0; loopIdx < endIndex - startIndex; ++loopIdx)
                {
                    var historyItemDisplayIndex = startIndex + loopIdx;
                    var historyItem = displayList[historyItemDisplayIndex];

                    var itemRect = EditorGUILayout.GetControlRect(false, _currentRowHeight, _rowButtonStyle);
                    var isCurrentlyHoveredByMouse = _hoveredItemLoopIndex == loopIdx && !_isReordering && !_isDragging;

                    var isValidItem = historyItem.IsValid();
                    var isCurrentlySelected = isValidItem && Selection.activeObject == historyItem.Target;

                    if (_isReordering && eventType == EventType.MouseDrag && currentEvent.button == 0)
                    {
                        if (!_isActivelyDraggingReorderItem)
                            if (Vector2.Distance(currentEvent.mousePosition, _reorderDragStartPosition) >
                                ReorderDragThreshold)
                                _isActivelyDraggingReorderItem = true;

                        if (_isActivelyDraggingReorderItem)
                        {
                            if (itemRect.Contains(currentEvent.mousePosition))
                                if (historyItem != _reorderingItem)
                                {
                                    if (currentEvent.mousePosition.y < itemRect.center.y)
                                    {
                                        _reorderDropTargetItem = historyItem;
                                    }
                                    else
                                    {
                                        var currentDisplayIndexOfItem = displayList.IndexOf(historyItem);
                                        if (currentDisplayIndexOfItem + 1 < displayList.Count)
                                        {
                                            var nextItem = displayList[currentDisplayIndexOfItem + 1];
                                            if (nextItem != _reorderingItem) _reorderDropTargetItem = nextItem;
                                            else if (currentDisplayIndexOfItem + 2 < displayList.Count)
                                                _reorderDropTargetItem = displayList[currentDisplayIndexOfItem + 2];
                                            else _reorderDropTargetItem = null;
                                        }
                                        else
                                        {
                                            _reorderDropTargetItem = null;
                                        }
                                    }
                                }

                            needsRepaint = true;
                        }
                    }

                    if (eventType == EventType.Repaint)
                    {
                        if (loopIdx % 2 != 0) EditorGUI.DrawRect(itemRect, _altRowColor);
                        if (isCurrentlyHoveredByMouse && !isCurrentlySelected)
                            EditorGUI.DrawRect(itemRect, _hoverColor);
                        if (isCurrentlySelected) EditorGUI.DrawRect(itemRect, GUI.skin.settings.selectionColor);

                        if (_isReordering && _isActivelyDraggingReorderItem && _reorderingItem != null)
                        {
                            var lineXStart = itemRect.xMin;
                            var lineWidth = itemRect.width;

                            var drawLineAbove = _reorderDropTargetItem == historyItem;

                            var isLastVisiblePinnedItemOnPage = historyItemDisplayIndex == endIndex - 1;
                            var lastPinnedItemInFullList =
                                displayList.LastOrDefault(it => it.IsPinned && it != _reorderingItem);
                            var isTrulyLastPinnedItem = lastPinnedItemInFullList == historyItem;


                            var drawLineAtEnd = _reorderDropTargetItem == null &&
                                                isLastVisiblePinnedItemOnPage &&
                                                isTrulyLastPinnedItem &&
                                                currentEvent.mousePosition.y >
                                                itemRect.yMax - _scrollPosition.y + itemRect.height * 0.25f;


                            if (drawLineAbove)
                            {
                                var lineRect = new Rect(lineXStart, itemRect.yMin - 1, lineWidth, 2);
                                EditorGUI.DrawRect(lineRect, GUI.skin.settings.selectionColor);
                            }
                            else if (drawLineAtEnd)
                            {
                                var lineRect = new Rect(lineXStart, itemRect.yMax - 1, lineWidth, 2);
                                EditorGUI.DrawRect(lineRect, GUI.skin.settings.selectionColor);
                            }
                        }
                    }

                    var currentX = itemRect.x + _rowButtonStyle.padding.left;
                    var availableHeight = itemRect.height;
                    var rightEdgeLimit = itemRect.xMax - _rowButtonStyle.padding.right;

                    var reorderHandleRect = Rect.zero;
                    if (_currentTab == 1 && _reorderHandleIcon != null)
                    {
                        reorderHandleRect = new Rect(
                            rightEdgeLimit - _currentReorderHandleIconSize,
                            itemRect.y + (availableHeight - _currentReorderHandleIconSize) / 2,
                            _currentReorderHandleIconSize,
                            _currentReorderHandleIconSize
                        );
                        rightEdgeLimit = reorderHandleRect.xMin - IconPadding;

                        if (eventType == EventType.Repaint)
                        {
                            var originalGuiColor = GUI.color;
                            GUI.color = new Color(originalGuiColor.r, originalGuiColor.g, originalGuiColor.b, 0.8f);
                            GUI.DrawTexture(reorderHandleRect, _reorderHandleIcon, ScaleMode.ScaleToFit);
                            GUI.color = originalGuiColor;
                        }
                        else if (eventType == EventType.MouseDown &&
                                 reorderHandleRect.Contains(currentEvent.mousePosition) &&
                                 currentEvent.button == 0)
                        {
                            if (historyItem.IsPinned)
                            {
                                _isReordering = true;
                                _reorderingItem = historyItem;
                                _reorderDragStartPosition = currentEvent.mousePosition;
                                _isActivelyDraggingReorderItem = false;
                                _reorderDropTargetItem = null;
                                _pendingClickItem = null;
                                _draggedItem = null;
                                needsRepaint = true;
                                currentEvent.Use();
                            }
                        }
                    }


                    var pinRect = new Rect(
                        currentX,
                        itemRect.y + (availableHeight - _currentPinButtonSize) / 2,
                        _currentPinButtonSize,
                        _currentPinButtonSize
                    );
                    currentX += _currentPinButtonSize + IconPadding;

                    var indicatorRect = Rect.zero;
                    var isFolder = isValidItem && !historyItem.IsSceneObject() && historyItem.Target is DefaultAsset &&
                                   AssetDatabase.IsValidFolder(historyItem.CachedPath);
                    var shouldDisplayIndicatorIcon =
                        _showIndicatorIcons && _sceneIcon != null && _projectIcon != null && !isFolder;

                    var thumbnailStartX = currentX;

                    if (shouldDisplayIndicatorIcon)
                    {
                        indicatorRect = new Rect(
                            currentX,
                            itemRect.y + (availableHeight - _currentIndicatorIconSize) / 2,
                            _currentIndicatorIconSize,
                            _currentIndicatorIconSize
                        );
                        thumbnailStartX = indicatorRect.xMax + IconPadding;
                    }

                    Texture itemThumbnail = null;
                    var thumbnailRect = Rect.zero;
                    var thumbnailDrawSize = _size == Size.Medium ? MediumThumbnailSize : SmallThumbnailSize;
                    thumbnailDrawSize = Mathf.Min(thumbnailDrawSize, availableHeight - 4);

                    thumbnailRect = new Rect(
                        thumbnailStartX,
                        itemRect.y + (availableHeight - thumbnailDrawSize) / 2,
                        thumbnailDrawSize,
                        thumbnailDrawSize
                    );

                    if (isValidItem) itemThumbnail = AssetPreview.GetMiniThumbnail(historyItem.Target);
                    else itemThumbnail = EditorGUIUtility.IconContent("console.warnicon.sml")?.image;

                    if (!isValidItem && itemThumbnail != null && SmallThumbnailSize < thumbnailDrawSize)
                    {
                        var warningIconSize = Mathf.Min(SmallThumbnailSize, availableHeight - 4);
                        thumbnailRect.width = warningIconSize;
                        thumbnailRect.height = warningIconSize;
                        thumbnailRect.y = itemRect.y + (availableHeight - warningIconSize) / 2;
                    }

                    _pinButtonStyle.normal.textColor = historyItem.IsPinned
                        ? EditorGUIUtility.isProSkin ? Color.yellow : new Color(0.9f, 0.7f, 0.1f)
                        : Color.gray;

                    if (GUI.Button(pinRect, historyItem.IsPinned ? "★" : "☆", _pinButtonStyle))
                    {
                        historyItem.IsPinned = !historyItem.IsPinned;
                        if (historyItem.IsPinned)
                        {
                            historyItem.Category = GetTargetCategoryForNewPin();
                            _selectionHistory.Remove(historyItem);
                            _selectionHistory.Insert(0, historyItem);
                        }
                        else
                        {
                            historyItem.Timestamp = DateTime.UtcNow;
                            _selectionHistory.Remove(historyItem);
                            var firstNonPinnedInsertIndex = _selectionHistory.TakeWhile(h => h.IsPinned).Count();
                            _selectionHistory.Insert(firstNonPinnedInsertIndex, historyItem);
                            TrimHistory();
                        }

                        SaveHistory();
                        needsRepaint = true;
                        _pendingClickItem = null;
                        _draggedItem = null;
                        currentEvent.Use();
                    }


                    if (eventType == EventType.Repaint && shouldDisplayIndicatorIcon && indicatorRect != Rect.zero)
                    {
                        var indicatorIconToDraw = historyItem.IsSceneObject() ? _sceneIcon : _projectIcon;
                        if (indicatorIconToDraw != null)
                            GUI.DrawTexture(indicatorRect, indicatorIconToDraw, ScaleMode.ScaleToFit);
                    }

                    if (eventType == EventType.Repaint && shouldDisplayIndicatorIcon && indicatorRect != Rect.zero &&
                        itemThumbnail != null && thumbnailRect != Rect.zero)
                        if (thumbnailRect.xMin > indicatorRect.xMax + 0.5f)
                        {
                            var lineY = indicatorRect.center.y;
                            var lineThickness = 1f;
                            var lineRect = new Rect(indicatorRect.xMax,
                                lineY - lineThickness / 2f,
                                thumbnailRect.xMin - indicatorRect.xMax,
                                lineThickness);
                            if (lineRect.width > 0.5f) EditorGUI.DrawRect(lineRect, _connectorLineColor);
                        }

                    if (eventType == EventType.Repaint && itemThumbnail != null && thumbnailRect != Rect.zero)
                        GUI.DrawTexture(thumbnailRect, itemThumbnail, ScaleMode.ScaleToFit);

                    var mainContentStartXCalculated = thumbnailRect.xMax + IconPadding;
                    if (itemThumbnail == null) mainContentStartXCalculated = thumbnailStartX;


                    var baseName = "[Unnamed Object]";
                    if (isValidItem && historyItem.Target != null) baseName = historyItem.Target.name;
                    else if (!isValidItem) baseName = "[Deleted Object]";
                    if (string.IsNullOrEmpty(baseName)) baseName = "[Unnamed Object]";

                    var labelText = baseName;
                    if (_showTypeName && isValidItem && historyItem.Target != null)
                    {
                        var typeString = historyItem.Target.GetType()?.Name ?? "Unknown";
                        if (historyItem.Target is DefaultAsset && AssetDatabase.IsValidFolder(historyItem.CachedPath))
                            typeString = "Folder";
                        labelText += $" ({typeString})";
                    }

                    var mainContentGUIC = new GUIContent($" {labelText}",
                        isValidItem ? historyItem.CachedPath : historyItem.GetDisplayName());
                    var currentStyleToUse = isCurrentlySelected ? _rowButtonSelectedStyle : _rowButtonStyle;
                    if (!isValidItem)
                    {
                        var disabledStyle = new GUIStyle(currentStyleToUse);
                        disabledStyle.normal.textColor = Color.grey;
                        currentStyleToUse = disabledStyle;
                    }

                    var mainContentDisplayRect = new Rect(
                        mainContentStartXCalculated,
                        itemRect.y,
                        rightEdgeLimit - mainContentStartXCalculated,
                        availableHeight
                    );


                    if (eventType == EventType.MouseDown &&
                        mainContentDisplayRect.Contains(currentEvent.mousePosition) &&
                        currentEvent.button == 0 && !_isReordering)
                    {
                        if (currentEvent.clickCount == 2)
                        {
                            if (isValidItem)
                            {
                                var shouldMoveItemToTopForDoubleClick = _moveSelectedToTopOnClick;
                                if (_currentTab == 1 && historyItem.IsPinned) shouldMoveItemToTopForDoubleClick = false;

                                if (shouldMoveItemToTopForDoubleClick) AddObjectToHistory(historyItem.Target);

                                _ignoreNextSelectionChange = true;

                                if (historyItem.IsSceneObject()) SelectObject(historyItem.Target);
                                else AssetDatabase.OpenAsset(historyItem.Target);
                            }

                            _pendingClickItem = null;
                            _draggedItem = null;
                            needsRepaint = true;
                            currentEvent.Use();
                        }
                        else
                        {
                            _draggedItem = historyItem;
                            _dragStartPosition = currentEvent.mousePosition;
                            _isDragging = false;
                            _pendingClickItem = historyItem;
                            needsRepaint = true;
                        }
                    }
                    else if (eventType == EventType.Repaint)
                    {
                        GUI.Label(mainContentDisplayRect, mainContentGUIC, currentStyleToUse);
                    }

                    if (eventType == EventType.MouseDrag && currentEvent.button == 0 && _draggedItem == historyItem &&
                        !_isReordering)
                        if (!_isDragging &&
                            Vector2.Distance(currentEvent.mousePosition, _dragStartPosition) > ReorderDragThreshold)
                        {
                            if (isValidItem)
                            {
                                _pendingClickItem = null;
                                DragAndDrop.PrepareStartDrag();
                                DragAndDrop.objectReferences = new[] { _draggedItem.Target };
                                DragAndDrop.paths = !_draggedItem.IsSceneObject()
                                    ? new[] { _draggedItem.CachedPath }
                                    : null;
                                DragAndDrop.visualMode = DragAndDropVisualMode.Copy | DragAndDropVisualMode.Link;
                                DragAndDrop.StartDrag(historyItem.GetDisplayName());
                                _isDragging = true;
                                currentEvent.Use();
                            }
                            else
                            {
                                _draggedItem = null;
                                _pendingClickItem = null;
                            }
                        }


                    if (eventType == EventType.ContextClick && itemRect.Contains(currentEvent.mousePosition) &&
                        !_isReordering)
                    {
                        ShowContextMenu(historyItem);
                        currentEvent.Use();
                    }
                }

            EditorGUILayout.EndScrollView();
            if (needsRepaint) Repaint();
        }

        private void ShowContextMenu(HistoryItem item)
        {
            var menu = new GenericMenu();
            var isValid = item.IsValid();
            var isAsset = isValid && !item.IsSceneObject();
            var isSceneObj = isValid && item.IsSceneObject();
            var isFolder = isValid && isAsset && item.Target is DefaultAsset &&
                           AssetDatabase.IsValidFolder(item.CachedPath);


            if (isValid)
            {
                menu.AddItem(new GUIContent("Select"), false, () =>
                {
                    var shouldMove = _moveSelectedToTopOnClick;
                    if (_currentTab == 1 && item.IsPinned) shouldMove = false;

                    if (shouldMove) AddObjectToHistory(item.Target);
                    else _ignoreNextSelectionChange = true;
                    SelectObject(item.Target);
                });

                menu.AddItem(new GUIContent("Ping"), false, () => EditorGUIUtility.PingObject(item.Target));

                if (isAsset)
                {
                    if (!isFolder)
                        menu.AddItem(new GUIContent("Open Asset"), false, () => AssetDatabase.OpenAsset(item.Target));
                    else
                        menu.AddItem(new GUIContent("Reveal in Project"), false,
                            () => EditorGUIUtility.PingObject(item.Target));

                    if (!string.IsNullOrEmpty(item.CachedPath) &&
                        (File.Exists(item.CachedPath) || Directory.Exists(item.CachedPath)))
                        menu.AddItem(new GUIContent("Show in Explorer/Finder"), false,
                            () => EditorUtility.RevealInFinder(item.CachedPath));
                    else menu.AddDisabledItem(new GUIContent("Show in Explorer/Finder (Path Invalid)"));

                    if (!isFolder)
                        menu.AddItem(new GUIContent("Find References in Project"), false, () =>
                        {
                            EditorUtility.FocusProjectWindow();
                            EditorGUIUtility.PingObject(item.Target);
                            EditorApplication.delayCall += () =>
                            {
                                Selection.activeObject = item.Target;
                                EditorApplication.ExecuteMenuItem("Assets/Find References In Project");
                            };
                        });

                    if (!string.IsNullOrEmpty(item.AssetGuid))
                        menu.AddItem(new GUIContent("Copy GUID"), false,
                            () => EditorGUIUtility.systemCopyBuffer = item.AssetGuid);
                    else menu.AddDisabledItem(new GUIContent("Copy GUID"));

                    var duplicateMenuLabel = isFolder ? "Duplicate Folder..." : "Duplicate Asset...";
                    menu.AddItem(new GUIContent(duplicateMenuLabel), false, () => DuplicateItem(item));
                    var deleteMenuLabel = isFolder ? "Delete Folder..." : "Delete Asset...";
                    menu.AddItem(new GUIContent(deleteMenuLabel), false, () => DeleteItem(item));
                }
                else if (isSceneObj)
                {
                    menu.AddDisabledItem(new GUIContent("Show in Explorer/Finder"));
                    menu.AddItem(new GUIContent("Duplicate GameObject"), false, () => DuplicateItem(item));
                    menu.AddItem(new GUIContent("Delete GameObject..."), false, () => DeleteItem(item));
                }

                if (!string.IsNullOrEmpty(item.CachedPath))
                    menu.AddItem(new GUIContent("Copy Path"), false,
                        () => EditorGUIUtility.systemCopyBuffer = item.CachedPath);
                else menu.AddDisabledItem(new GUIContent("Copy Path"));
                menu.AddItem(new GUIContent("Copy Name"), false, () =>
                {
                    if (item.Target != null) EditorGUIUtility.systemCopyBuffer = item.Target.name;
                });
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("Select (Object Deleted)"));
                menu.AddDisabledItem(new GUIContent("Ping (Object Deleted)"));

                if (!string.IsNullOrEmpty(item.CachedPath))
                    menu.AddItem(new GUIContent("Copy Path (Cached)"), false,
                        () => EditorGUIUtility.systemCopyBuffer = item.CachedPath);
                if (!item.IsSceneObject() && !string.IsNullOrEmpty(item.AssetGuid))
                    menu.AddItem(new GUIContent("Copy GUID (Cached)"), false,
                        () => EditorGUIUtility.systemCopyBuffer = item.AssetGuid);

                menu.AddDisabledItem(new GUIContent("Duplicate (Object Deleted)"));
                menu.AddDisabledItem(new GUIContent("Delete (Object Deleted)"));
            }

            menu.AddSeparator("");

            menu.AddItem(new GUIContent(item.IsPinned ? "Unpin Item" : "Pin Item"), false, () =>
            {
                item.IsPinned = !item.IsPinned;
                if (item.IsPinned)
                {
                    item.Category = GetTargetCategoryForNewPin();
                    _selectionHistory.Remove(item);
                    _selectionHistory.Insert(0, item);
                }
                else
                {
                    item.Timestamp = DateTime.UtcNow;
                    _selectionHistory.Remove(item);
                    var firstNonPinnedInsertIndex = _selectionHistory.TakeWhile(h => h.IsPinned).Count();
                    _selectionHistory.Insert(firstNonPinnedInsertIndex, item);
                    TrimHistory();
                }

                SaveHistory();
                Repaint();
            });

            if (item.IsPinned)
            {
                menu.AddSeparator("");
                foreach (var cat in _categories)
                {
                    bool isCurrentCat = (item.Category == cat);
                    menu.AddItem(new GUIContent($"Move to Category/{cat}"), isCurrentCat, () =>
                    {
                        item.Category = cat;
                        SaveHistory();
                        Repaint();
                    });
                }

                menu.AddSeparator("");
            }

            menu.AddItem(new GUIContent("Remove from History"), false, () =>
            {
                _selectionHistory.Remove(item);
                if (item.IsPinned && !item.IsSceneObject()) SaveHistory();
                Repaint();
            });

            menu.ShowAsContext();
        }

        private void DuplicateItem(HistoryItem item)
        {
            if (!item.IsValid()) return;

            var isFolder = !item.IsSceneObject() && item.Target is DefaultAsset &&
                           AssetDatabase.IsValidFolder(item.CachedPath);

            if (!item.IsSceneObject())
            {
                var originalPath = item.CachedPath;
                if (string.IsNullOrEmpty(originalPath) || !AssetDatabase.Contains(item.Target))
                {
                    Debug.LogError(
                        $"[{nameof(SelectionHistoryWindow)}] Cannot duplicate: Path is invalid or target is not a recognized asset/folder. Path: {originalPath}");
                    return;
                }

                var itemTypeForDialog = isFolder ? "folder" : "asset";
                if (EditorUtility.DisplayDialog($"Duplicate {itemTypeForDialog}?",
                        $"Are you sure you want to duplicate the {itemTypeForDialog} '{Path.GetFileName(originalPath)}'?",
                        "Duplicate", "Cancel"))
                {
                    var newPath = AssetDatabase.GenerateUniqueAssetPath(originalPath);
                    if (AssetDatabase.CopyAsset(originalPath, newPath))
                    {
                        Debug.Log($"[{nameof(SelectionHistoryWindow)}] {itemTypeForDialog} duplicated: {newPath}");
                        AssetDatabase.Refresh();
                        var newAsset = AssetDatabase.LoadAssetAtPath<Object>(newPath);
                        if (newAsset != null)
                        {
                            AddObjectToHistory(newAsset);
                            EditorApplication.delayCall += () => EditorGUIUtility.PingObject(newAsset);
                        }
                    }
                    else
                    {
                        Debug.LogError(
                            $"[{nameof(SelectionHistoryWindow)}] Failed to duplicate {itemTypeForDialog}: {originalPath}");
                    }
                }
            }
            else
            {
                var go = item.Target as GameObject ?? (item.Target as Component)?.gameObject;
                if (go == null)
                {
                    Debug.LogError(
                        $"[{nameof(SelectionHistoryWindow)}] Cannot duplicate: Target GameObject not found.");
                    return;
                }

                if (string.IsNullOrEmpty(go.scene.path) && PrefabUtility.IsPartOfPrefabAsset(go))
                {
                    Debug.LogWarning(
                        $"[{nameof(SelectionHistoryWindow)}] GameObject '{go.name}' is part of a Prefab Asset. Please duplicate the Prefab Asset itself from the Project window if you intend to create a new Prefab variant. Duplicating in scene will create an instance.",
                        go);
                }
                else if (string.IsNullOrEmpty(go.scene.path))
                {
                    Debug.LogWarning(
                        $"[{nameof(SelectionHistoryWindow)}] Cannot duplicate GameObject '{go.name}' that is not part of a saved scene and not a prefab asset. This might be an in-memory object not fully part of the scene hierarchy.",
                        go);
                    return;
                }


                Undo.SetCurrentGroupName("Duplicate History Object");
                var undoGroup = Undo.GetCurrentGroup();
                var newGameObject = Instantiate(go, go.transform.parent);
                newGameObject.name = go.name + " (Clone)";
                Undo.RegisterCreatedObjectUndo(newGameObject, "Duplicate GameObject");
                Undo.CollapseUndoOperations(undoGroup);

                Debug.Log($"[{nameof(SelectionHistoryWindow)}] GameObject duplicated: {newGameObject.name}",
                    newGameObject);
                AddObjectToHistory(newGameObject);
                SelectObject(newGameObject);
            }
        }

        private void DeleteItem(HistoryItem item)
        {
            if (!item.IsValid())
            {
                if (EditorUtility.DisplayDialog("Remove Invalid Item?",
                        $"The object '{item.GetDisplayName()}' no longer exists.\n\nDo you want to remove this entry from the history?",
                        "Remove Entry", "Cancel"))
                {
                    _selectionHistory.Remove(item);
                    if (item.IsPinned && !item.IsSceneObject()) SaveHistory();
                    Repaint();
                }

                return;
            }

            var isSceneObject = item.IsSceneObject();
            var isFolder = !isSceneObject && item.Target is DefaultAsset &&
                           AssetDatabase.IsValidFolder(item.CachedPath);
            var typeName = isSceneObject ? "GameObject" : isFolder ? "Folder" : "Asset";
            var objectName = item.Target.name;
            var pathName = isSceneObject ? "" :
                !string.IsNullOrEmpty(item.CachedPath) ? Path.GetFileName(item.CachedPath) : objectName;
            var prompt = isSceneObject
                ? $"Are you sure you want to delete the GameObject '{objectName}' from the scene?\n\nThis action can be undone."
                : $"Are you sure you want to permanently delete the {typeName.ToLower()} '{pathName}' from your project?\n\nTHIS ACTION CANNOT BE UNDONE.";
            var confirmButton = "Delete " + typeName;

            var confirmationResult = EditorUtility.DisplayDialogComplex(
                $"Delete {typeName}?",
                prompt,
                confirmButton,
                "Cancel",
                "");

            if (confirmationResult == 0)
            {
                var deletedSuccessfully = false;
                if (!isSceneObject)
                {
                    var assetPath = item.CachedPath;
                    if (!string.IsNullOrEmpty(assetPath))
                    {
                        AssetDatabase.StartAssetEditing();
                        try
                        {
                            if (AssetDatabase.DeleteAsset(assetPath))
                            {
                                Debug.Log(
                                    $"[{nameof(SelectionHistoryWindow)}] {typeName} permanently deleted: {assetPath}");
                                deletedSuccessfully = true;
                            }
                            else
                            {
                                Debug.LogError(
                                    $"[{nameof(SelectionHistoryWindow)}] Failed to delete {typeName.ToLower()}: {assetPath}");
                            }
                        }
                        finally
                        {
                            AssetDatabase.StopAssetEditing();
                        }
                    }
                    else
                    {
                        Debug.LogWarning(
                            $"[{nameof(SelectionHistoryWindow)}] Cannot delete {typeName.ToLower()} '{objectName}', path is missing.",
                            item.Target);
                    }
                }
                else
                {
                    var go = item.Target as GameObject ?? (item.Target as Component)?.gameObject;
                    if (go != null)
                    {
                        var goName = go.name;
                        Undo.DestroyObjectImmediate(go);
                        Debug.Log($"[{nameof(SelectionHistoryWindow)}] GameObject deleted: {goName}");
                        deletedSuccessfully = true;
                    }
                    else
                    {
                        Debug.LogWarning(
                            $"[{nameof(SelectionHistoryWindow)}] GameObject '{objectName}' could not be found for deletion.",
                            item.Target);
                    }
                }

                if (deletedSuccessfully)
                {
                    item.Target = null;
                    if (item.IsPinned && !item.IsSceneObject()) SaveHistory();
                    Repaint();
                }
            }
        }

        #endregion

        #region Persistence (Prefs & History)

        private void LoadPrefs()
        {
            _maxHistorySize = EditorPrefs.GetInt(HistorySizePrefKey, 30);
            _showSceneObjects = EditorPrefs.GetBool(ShowScenePrefKey, true);
            _showProjectAssets = EditorPrefs.GetBool(ShowProjectPrefKey, true);
            _moveSelectedToTopOnClick = EditorPrefs.GetBool(MoveSelectedToTopPrefKey, true);
            _showIndicatorIcons = EditorPrefs.GetBool(ShowIndicatorIconsPrefKey, true);
            _showTypeName = EditorPrefs.GetBool(ShowTypeNamePrefKey, true);

            var sizeInt = EditorPrefs.GetInt(SizePrefKey, (int)Size.Small);
            _size = Enum.IsDefined(typeof(Size), sizeInt) ? (Size)sizeInt : Size.Small;

            var catRaw = EditorPrefs.GetString(CategoriesPrefKey, "Default");
            _categories = catRaw.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            if (_categories.Count == 0) _categories.Add("Default");

            _selectedCategoryFilterIndex = EditorPrefs.GetInt(ActiveCategoryPrefKey, 0);
            if (_selectedCategoryFilterIndex > _categories.Count) _selectedCategoryFilterIndex = 0;
        }

        private void SavePrefs()
        {
            EditorPrefs.SetInt(HistorySizePrefKey, _maxHistorySize);
            EditorPrefs.SetBool(ShowScenePrefKey, _showSceneObjects);
            EditorPrefs.SetBool(ShowProjectPrefKey, _showProjectAssets);
            EditorPrefs.SetBool(MoveSelectedToTopPrefKey, _moveSelectedToTopOnClick);
            EditorPrefs.SetInt(SizePrefKey, (int)_size);
            EditorPrefs.SetBool(ShowIndicatorIconsPrefKey, _showIndicatorIcons);
            EditorPrefs.SetBool(ShowTypeNamePrefKey, _showTypeName);

            EditorPrefs.SetString(CategoriesPrefKey, string.Join("|", _categories));
            EditorPrefs.SetInt(ActiveCategoryPrefKey, _selectedCategoryFilterIndex);
        }

        private void SaveHistory()
        {
            // Pinned Items now save their Category as well (Guid,Category format)
            var pinnedAssetData = _selectionHistory
                .Where(i => i.IsPinned && !i.IsSceneObject() && !string.IsNullOrEmpty(i.AssetGuid))
                .Select(i => $"{i.AssetGuid},{i.Category}")
                .ToList();
            EditorPrefs.SetString(PinnedDataPrefKey, string.Join("|", pinnedAssetData));

            var historyAssetGuids = _selectionHistory
                .Where(i => !i.IsPinned && !i.IsSceneObject() && !string.IsNullOrEmpty(i.AssetGuid))
                .OrderByDescending(i => i.Timestamp)
                .Select(i => i.AssetGuid)
                .Distinct()
                .Take(_maxHistorySize)
                .ToList();
            EditorPrefs.SetString(HistoryDataPrefKey, string.Join("|", historyAssetGuids));
        }

        private void LoadHistory()
        {
            _selectionHistory.Clear();
            var loadedItems = new List<HistoryItem>();
            var loadedPinnedGuids = new HashSet<string>();

            var pinnedGuidsRaw = EditorPrefs.GetString(PinnedDataPrefKey, "");
            if (!string.IsNullOrEmpty(pinnedGuidsRaw))
                foreach (var data in pinnedGuidsRaw.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var parts = data.Split(',');
                    var guid = parts[0];
                    var category = parts.Length > 1 ? parts[1] : "Default";

                    var item = new HistoryItem(guid, true) { Category = category };
                    loadedItems.Add(item);
                    loadedPinnedGuids.Add(guid);
                }

            var historyGuidsRaw = EditorPrefs.GetString(HistoryDataPrefKey, "");
            if (!string.IsNullOrEmpty(historyGuidsRaw))
            {
                var loadedHistoryCount = 0;
                foreach (var guid in historyGuidsRaw.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (loadedHistoryCount >= _maxHistorySize) break;
                    if (loadedPinnedGuids.Contains(guid)) continue;

                    var item = new HistoryItem(guid, false);
                    if (item.IsValid())
                    {
                        loadedItems.Add(item);
                        loadedHistoryCount++;
                    }
                }
            }

            _selectionHistory.AddRange(loadedItems);
        }

        #endregion
    }
}