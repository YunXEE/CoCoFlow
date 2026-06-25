using CoCoFlow.Runtime.Modules.Persistence.Container;
using UnityEditor;
using UnityEngine;

namespace CoCoFlow.Editor.Modules.Persistence
{
    public sealed class PersistenceCatalogEditor : EditorWindow
    {
        private static readonly CatalogTab[] Tabs =
        {
            new CatalogTab("Overview", string.Empty, "Catalog summary and validation."),
            new CatalogTab("Items", "itemDefinitions", "Static item definitions used by inventories, loot and rewards."),
            new CatalogTab("Containers", "containerDefinitions", "Container capability contracts and transfer rules."),
            new CatalogTab("Templates", "containerTemplates", "Materialized runtime container templates."),
            new CatalogTab("Rewards", "rewardDefinitions", "Reward bundles granted into item containers."),
            new CatalogTab("Loot", "lootTableDefinitions", "Weighted entries for randomized container materialization."),
            new CatalogTab("Quests", "sequentialQuestDefinitions", "V1 sequential quest definitions."),
            new CatalogTab("Events", "eventDefinitions", "Known event facts that can be stored in event containers."),
            new CatalogTab("Facts", "factDefinitions", "Known world facts that can be stored in fact containers."),
            new CatalogTab("Tags", "gameplayTags", "Gameplay tag registry.")
        };

        private PersistenceContainerCatalog _catalog;
        private SerializedObject _serializedCatalog;
        private Vector2 _tabScroll;
        private Vector2 _contentScroll;
        private int _selectedTab;

        #region Public API

        [MenuItem("CoCoFlow/Persistence/Catalog Editor")]
        public static void ShowWindow()
        {
            Open(null, 0);
        }

        [MenuItem("CoCoFlow/Persistence/Quest Editor")]
        public static void ShowQuestWindow()
        {
            Open(null, 6);
        }

        public static void Open(
            PersistenceContainerCatalog nextCatalog,
            int tabIndex = 0)
        {
            var window = GetWindow<PersistenceCatalogEditor>("Persistence Catalog");
            window.SetCatalog(nextCatalog != null
                ? nextCatalog
                : Selection.activeObject as PersistenceContainerCatalog);
            window._selectedTab = Mathf.Clamp(tabIndex, 0, Tabs.Length - 1);
            window.Show();
        }

        #endregion

        #region Internal Logic

        private void OnEnable()
        {
            if (_catalog == null)
            {
                SetCatalog(Selection.activeObject as PersistenceContainerCatalog);
            }
        }

        private void OnSelectionChange()
        {
            if (Selection.activeObject is PersistenceContainerCatalog selectedCatalog)
            {
                SetCatalog(selectedCatalog);
                Repaint();
            }
        }

        private void OnGUI()
        {
            DrawToolbar();

            if (_serializedCatalog == null)
            {
                DrawEmptyState();
                return;
            }

            _serializedCatalog.Update();

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawTabList();
                DrawSelectedTab();
            }

            _serializedCatalog.ApplyModifiedProperties();
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                var nextCatalog = (PersistenceContainerCatalog)EditorGUILayout.ObjectField(
                    _catalog,
                    typeof(PersistenceContainerCatalog),
                    false,
                    GUILayout.MinWidth(220f));
                if (nextCatalog != _catalog)
                {
                    SetCatalog(nextCatalog);
                }

                if (GUILayout.Button("Find First", EditorStyles.toolbarButton, GUILayout.Width(72f)))
                {
                    SetCatalog(FindFirstCatalog());
                }

                GUILayout.FlexibleSpace();

