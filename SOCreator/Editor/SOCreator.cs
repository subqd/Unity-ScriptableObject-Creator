#if UNITY_EDITOR

using UnityEditor;
using UnityEngine;
using System.Linq;
using System.Collections.Generic;
using System;
using System.Reflection;
using System.IO;

/// <summary>
/// ScriptableObject Creator
/// Author: Subqd
/// Version : 1.0.0
/// Description: This script provides a window for creating ScriptableObjects with the CreateAssetMenu attribute.
/// Link : https://github.com/subqd/Unity-ScriptableObject-Creator
/// </summary>

public class ScriptableObjectCreator : EditorWindow
{
    [MenuItem("Tools/SOCreator")]
    public static void ShowWindow()
    {
        var window = GetWindow<ScriptableObjectCreator>("SOCreator");
        window.minSize = new Vector2(600, 400);
        window.Show();
    }

    private Vector2 scrollPosition;
    private Vector2 rightPanelScroll;
    private string searchString = "";
    private Dictionary<string, bool> categoryFoldoutStates = new Dictionary<string, bool>();
    private Dictionary<string, List<Type>> categoryMap = new Dictionary<string, List<Type>>();
    private bool showAll = false;
    private ScriptableObject currentAsset;
    private Editor assetEditor;
    private string savePath = "Assets/Resources"; //Default Save Path
    private string lastValidFolderPath = "Assets/Resources";
    private string assetName = "new ScriptableObject";//Default Save Name
    private bool focusNameField = false;

    //Splitter Parameter
    private Rect splitterRect;
    private bool isDraggingSplitter = false;
    private float splitViewPosition = 300f;
    private const float SPLITTER_WIDTH = 3f;
    private const float MIN_PANEL_WIDTH = 300f;

    private void OnEnable()
    {
        RefreshTypeList();
        UpdateSavePathFromSelection();
        EditorApplication.projectChanged += UpdateSavePathFromSelection;
        Selection.selectionChanged += UpdateSavePathFromSelection;
    }

    private void OnDisable()
    {
        DestroyImmediate(assetEditor);
        EditorApplication.projectChanged -= UpdateSavePathFromSelection;
        Selection.selectionChanged -= UpdateSavePathFromSelection;
    }

    private void OnFocus()
    {
        UpdateSavePathFromSelection();
    }

    private void OnSelectionChange()
    {
        UpdateSavePathFromSelection();
        Repaint();
    }

    //更新保存路径 
    //Update Save Path
    private void UpdateSavePathFromSelection()
    {
        // 获取Project窗口中选中的对象 - Get selected object in Project window
        UnityEngine.Object[] selectedObjects = Selection.GetFiltered(typeof(UnityEngine.Object), SelectionMode.Assets);

        if (selectedObjects.Length > 0)
        {
            string newPath = AssetDatabase.GetAssetPath(selectedObjects[0]);

            if (!string.IsNullOrEmpty(newPath))
            {
                if (Directory.Exists(newPath))
                {
                    savePath = newPath;
                    lastValidFolderPath = newPath;
                }
                else if (File.Exists(newPath))
                {
                    string directory = Path.GetDirectoryName(newPath);
                    if (!string.IsNullOrEmpty(directory))
                    {
                        savePath = directory;
                        lastValidFolderPath = directory;
                    }
                }
            }
        }

        // 确保路径有效，否则使用上次有效的文件夹路径
        // Ensure path is valid, otherwise use last valid folder path
        if (string.IsNullOrEmpty(savePath) || !savePath.StartsWith("Assets"))
        {
            savePath = lastValidFolderPath;
        }
        EnsureResourcesFolderExists();
    }


    // 确保Resources文件夹存在
    // Ensure Resources folder exists
    private void EnsureResourcesFolderExists()
    {
        if (savePath == "Assets/Resources" && !Directory.Exists(Application.dataPath + "/Resources"))
        {
            AssetDatabase.CreateFolder("Assets", "Resources");
            AssetDatabase.Refresh();
        }
    }
    //刷新类型列表
    //Refresh Type List
    private void RefreshTypeList()
    {
        categoryMap.Clear();
        var types = GetTypesWithCreateAssetMenuAttribute();

        foreach (var type in types)
        {
            var attr = type.GetCustomAttribute<CreateAssetMenuAttribute>();
            if (attr != null)
            {
                string menuPath = attr.menuName;
                if (string.IsNullOrEmpty(menuPath))
                {
                    menuPath = type.Name;
                }

                string[] pathParts = menuPath.Split('/');
                string category = "Other";

                if (pathParts.Length > 1)
                {
                    category = pathParts[0];
                    for (int i = 1; i < pathParts.Length - 1; i++)
                    {
                        category += "/" + pathParts[i];
                    }
                }

                if (!categoryMap.ContainsKey(category))
                {
                    categoryMap.Add(category, new List<Type>());
                    if (!categoryFoldoutStates.ContainsKey(category))
                    {
                        categoryFoldoutStates[category] = showAll || !string.IsNullOrEmpty(searchString);
                    }
                }

                categoryMap[category].Add(type);
            }
        }
    }

