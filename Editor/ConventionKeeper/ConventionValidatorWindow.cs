using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ConventionKeeper
{
    public class ConventionValidatorWindow : EditorWindow
    {
        private enum ValidationScope { Selection, Scene, Project }
        
        private ValidationScope _validationScope = ValidationScope.Selection;
        private Dictionary<Object, List<string>> _validationResults = new Dictionary<Object, List<string>>();
        private Vector2 _scrollPosition;
        private Vector2 _rulesScrollPosition;
        
        private bool _showRulesSettings = false;
        private List<NamingValidator.NamingRule> _customRules;
        
        [MenuItem("Tools/Convention Keeper/Validate Naming Conventions")]
        public static void ShowWindow()
        {
            GetWindow<ConventionValidatorWindow>("Convention Validator");
        }

        private void OnEnable()
        {
            _customRules = NamingValidator.GetDefaultRules();
            LoadRulesSettings();
        }

        private void OnDisable()
        {
            SaveRulesSettings();
        }

        private void OnGUI()
        {
            GUILayout.Label("Convention Validator", EditorStyles.boldLabel);
            
            DrawValidationControls();
            DrawRulesSettings();
            DrawValidationResults();
        }

        private void DrawValidationControls()
        {
            EditorGUILayout.Space();
            
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Scope:", GUILayout.Width(60));
            _validationScope = (ValidationScope)EditorGUILayout.EnumPopup(_validationScope);
            
            if (GUILayout.Button("Validate", GUILayout.Width(100)))
            {
                RunValidation();
            }
            EditorGUILayout.EndHorizontal();
            
            if (_validationResults.Count > 0)
            {
                int totalViolations = _validationResults.Values.Sum(list => list.Count);
                EditorGUILayout.HelpBox($"Found {totalViolations} naming violations in {_validationResults.Count} objects.", 
                    MessageType.Warning);
            }
        }

        private void DrawRulesSettings()
        {
            EditorGUILayout.Space();
            _showRulesSettings = EditorGUILayout.Foldout(_showRulesSettings, "Naming Rules Settings");
            
            if (_showRulesSettings)
            {
                EditorGUI.indentLevel++;
                
                _rulesScrollPosition = EditorGUILayout.BeginScrollView(_rulesScrollPosition, GUILayout.Height(200));
                
                for (int i = 0; i < _customRules.Count; i++)
                {
                    DrawRuleEditor(i);
                }
                
                EditorGUILayout.EndScrollView();
                
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Add Rule"))
                {
                    _customRules.Add(new NamingValidator.NamingRule
                    {
                        RuleName = "New Rule",
                        Pattern = "",
                        ObjectType = "All",
                        IsRegex = false,
                        ErrorMessage = "Custom rule violation",
                        IsEnabled = true
                    });
                }
                
                if (GUILayout.Button("Reset to Defaults"))
                {
                    _customRules = NamingValidator.GetDefaultRules();
                }
                EditorGUILayout.EndHorizontal();
                
                EditorGUI.indentLevel--;
            }
        }

        private void DrawRuleEditor(int index)
        {
            NamingValidator.NamingRule rule = _customRules[index];
            
            EditorGUILayout.BeginVertical("box");
            
            EditorGUILayout.BeginHorizontal();
            rule.IsEnabled = EditorGUILayout.Toggle(rule.IsEnabled, GUILayout.Width(20));
            rule.RuleName = EditorGUILayout.TextField("Rule Name", rule.RuleName);
            
            if (GUILayout.Button("X", GUILayout.Width(20)))
            {
                _customRules.RemoveAt(index);
                return;
            }
            EditorGUILayout.EndHorizontal();
            
            rule.ObjectType = EditorGUILayout.TextField("Object Type", rule.ObjectType);
            rule.Pattern = EditorGUILayout.TextField("Pattern", rule.Pattern);
            rule.IsRegex = EditorGUILayout.Toggle("Is Regex", rule.IsRegex);
            rule.ErrorMessage = EditorGUILayout.TextField("Error Message", rule.ErrorMessage);
            
            EditorGUILayout.EndVertical();
        }

        private void DrawValidationResults()
        {
            if (_validationResults.Count == 0) return;
            
            EditorGUILayout.Space();
            EditorGUILayout.LabelField($"Validation Results ({_validationResults.Count} objects with issues):", 
                EditorStyles.boldLabel);
            
            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            
            foreach (KeyValuePair<Object, List<string>> kvp in _validationResults)
            {
                Object obj = kvp.Key;
                List<string> violations = kvp.Value;
                
                if (obj == null) continue;
                
                EditorGUILayout.BeginVertical("box");
                
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.ObjectField(obj, typeof(Object), true);
                
                if (GUILayout.Button("Suggest Fix", GUILayout.Width(100)))
                {
                    ShowFixSuggestion(obj);
                }
                
                if (GUILayout.Button("Select", GUILayout.Width(60)))
                {
                    Selection.activeObject = obj;
                    EditorGUIUtility.PingObject(obj);
                }
                EditorGUILayout.EndHorizontal();
                
                EditorGUI.indentLevel++;
                foreach (string violation in violations)
                {
                    EditorGUILayout.LabelField("• " + violation, EditorStyles.wordWrappedLabel);
                }
                EditorGUI.indentLevel--;
                
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space();
            }
            
            EditorGUILayout.EndScrollView();
            
            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();
            
            if (GUILayout.Button("Clear Results"))
            {
                _validationResults.Clear();
            }
            
            if (GUILayout.Button("Export Report"))
            {
                ExportValidationReport();
            }
            
            if (GUILayout.Button("Fix All (Auto)"))
            {
                AutoFixAllViolations();
            }
            
            EditorGUILayout.EndHorizontal();
        }

        private void RunValidation()
        {
            _validationResults.Clear();
            
            switch (_validationScope)
            {
                case ValidationScope.Selection:
                    _validationResults = NamingValidator.ValidateSelection();
                    break;
                case ValidationScope.Scene:
                    _validationResults = NamingValidator.ValidateScene();
                    break;
                case ValidationScope.Project:
                    _validationResults = NamingValidator.ValidateProject();
                    break;
            }
            
            Debug.Log($"Convention Validator: Found {_validationResults.Count} objects with naming violations.");
        }

        private void ShowFixSuggestion(Object obj)
        {
            string currentDisplayName = RenameUtility.GetDisplayName(obj);
            string suggested = NamingValidator.GenerateSuggestedName(currentDisplayName, obj.GetType().Name);
            string oldName = currentDisplayName;
            
            bool result = EditorUtility.DisplayDialog(
                "Fix Suggestion",
                $"Current name: {obj.name}\nSuggested name: {suggested}\n\nApply this fix?",
                "Apply",
                "Cancel"
            );
            
            if (result)
            {
                Undo.RecordObject(obj, "Convention Keeper: Fix Naming");
                
                if (RenameUtility.RenameObject(obj, suggested))
                {
                    OperationHistory.RecordRenameOperation(
                        "Single Fix", 
                        $"Naming fix: {oldName} -> {suggested}",
                        new List<Object> { obj },
                        new List<string> { oldName },
                        new List<string> { suggested }
                    );
                    
                    _validationResults.Remove(obj);
                    
                    if (RenameUtility.IsAsset(obj))
                    {
                        RenameUtility.RefreshProjectWindow();
                    }
                    
                    Debug.Log($"Convention Keeper: Renamed '{oldName}' to '{suggested}'");
                }
                else
                {
                    Debug.LogError($"Convention Keeper: Failed to rename '{oldName}' to '{suggested}'");
                }
            }
        }

        private void AutoFixAllViolations()
        {
            if (_validationResults.Count == 0) return;
            
            bool result = EditorUtility.DisplayDialog(
                "Auto Fix All",
                $"This will automatically rename {_validationResults.Count} objects to fix naming violations.\n\nThis action will be recorded in operation history for undo. Continue?",
                "Yes, Fix All",
                "Cancel"
            );
            
            if (!result) return;
            
            List<Object> objectsToFix = _validationResults.Keys.ToList();
            Undo.RecordObjects(objectsToFix.ToArray(), "Convention Keeper: Auto Fix All");
            
            List<Object> actuallyFixed = new List<Object>();
            List<string> oldNames = new List<string>();
            List<string> newNames = new List<string>();
            
            int fixedCount = 0;
            bool hasAssets = false;
            
            foreach (Object obj in objectsToFix)
            {
                string currentDisplayName = RenameUtility.GetDisplayName(obj);
                string oldName = currentDisplayName;
                string suggested = NamingValidator.GenerateSuggestedName(currentDisplayName, obj.GetType().Name);
                
                if (oldName != suggested)
                {
                    if (RenameUtility.RenameObject(obj, suggested))
                    {
                        actuallyFixed.Add(obj);
                        oldNames.Add(oldName);
                        newNames.Add(suggested);
                        fixedCount++;
                        
                        if (RenameUtility.IsAsset(obj))
                        {
                            hasAssets = true;
                        }
                    }
                }
            }
            
            if (actuallyFixed.Count > 0)
            {
                OperationHistory.RecordRenameOperation(
                    "Auto Fix All",
                    $"Automatic fix of {actuallyFixed.Count} naming violations",
                    actuallyFixed,
                    oldNames,
                    newNames
                );
            }
            
            if (hasAssets)
            {
                RenameUtility.RefreshProjectWindow();
            }
            
            _validationResults.Clear();
            Debug.Log($"Convention Keeper: Auto-fixed {fixedCount} naming violations.");
        }

        private void ExportValidationReport()
        {
            string path = EditorUtility.SaveFilePanel("Export Validation Report", "", "naming_report.txt", "txt");
            if (string.IsNullOrEmpty(path)) return;
            
            System.Text.StringBuilder report = new System.Text.StringBuilder();
            report.AppendLine("Naming Convention Validation Report");
            report.AppendLine($"Generated: {System.DateTime.Now}");
            report.AppendLine($"Scope: {_validationScope}");
            report.AppendLine($"Total objects with violations: {_validationResults.Count}");
            report.AppendLine();
            
            foreach (KeyValuePair<Object, List<string>> kvp in _validationResults)
            {
                Object obj = kvp.Key;
                List<string> violations = kvp.Value;
                
                report.AppendLine($"Object: {obj.name} ({obj.GetType().Name})");
                foreach (string violation in violations)
                {
                    report.AppendLine($"  - {violation}");
                }
                report.AppendLine();
            }
            
            System.IO.File.WriteAllText(path, report.ToString());
            Debug.Log($"Convention Keeper: Validation report exported to {path}");
        }

        private void LoadRulesSettings()
        {
            string rulesJson = EditorPrefs.GetString("ConventionValidator.CustomRules", "");
            if (!string.IsNullOrEmpty(rulesJson))
            {
                try
                {
                    // More complex serialization can be implemented here
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"Failed to load custom rules: {e.Message}");
                }
            }
        }

        private void SaveRulesSettings()
        {
            try
            {
                // Here you can implement the serialization of custom rules
                // string rulesJson = JsonUtility.ToJson(_customRules);
                // EditorPrefs.SetString("ConventionValidator.CustomRules", rulesJson);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Failed to save custom rules: {e.Message}");
            }
        }
    }
} 