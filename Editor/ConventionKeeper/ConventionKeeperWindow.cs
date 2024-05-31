using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using System.Linq;
using UnityEngine.Serialization;
using Object = UnityEngine.Object;

namespace ConventionKeeper
{
    internal static class RenameScopeExtensions
    {
        public static string GetDisplayName(this ConventionKeeperWindow.RenameScope scope) => scope switch
        {
            ConventionKeeperWindow.RenameScope.Selection => "Selection",
            ConventionKeeperWindow.RenameScope.Scene => "Scene",
            _ => scope.ToString()
        };
    }
    
    internal static class RenameModeExtensions
    {
        public static bool RequiresRegexValidation(this ConventionKeeperWindow.RenameMode mode) => 
            mode == ConventionKeeperWindow.RenameMode.RegexReplace;
        public static bool ShowsCaseSensitivityOption(this ConventionKeeperWindow.RenameMode mode) => 
            mode == ConventionKeeperWindow.RenameMode.RegexReplace;
        public static bool ShowsRenameSettings(this ConventionKeeperWindow.RenameMode mode) => 
            mode != ConventionKeeperWindow.RenameMode.Template;
    }

    public class ConventionKeeperWindow : EditorWindow
    {
        private const int MaxVisibleTargets = 50;
        private const int MaxPreviewItems = 100;
        private const int ScrollHeight = 150;
        private const int PreviewHeight = 200;
        private const int ButtonWidth = 50;
        private const int HelpButtonWidth = 20;
        private const int UndoButtonWidth = 80;
        private const int ScopeLabelWidth = 60;
        private const int MaxHistoryOperations = 10;
        private const string DuplicateSuffix = "_DUPLICATE";
        private const string IndexFormat = "00";
        private const string RegexHelpUrl = "https://docs.microsoft.com/en-us/dotnet/standard/base-types/regular-expression-language-quick-reference";
        
        internal enum RenameScope { Selection, Scene }
        internal enum RenameMode { SimpleReplace, RegexReplace, Template }

        [Serializable]
        private class RenameTemplate
        {
            public string Name;
            public string Prefix;
            public string Suffix;
            public string Search;
            public string Replace;
            public bool AddIndex;
            public bool UseRegex;
            
            public bool IsValid() => !string.IsNullOrWhiteSpace(Name);
            
            public void CopyFrom(RenameTemplate other)
            {
                if (other == null) return;
                
                Name = other.Name;
                Prefix = other.Prefix;
                Suffix = other.Suffix;
                Search = other.Search;
                Replace = other.Replace;
                AddIndex = other.AddIndex;
                UseRegex = other.UseRegex;
            }
        }

        private RenameScope _renameScope = RenameScope.Selection;
        private RenameMode _renameMode = RenameMode.SimpleReplace;

        private string _prefix = string.Empty;
        private string _suffix = string.Empty;
        private string _search = string.Empty;
        private string _replace = string.Empty;
        private bool _addIndex = false;
        private bool _useRegex = false;
        private bool _caseSensitive = true;

        private Vector2 _previewScroll;
        private Vector2 _selectionScroll;
        private Vector2 _historyScroll;

        private readonly List<Object> _targets = new List<Object>();
        private readonly List<string> _previewNames = new List<string>();
        private readonly List<string> _warnings = new List<string>();
        private readonly HashSet<string> _processedNames = new HashSet<string>();
        
        private GUIStyle _richTextStyle;
        private GUIStyle _cachedBoldStyle;
        private bool _isStylesInitialized;

        private readonly List<RenameTemplate> _templates = new List<RenameTemplate>();
        private int _selectedTemplate = -1;
        private string _newTemplateName = string.Empty;
        private bool _showTemplates = false;

        private bool _showAdvanced = false;
        private string _typeFilter = string.Empty;
        private int _displayedTargetsLimit = MaxVisibleTargets;
        private int _displayedPreviewLimit = MaxPreviewItems;
        private string _basePreviewColor = "white";
        private string _highlightPreviewColor = "#4FC3F7";
        