    private void OnGUI()
    {
        HandleSplitter();

        EditorGUILayout.Space(4f);

        EditorGUILayout.BeginHorizontal();
        {
            //左面板 / Left Panel
            EditorGUILayout.BeginVertical(GUILayout.Width(splitViewPosition));
            {
                DrawLeftPanel();
            }
            EditorGUILayout.EndVertical();

            //分割线 / Splitter
            DrawSplitter();

            EditorGUILayout.Space(5f);

            //右面面板 / Right Panel
            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            {
                DrawRightPanel();
            }
            EditorGUILayout.EndVertical();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(5f);
    }

    //左面板 / Left Panel
    //获取attribute上下文
    private void DrawLeftPanel()
    {
        EditorGUILayout.BeginVertical(GUILayout.Width(splitViewPosition));
        {
            //Search Panel
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            {
                string newSearch = EditorGUILayout.TextField("Search", searchString);
                if (newSearch != searchString)
                {
                    searchString = newSearch;
                    RefreshTypeList();
                }

                if (GUILayout.Button(showAll ? "Collapse All" : "Expand All", EditorStyles.miniButton, GUILayout.Width(80)))
                {
                    showAll = !showAll;
                    foreach (var key in categoryFoldoutStates.Keys.ToList())
                    {
                        categoryFoldoutStates[key] = showAll;
                    }
                }
            }
            EditorGUILayout.EndHorizontal();

            //Foldout Attribute List
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            foreach (var category in categoryMap.Keys.OrderBy(k => k))
            {
                bool hasVisibleItems = categoryMap[category].Any(t =>
                    string.IsNullOrEmpty(searchString) ||
                    t.Name.ToLower().Contains(searchString.ToLower()));

                if (!hasVisibleItems && !string.IsNullOrEmpty(searchString)) continue;

                EditorGUILayout.Space();
                bool foldoutState = categoryFoldoutStates.ContainsKey(category) ? categoryFoldoutStates[category] : false;
                categoryFoldoutStates[category] = EditorGUILayout.Foldout(foldoutState, category, true, EditorStyles.foldoutHeader);

                if (categoryFoldoutStates[category])
                {
                    EditorGUI.indentLevel++;
                    foreach (var type in categoryMap[category].OrderBy(t => t.Name))
                    {
                        var attr = type.GetCustomAttribute<CreateAssetMenuAttribute>();
                        string displayName = GetDisplayName(attr, type);

                        if (!string.IsNullOrEmpty(searchString))
                        {
                            if (!displayName.ToLower().Contains(searchString.ToLower()) &&
                                !type.Name.ToLower().Contains(searchString.ToLower()))
                            {
                                continue;
                            }
                        }

                        if (GUILayout.Button(displayName, EditorStyles.miniButton))
                        {
                            CreateAndShowScriptableObject(type);
                        }
                    }
                    EditorGUI.indentLevel--;
                }
            }

            EditorGUILayout.EndScrollView();
        }
        EditorGUILayout.EndVertical();
    }

    //右面面板 / Right Panel
    //inspector和控制面板
    private void DrawRightPanel()
    {
        EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
        {
            if (currentAsset != null)
            {
                GUILayout.Label("Inspector", EditorStyles.boldLabel);
                //Inspector Top Control Panel
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                {
                    //Path Area
                    EditorGUILayout.BeginHorizontal();
                    {
                        GUILayout.Label("Save Location:", GUILayout.Width(90));
                        EditorGUILayout.SelectableLabel(savePath, EditorStyles.textField, GUILayout.Height(EditorGUIUtility.singleLineHeight));

                        if (GUILayout.Button("Refresh", GUILayout.Width(70)))
                        {
                            UpdateSavePathFromSelection();
                        }
                    }
                    EditorGUILayout.EndHorizontal();

                    //Asset Name Area
                    EditorGUILayout.BeginHorizontal();
                    {
                        GUILayout.Label("Asset Name:", GUILayout.Width(90));

                        GUI.SetNextControlName("AssetNameField");
                        assetName = EditorGUILayout.TextField(assetName);

                        if (focusNameField)
                        {
                            EditorGUI.FocusTextInControl("AssetNameField");
                            focusNameField = false;
                        }

                        if (GUILayout.Button("Reset", GUILayout.Width(70)))
                        {
                            assetName = "New" + currentAsset.GetType().Name;
                            focusNameField = true;
                        }
                    }
                    EditorGUILayout.EndHorizontal();

                    //Save Button Area
                    EditorGUILayout.BeginHorizontal();
                    {
                        GUI.color = Color.green;

                        if (GUILayout.Button("Save", GUILayout.Width(60)))
                        {
                            string fileName = "New " + currentAsset.GetType().Name + ".asset";

                            if (assetName != "")
                                fileName = assetName + ".asset";

                            string path = AssetDatabase.GenerateUniqueAssetPath(savePath + "/" + fileName);
                            SaveAssetToPath(path);
                        }

                        if (GUILayout.Button("Save As", GUILayout.Width(60)))
                        {
                            string defaultName = "New " + currentAsset.GetType().Name + ".asset";
                            string path = EditorUtility.SaveFilePanelInProject(
                                "Save ScriptableObject",
                                defaultName,
                                "asset",
                                "Please enter a file name to save the ScriptableObject",
                                savePath);

                            if (!string.IsNullOrEmpty(path))
                            {
                                SaveAssetToPath(path);
                            }
                        }

                        GUI.color = Color.white;

                        EditorGUILayout.HelpBox("Click a folder in Project window to get path",MessageType.None);
                    }
                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.EndVertical();

                //Scroll Inspector Area
                rightPanelScroll = EditorGUILayout.BeginScrollView(rightPanelScroll);
                {
                    if (assetEditor == null && currentAsset != null)
                    {
                        assetEditor = Editor.CreateEditor(currentAsset);
                    }

                    assetEditor?.OnInspectorGUI();
                }
                EditorGUILayout.EndScrollView();
            }
            else
            {
                //Default Notice
                EditorGUILayout.HelpBox("Select a ScriptableObject type from left panel", MessageType.Info);
                EditorGUILayout.HelpBox("Click a folder in Project window to get path", MessageType.Info);
                EditorGUILayout.HelpBox($"Current save path: {savePath}", MessageType.None);
            }
        }
        EditorGUILayout.EndVertical();
    }

    //Save Function
    private void SaveAssetToPath(string path)
    {
        AssetDatabase.CreateAsset(currentAsset, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorUtility.FocusProjectWindow();
        Selection.activeObject = currentAsset;

        var savedAsset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
        DestroyImmediate(assetEditor);
        currentAsset = savedAsset;
        assetEditor = Editor.CreateEditor(currentAsset);

        Debug.Log("Saved: " + path);
    }

    //Splitter Draw
    private void DrawSplitter()
    {
        EditorGUI.DrawRect(new Rect(splitViewPosition, 0, SPLITTER_WIDTH, position.height),
            EditorGUIUtility.isProSkin ? new Color(0.11f, 0.11f, 0.11f) : new Color(0.51f, 0.51f, 0.51f));
    }

    //Moveable Splitter Function
    private void HandleSplitter()
    {
        splitterRect = new Rect(splitViewPosition, 0, SPLITTER_WIDTH, position.height);
        EditorGUIUtility.AddCursorRect(splitterRect, MouseCursor.ResizeHorizontal);

        if (Event.current.type == EventType.MouseDown && splitterRect.Contains(Event.current.mousePosition))
        {
            isDraggingSplitter = true;
            Event.current.Use();
        }

        if (isDraggingSplitter && Event.current.type == EventType.MouseDrag)
        {
            splitViewPosition = Mathf.Clamp(Event.current.mousePosition.x, MIN_PANEL_WIDTH, position.width - MIN_PANEL_WIDTH);
            Repaint();
            Event.current.Use();
        }

        if (Event.current.type == EventType.MouseUp)
        {
            isDraggingSplitter = false;
        }
    }

    //Create SO Function
    private void CreateAndShowScriptableObject(Type type)
    {
        if (currentAsset != null)
        {
            DestroyImmediate(currentAsset);
        }

        currentAsset = ScriptableObject.CreateInstance(type);

        if (assetEditor != null) DestroyImmediate(assetEditor);
        assetEditor = Editor.CreateEditor(currentAsset);
    }

    private string GetDisplayName(CreateAssetMenuAttribute attr, Type type)
    {
        if (attr == null || string.IsNullOrEmpty(attr.menuName))
            return type.Name;

        string[] parts = attr.menuName.Split('/');
        return parts[parts.Length - 1];
    }

    private static IEnumerable<Type> GetTypesWithCreateAssetMenuAttribute()
    {
        return from assembly in AppDomain.CurrentDomain.GetAssemblies()
               from type in assembly.GetTypes()
               where type.IsSubclassOf(typeof(ScriptableObject))
               let attributes = type.GetCustomAttributes(typeof(CreateAssetMenuAttribute), true)
               where attributes != null && attributes.Length > 0
               select type;
    }
}
#endif