                using (new EditorGUI.DisabledScope(_catalog == null))
                {
                    if (GUILayout.Button("Validate", EditorStyles.toolbarButton, GUILayout.Width(72f)))
                    {
                        PersistenceContainerValidator.Validate(_catalog);
                    }
                }
            }
        }

        private void DrawEmptyState()
        {
            EditorGUILayout.Space(12f);
            EditorGUILayout.HelpBox(
                "Select a PersistenceContainerCatalog asset or create one from Assets/Create/CoCoFlow/Persistence/Container Catalog.",
                MessageType.Info);
        }

        private void DrawTabList()
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.Width(170f)))
            {
                _tabScroll = EditorGUILayout.BeginScrollView(_tabScroll, GUI.skin.box);
                for (int i = 0; i < Tabs.Length; i++)
                {
                    var tab = Tabs[i];
                    string count = string.IsNullOrEmpty(tab.PropertyName)
                        ? string.Empty
                        : $" ({GetArraySize(tab.PropertyName)})";
                    var content = new GUIContent(tab.Title + count, tab.Description);
                    var style = _selectedTab == i ? EditorStyles.miniButtonMid : EditorStyles.miniButton;
                    if (GUILayout.Toggle(_selectedTab == i, content, style))
                    {
                        _selectedTab = i;
                    }
                }

                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawSelectedTab()
        {
            using (new EditorGUILayout.VerticalScope())
            {
                var tab = Tabs[_selectedTab];
                EditorGUILayout.LabelField(tab.Title, EditorStyles.boldLabel);
                EditorGUILayout.HelpBox(tab.Description, MessageType.None);

                _contentScroll = EditorGUILayout.BeginScrollView(_contentScroll);
                if (string.IsNullOrEmpty(tab.PropertyName))
                {
                    DrawOverview();
                }
                else
                {
                    DrawPropertyTab(tab.PropertyName);
                }

                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawOverview()
        {
            DrawCount("Gameplay Tags", "gameplayTags");
            DrawCount("Items", "itemDefinitions");
            DrawCount("Container Definitions", "containerDefinitions");
            DrawCount("Container Templates", "containerTemplates");
            DrawCount("Rewards", "rewardDefinitions");
            DrawCount("Loot Tables", "lootTableDefinitions");
            DrawCount("Sequential Quests", "sequentialQuestDefinitions");
            DrawCount("Events", "eventDefinitions");
            DrawCount("Facts", "factDefinitions");

            EditorGUILayout.Space(8f);
            if (GUILayout.Button("Validate Catalog"))
            {
                PersistenceContainerValidator.Validate(_catalog);
            }
        }

        private void DrawCount(
            string label,
            string propertyName)
        {
            EditorGUILayout.LabelField(label, GetArraySize(propertyName).ToString());
        }

        private void DrawPropertyTab(string propertyName)
        {
            var property = _serializedCatalog.FindProperty(propertyName);
            if (property == null)
            {
                EditorGUILayout.HelpBox($"Missing property: {propertyName}", MessageType.Warning);
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(property.displayName, EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Add", GUILayout.Width(64f)))
                {
                    Undo.RecordObject(_catalog, $"Add {property.displayName}");
                    property.arraySize++;
                    _serializedCatalog.ApplyModifiedProperties();
                    EditorUtility.SetDirty(_catalog);
                }
            }

            EditorGUILayout.PropertyField(property, true);
        }

        private int GetArraySize(string propertyName)
        {
            if (_serializedCatalog == null) return 0;

            var property = _serializedCatalog.FindProperty(propertyName);
            return property != null && property.isArray ? property.arraySize : 0;
        }

        private void SetCatalog(PersistenceContainerCatalog nextCatalog)
        {
            _catalog = nextCatalog;
            _serializedCatalog = _catalog != null ? new SerializedObject(_catalog) : null;
        }

        private static PersistenceContainerCatalog FindFirstCatalog()
        {
            string[] guids = AssetDatabase.FindAssets("t:PersistenceContainerCatalog");
            if (guids.Length == 0) return null;

            string assetPath = AssetDatabase.GUIDToAssetPath(guids[0]);
            return AssetDatabase.LoadAssetAtPath<PersistenceContainerCatalog>(assetPath);
        }

        #endregion

        private readonly struct CatalogTab
        {
            public CatalogTab(
                string title,
                string propertyName,
                string description)
            {
                Title = title;
                PropertyName = propertyName;
                Description = description;
            }

            public string Title { get; }
            public string PropertyName { get; }
            public string Description { get; }
        }
    }

    [CustomEditor(typeof(PersistenceContainerCatalog))]
    public sealed class PersistenceContainerCatalogInspector : UnityEditor.Editor
    {
        #region Public API

        public override void OnInspectorGUI()
        {
            var catalog = (PersistenceContainerCatalog)target;

            EditorGUILayout.HelpBox(
                "Use the Catalog Editor window for tabbed editing of definitions, templates, rewards, quests, facts and tags.",
                MessageType.Info);

            if (GUILayout.Button("Open Catalog Editor"))
            {
                PersistenceCatalogEditor.Open(catalog);
            }

            if (GUILayout.Button("Validate Catalog"))
            {
                PersistenceContainerValidator.Validate(catalog);
            }

            EditorGUILayout.Space();
            DrawSummary(catalog);
        }

        #endregion

        #region Internal Logic

        private static void DrawSummary(PersistenceContainerCatalog catalog)
        {
            EditorGUILayout.LabelField("Summary", EditorStyles.boldLabel);
            DrawCount("Gameplay Tags", catalog.gameplayTags.Count);
            DrawCount("Items", catalog.itemDefinitions.Count);
            DrawCount("Container Definitions", catalog.containerDefinitions.Count);
            DrawCount("Container Templates", catalog.containerTemplates.Count);
            DrawCount("Rewards", catalog.rewardDefinitions.Count);
            DrawCount("Loot Tables", catalog.lootTableDefinitions.Count);
            DrawCount("Sequential Quests", catalog.sequentialQuestDefinitions.Count);
            DrawCount("Events", catalog.eventDefinitions.Count);
            DrawCount("Facts", catalog.factDefinitions.Count);
        }

        private static void DrawCount(
            string label,
            int count)
        {
            EditorGUILayout.LabelField(label, count.ToString());
        }

        #endregion
    }
}