        private bool _showHistory = false;

        [MenuItem("Tools/Convention Keeper")]
        public static void ShowWindow()
        {
            GetWindow<ConventionKeeperWindow>("Convention Keeper");
        }

        private void OnEnable()
        {
            LoadSettings();
        }

        private void OnDisable()
        {
            SaveSettings();
        }

        private void OnGUI()
        {
            InitializeStyles();
            
            GUILayout.Label("Convention Keeper Tool", _cachedBoldStyle);
            
            DrawScopeSelection();
            DrawModeSelection();
            DrawRenameSettings();
            DrawTemplates();
            DrawTargetsList();
            DrawPreview();
            DrawAdvancedSettings();
            DrawOperationHistory();
        }

        private void InitializeStyles()
        {
            if (_isStylesInitialized) return;
            
            _richTextStyle = new GUIStyle(EditorStyles.label) { richText = true };
            _cachedBoldStyle = EditorStyles.boldLabel;
            _isStylesInitialized = true;
        }

        private void DrawScopeSelection()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Scope:", GUILayout.Width(ScopeLabelWidth));
            _renameScope = (RenameScope)EditorGUILayout.EnumPopup(_renameScope);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawModeSelection()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Mode:", GUILayout.Width(ScopeLabelWidth));
            _renameMode = (RenameMode)EditorGUILayout.EnumPopup(_renameMode);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawRenameSettings()
        {
            EditorGUILayout.Space();
            
            if (!_renameMode.ShowsRenameSettings())
                return;

            _prefix = EditorGUILayout.TextField("Prefix", _prefix);
            _suffix = EditorGUILayout.TextField("Suffix", _suffix);
            
            EditorGUILayout.BeginHorizontal();
            _search = EditorGUILayout.TextField("Search", _search);
            if (_renameMode == RenameMode.RegexReplace)
            {
                if (GUILayout.Button("?", GUILayout.Width(HelpButtonWidth)))
                {
                    Application.OpenURL(RegexHelpUrl);
                }
            }
            EditorGUILayout.EndHorizontal();
            
            _replace = EditorGUILayout.TextField("Replace", _replace);
            
            EditorGUILayout.BeginHorizontal();
            _addIndex = EditorGUILayout.Toggle("Add Index", _addIndex);
            if (_renameMode.ShowsCaseSensitivityOption())
            {
                _caseSensitive = EditorGUILayout.Toggle("Case Sensitive", _caseSensitive);
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawTemplates()
        {
            EditorGUILayout.Space();
            _showTemplates = EditorGUILayout.Foldout(_showTemplates, "Templates");
            
            if (_showTemplates)
            {
                EditorGUI.indentLevel++;
                
                if (_templates.Count > 0)
                {
                    EditorGUILayout.BeginHorizontal();
                    _selectedTemplate = EditorGUILayout.Popup("Load Template", _selectedTemplate, 
                        _templates.Select(t => t.Name).ToArray());
                    
                    if (GUILayout.Button("Load", GUILayout.Width(ButtonWidth)) && IsValidTemplateSelected())
                    {
                        LoadTemplate(_templates[_selectedTemplate]);
                    }
                    
                    if (GUILayout.Button("Delete", GUILayout.Width(ButtonWidth)) && IsValidTemplateSelected())
                    {
                        _templates.RemoveAt(_selectedTemplate);
                        _selectedTemplate = -1;
                    }
                    EditorGUILayout.EndHorizontal();
                }
                
                EditorGUILayout.BeginHorizontal();
                _newTemplateName = EditorGUILayout.TextField("New Template", _newTemplateName);
                if (GUILayout.Button("Save", GUILayout.Width(ButtonWidth)) && IsValidNewTemplateName())
                {
                    SaveCurrentAsTemplate(_newTemplateName);
                    _newTemplateName = string.Empty;
                }
                EditorGUILayout.EndHorizontal();
                
                EditorGUI.indentLevel--;
            }
        }

        
        private void DrawTargetsList()
        {
            List<Object> currentTargets = GetCurrentTargets();
            
            if (currentTargets.Count > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField($"Target objects ({currentTargets.Count}):", _cachedBoldStyle);

                _selectionScroll = EditorGUILayout.BeginScrollView(_selectionScroll, GUILayout.Height(ScrollHeight));
                
                foreach (Object target in currentTargets.Take(_displayedTargetsLimit))
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.ObjectField(target, typeof(Object), true);
                    EditorGUILayout.LabelField(target.name);
                    EditorGUILayout.EndHorizontal();
                }
                
                if (currentTargets.Count > _displayedTargetsLimit)
                {
                    EditorGUILayout.LabelField($"... and {currentTargets.Count - _displayedTargetsLimit} more objects");
                }
                
                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawPreview()
        {
            EditorGUILayout.Space();
            
            if (GUILayout.Button("Generate Preview"))
            {
                GeneratePreview();
            }

            if (_previewNames.Count > 0)
            {
                EditorGUILayout.Space();
                
                if (_warnings.Count > 0)
                {
                    EditorGUILayout.HelpBox($"Warnings: {string.Join(", ", _warnings)}", MessageType.Warning);
                }
                
                EditorGUILayout.LabelField($"Preview ({_previewNames.Count} objects):", _cachedBoldStyle);

                _previewScroll = EditorGUILayout.BeginScrollView(_previewScroll, GUILayout.Height(PreviewHeight));
                for (int i = 0; i < _targets.Count && i < _displayedPreviewLimit; i++)
                {
                    EditorGUILayout.BeginHorizontal();
                    
                    string currentDisplayName = RenameUtility.GetDisplayName(_targets[i]);
                    
                    GUILayout.Label(
                        $"{currentDisplayName} -> "
                    );

                    // if (_replace == string.Empty) // test
                    //     currentDisplayName = currentDisplayName.Replace(_search, "");

                    DrawMatchColored(_previewNames[i], currentDisplayName);
                    
                    EditorGUILayout.EndHorizontal();
                }
                
                if (_targets.Count > _displayedPreviewLimit)
                {
                    EditorGUILayout.LabelField($"... and {_targets.Count - _displayedPreviewLimit} more objects");
                }
                
                EditorGUILayout.EndScrollView();

                EditorGUILayout.Space();
                
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Rename All"))
                {
                    RenameAll();
                }
                
                if (GUILayout.Button("Clear Preview"))
                {
                    ClearPreview();
                }
                EditorGUILayout.EndHorizontal();
            }
        }
        
        private void DrawMatchColored(string fullText, string match)
        {
            int matchIndex = fullText.IndexOf(match, StringComparison.Ordinal);
            
            if (matchIndex >= 0)
            {
                string beforeMatch = fullText[..matchIndex];
                string afterMatch = fullText[(matchIndex + match.Length)..];
                string coloredText = $"<color={_highlightPreviewColor}>{beforeMatch}</color><color={_basePreviewColor}>{match}</color><color={_highlightPreviewColor}>{afterMatch}</color>";
                
                GUILayout.Label(coloredText, _richTextStyle);
            }
            else
            {
                string coloredText = $"<color={_highlightPreviewColor}>{fullText}</color>";
                
                GUILayout.Label(coloredText, _richTextStyle);
            }
        }


        private void DrawAdvancedSettings()
        {
            EditorGUILayout.Space();
            _showAdvanced = EditorGUILayout.Foldout(_showAdvanced, "Advanced Settings");
            
            if (_showAdvanced)
            {
                EditorGUI.indentLevel++;
                
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Type Filter (e.g., 'Texture')");
                _typeFilter = EditorGUILayout.TextField(_typeFilter);
                EditorGUILayout.EndHorizontal();
                
                EditorGUI.indentLevel--;
            }
        }

        private void DrawOperationHistory()
        {
            EditorGUILayout.Space();
            
            EditorGUILayout.BeginHorizontal();
            _showHistory = EditorGUILayout.Foldout(_showHistory, "Operation History");
            
            GUI.enabled = OperationHistory.HasUndoableOperations();
            if (GUILayout.Button("Undo Last", GUILayout.Width(UndoButtonWidth)))
            {
                OperationHistory.UndoLastOperation();
            }
            GUI.enabled = true;
            
            EditorGUILayout.EndHorizontal();
            
            if (_showHistory)
            {
                EditorGUI.indentLevel++;
                
                List<OperationHistory.OperationRecord> recentOperations = OperationHistory.GetRecentOperations(MaxHistoryOperations);
                
                if (recentOperations.Count == 0)
                {
                    EditorGUILayout.LabelField("No recorded operations", EditorStyles.centeredGreyMiniLabel);
                }
                else
                {
                    if (OperationHistory.HasUndoableOperations())
                    {
                        EditorGUILayout.HelpBox($"Last operation: {OperationHistory.GetLastOperationDescription()}", 
                            MessageType.Info);
                    }
                    
                    _historyScroll = EditorGUILayout.BeginScrollView(_historyScroll, GUILayout.Height(ScrollHeight));
                    
                    foreach (OperationHistory.OperationRecord operation in recentOperations)
                    {
                        EditorGUILayout.BeginVertical("box");
                        
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField($"[{operation.Timestamp:HH:mm:ss}] {operation.OperationType}", 
                            EditorStyles.boldLabel);
                        
                        if (GUILayout.Button("Undo", GUILayout.Width(ButtonWidth)))
                        {
                            OperationHistory.UndoOperation(operation);
                        }
                        
                        EditorGUILayout.EndHorizontal();
                        
                        EditorGUILayout.LabelField($"Description: {operation.Description}", EditorStyles.wordWrappedLabel);
                        EditorGUILayout.LabelField($"Objects: {operation.RenamedObjects.Count}");
                        
                        EditorGUILayout.EndVertical();
                    }
                    
                    EditorGUILayout.EndScrollView();
                    
                    EditorGUILayout.BeginHorizontal();
                    if (GUILayout.Button("Export History"))
                    {
                        ExportOperationHistory();
                    }
                    
                    if (GUILayout.Button("Clear History"))
                    {
                        if (EditorUtility.DisplayDialog("Clear History", 
                            "Are you sure you want to clear your operations history?", "Yes", "Cancel"))
                        {
                            OperationHistory.ClearHistory();
                        }
                    }
                    EditorGUILayout.EndHorizontal();
                }
                
                EditorGUI.indentLevel--;
            }
        }

        private List<Object> GetCurrentTargets()
        {
            List<Object> targets = new List<Object>();
            
            switch (_renameScope)
            {
                case RenameScope.Selection:
                    targets.AddRange(Selection.objects);
                    break;
                    
                case RenameScope.Scene:
                    targets.AddRange(FindObjectsOfType<GameObject>());
                    break;
            }
            
            if (!string.IsNullOrEmpty(_typeFilter))
            {
                targets = targets.Where(t => t.GetType().Name.Contains(_typeFilter)).ToList();
            }
            
            return targets;
        }

        private void GeneratePreview()
        {
            ClearPreview();

            List<Object> currentTargets = GetCurrentTargets();
            if (currentTargets.Count == 0)
            {
                Debug.LogWarning("Convention Keeper: No targets found for preview generation");
                return;
            }

            _targets.Capacity = Math.Max(_targets.Capacity, currentTargets.Count);
            _previewNames.Capacity = Math.Max(_previewNames.Capacity, currentTargets.Count);
            
            _targets.AddRange(currentTargets);

            List<string> warningMessages = new List<string>();

            for (int i = 0; i < _targets.Count; i++)
            {
                try
                {
                    string originalName = RenameUtility.GetDisplayName(_targets[i]);
                    string processedName = ProcessName(originalName, i);
                    string finalName = ValidatePreviewName(processedName, _targets[i]);
                    
                    _previewNames.Add(finalName);
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Convention Keeper: Error processing target {i}: {ex.Message}");
                    _previewNames.Add($"ERROR_{i}");
                    warningMessages.Add($"Processing error for object {i}");
                }
            }

            if (warningMessages.Count > 0)
            {
                _warnings.AddRange(warningMessages);
            }

            Debug.Log($"Convention Keeper: Generated preview for {_targets.Count} objects with {_warnings.Count} warnings");
        }

        private string ProcessName(string originalName, int index)
        {
            if (string.IsNullOrEmpty(originalName))
            {
                _warnings.Add("Empty or null original name encountered");
                return $"unnamed_{index + 1:00}";
            }

            string resultName = originalName;

            if (!string.IsNullOrEmpty(_search))
            {
                if (_renameMode.RequiresRegexValidation())
                {
                    if (TryProcessRegexRename(resultName, out string regexResult, out string error))
                    {
                        resultName = regexResult;
                    }
                    else
                    {
                        _warnings.Add($"Regex error for '{originalName}': {error}");
                        resultName = originalName;
                    }
                }
                else
                {
                    resultName = ProcessSimpleRename(resultName);
                }
            }

            resultName = _prefix + resultName + _suffix;

            if (_addIndex)
                resultName += "_" + (index + 1).ToString(IndexFormat);

            return resultName;
        }
        
        private void RenameAll()
        {
            if (_targets.Count == 0) return;

            List<string> oldNames = _targets.Select(t => RenameUtility.GetDisplayName(t)).ToList();
            List<string> newNames = new List<string>(_previewNames);

            Undo.RecordObjects(_targets.ToArray(), "Convention Keeper: Rename Group");

            int renamedCount = 0;
            List<Object> actuallyRenamed = new List<Object>();
            List<string> actualOldNames = new List<string>();
            List<string> actualNewNames = new List<string>();

            for (int i = 0; i < _targets.Count; i++)
            {
                string currentDisplayName = RenameUtility.GetDisplayName(_targets[i]);
                if (currentDisplayName != _previewNames[i])
                {
                    actuallyRenamed.Add(_targets[i]);
                    actualOldNames.Add(currentDisplayName);
                    actualNewNames.Add(_previewNames[i]);
                    
                    if (RenameUtility.RenameObject(_targets[i], _previewNames[i]))
                    {
                        renamedCount++;
                    }
                }
            }

            if (actuallyRenamed.Count > 0)
            {
                string operationType = $"Renamed ({_renameScope})";
                string description = $"Group renamed: {GetOperationDescription()}";
                
                OperationHistory.RecordRenameOperation(operationType, description, 
                    actuallyRenamed, actualOldNames, actualNewNames);
            }

            Debug.Log($"Convention Keeper: Renamed {renamedCount} objects.");
            ClearPreview();
        }

        private void ClearPreview()
        {
            _targets.Clear();
            _previewNames.Clear();
            _warnings.Clear();
            _processedNames.Clear();
        }

        private void LoadTemplate(RenameTemplate template)
        {
            if (template?.IsValid() != true) return;
            
            _prefix = template.Prefix ?? string.Empty;
            _suffix = template.Suffix ?? string.Empty;
            _search = template.Search ?? string.Empty;
            _replace = template.Replace ?? string.Empty;
            _addIndex = template.AddIndex;
            _renameMode = template.UseRegex ? RenameMode.RegexReplace : RenameMode.SimpleReplace;
        }

        private void SaveCurrentAsTemplate(string templateName)
        {
            if (!IsValidNewTemplateName()) return;
            
            RenameTemplate template = new RenameTemplate
            {
                Name = templateName.Trim(),
                Prefix = _prefix,
                Suffix = _suffix,
                Search = _search,
                Replace = _replace,
                AddIndex = _addIndex,
                UseRegex = _renameMode.RequiresRegexValidation()
            };
            
            _templates.Add(template);
        }
        
        private void LoadSettings()
        {
            try
            {
                _prefix = EditorPrefs.GetString("ConventionKeeper.Prefix", string.Empty);
                _suffix = EditorPrefs.GetString("ConventionKeeper.Suffix", string.Empty);
                _search = EditorPrefs.GetString("ConventionKeeper.Search", string.Empty);
                _replace = EditorPrefs.GetString("ConventionKeeper.Replace", string.Empty);
                _addIndex = EditorPrefs.GetBool("ConventionKeeper.AddIndex", false);
                _useRegex = EditorPrefs.GetBool("ConventionKeeper.UseRegex", false);
                _caseSensitive = EditorPrefs.GetBool("ConventionKeeper.CaseSensitive", true);
                
                _displayedTargetsLimit = EditorPrefs.GetInt("ConventionKeeper.TargetsLimit", MaxVisibleTargets);
                _displayedPreviewLimit = EditorPrefs.GetInt("ConventionKeeper.PreviewLimit", MaxPreviewItems);
                
                _displayedTargetsLimit = Mathf.Clamp(_displayedTargetsLimit, 10, 1000);
                _displayedPreviewLimit = Mathf.Clamp(_displayedPreviewLimit, 10, 1000);
                
                LoadTemplatesFromPrefs();
            }
            catch (Exception ex)
            {
                Debug.LogError($"Convention Keeper: Failed to load settings - {ex.Message}");
                ResetToDefaultSettings();
            }
        }
        
        private void LoadTemplatesFromPrefs()
        {
            string templatesJson = EditorPrefs.GetString("ConventionKeeper.Templates", string.Empty);
            
            if (string.IsNullOrEmpty(templatesJson))
                return;
                
            try
            {
                List<RenameTemplate> loadedTemplates = JsonUtility.FromJson<List<RenameTemplate>>(templatesJson);
                if (loadedTemplates?.Count > 0)
                {
                    _templates.Clear();
                    
                    foreach (RenameTemplate template in loadedTemplates)
                    {
                        if (template?.IsValid() == true)
                        {
                            _templates.Add(template);
                        }
                        else
                        {
                            Debug.LogWarning($"Convention Keeper: Skipped invalid template: {template?.Name ?? "null"}");
                        }
                    }
                    
                    Debug.Log($"Convention Keeper: Loaded {_templates.Count} valid templates");
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Convention Keeper: Failed to load templates - {ex.Message}. Using default template set.");
                _templates.Clear();
            }
        }
        
        private void ResetToDefaultSettings()
        {
            _prefix = string.Empty;
            _suffix = string.Empty;
            _search = string.Empty;
            _replace = string.Empty;
            _addIndex = false;
            _useRegex = false;
            _caseSensitive = true;
            _displayedTargetsLimit = MaxVisibleTargets;
            _displayedPreviewLimit = MaxPreviewItems;
            _templates.Clear();
        }

        private void SaveSettings()
        {
            try
            {
                EditorPrefs.SetString("ConventionKeeper.Prefix", _prefix ?? string.Empty);
                EditorPrefs.SetString("ConventionKeeper.Suffix", _suffix ?? string.Empty);
                EditorPrefs.SetString("ConventionKeeper.Search", _search ?? string.Empty);
                EditorPrefs.SetString("ConventionKeeper.Replace", _replace ?? string.Empty);
                EditorPrefs.SetBool("ConventionKeeper.AddIndex", _addIndex);
                EditorPrefs.SetBool("ConventionKeeper.UseRegex", _useRegex);
                EditorPrefs.SetBool("ConventionKeeper.CaseSensitive", _caseSensitive);
                
                EditorPrefs.SetInt("ConventionKeeper.TargetsLimit", _displayedTargetsLimit);
                EditorPrefs.SetInt("ConventionKeeper.PreviewLimit", _displayedPreviewLimit);
                
                SaveTemplatesToPrefs();
            }
            catch (Exception ex)
            {
                Debug.LogError($"Convention Keeper: Failed to save settings - {ex.Message}");
            }
        }
        
        private void SaveTemplatesToPrefs()
        {
            try
            {
                if (_templates?.Count > 0)
                {
                    List<RenameTemplate> validTemplates = _templates.Where(t => t?.IsValid() == true).ToList();
                    
                    if (validTemplates.Count > 0)
                    {
                        string templatesJson = JsonUtility.ToJson(validTemplates);
                        EditorPrefs.SetString("ConventionKeeper.Templates", templatesJson);
                        Debug.Log($"Convention Keeper: Saved {validTemplates.Count} templates");
                    }
                    else
                    {
                        EditorPrefs.DeleteKey("ConventionKeeper.Templates");
                    }
                }
                else
                {
                    EditorPrefs.DeleteKey("ConventionKeeper.Templates");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Convention Keeper: Failed to save templates - {ex.Message}");
            }
        }

        private string GetOperationDescription()
        {
            List<string> parts = new List<string>();
            
            if (!string.IsNullOrEmpty(_prefix)) parts.Add($"prefix='{_prefix}'");
            if (!string.IsNullOrEmpty(_suffix)) parts.Add($"suffix='{_suffix}'");
            if (!string.IsNullOrEmpty(_search)) parts.Add($"search='{_search}'->'{_replace}'");
            if (_addIndex) parts.Add("add_index");
            
            return parts.Count > 0 ? string.Join(", ", parts) : "basic_rename";
        }

        private void ExportOperationHistory()
        {
            string path = EditorUtility.SaveFilePanel("Export Operation History", "", "convention_keeper_history.txt", "txt");
            if (!string.IsNullOrEmpty(path))
            {
                OperationHistory.ExportHistory(path);
                Debug.Log($"Convention Keeper: Operation History exported to {path}");
            }
        }

        private bool IsValidTemplateSelected() => _selectedTemplate >= 0 && _selectedTemplate < _templates.Count;
        
        private bool IsValidNewTemplateName() => !string.IsNullOrWhiteSpace(_newTemplateName) && 
                                                !_templates.Any(t => t.Name.Equals(_newTemplateName, StringComparison.OrdinalIgnoreCase));

        private bool TryProcessRegexRename(string input, out string result, out string error)
        {
            result = input;
            error = null;
            
            if (string.IsNullOrEmpty(_search))
            {
                error = "Search pattern cannot be empty";
                return false;
            }
            
            try
            {
                RegexOptions options = _caseSensitive ? RegexOptions.None : RegexOptions.IgnoreCase;
                
                Regex regex = new Regex(_search, options);
                result = regex.Replace(input, _replace ?? string.Empty);
                return true;
            }
            catch (ArgumentException ex)
            {
                error = $"Invalid regex pattern: {ex.Message}";
                return false;
            }
            catch (Exception ex)
            {
                error = $"Regex processing error: {ex.Message}";
                Debug.LogError($"Convention Keeper: Unexpected regex error - {ex}");
                return false;
            }
        }

        private string ProcessSimpleRename(string input)
        {
            if (string.IsNullOrEmpty(_search)) return input;
            
            StringComparison comparison = _caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            
            if (input.IndexOf(_search, comparison) < 0) return input;
            
            return _caseSensitive 
                ? input.Replace(_search, _replace) 
                : Regex.Replace(input, Regex.Escape(_search), _replace, RegexOptions.IgnoreCase);
        }

        private string ValidatePreviewName(string resultName, Object target)
        {
            bool isAsset = RenameUtility.IsAsset(target);
    
            if (!RenameUtility.ValidateName(resultName, isAsset))
            {
                _warnings.Add($"Invalid characters in name: {resultName}");
        
                string sanitized = RenameUtility.SanitizeName(resultName, isAsset);
                if (sanitized != resultName)
                {
                    _warnings.Add($"Suggested fix: {sanitized}");
                }
            }

            if (_processedNames.Contains(resultName))
            {
                _warnings.Add($"Duplicate name: {resultName}");
                string finalName = resultName + DuplicateSuffix;
                _processedNames.Add(finalName);
                return finalName;
            }
    
            _processedNames.Add(resultName);
            return resultName;
        }
    }
}