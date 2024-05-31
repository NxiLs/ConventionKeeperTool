using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace ConventionKeeper
{
    public static class ConventionKeeperContextMenu
    {
        [MenuItem("GameObject/Convention Keeper/Rename Group...", false, 0)]
        public static void OpenFromHierarchy(MenuCommand command)
        {
            if (!IsValidMenuCommand(command)) return;
            ConventionKeeperWindow.ShowWindow();
        }

        [MenuItem("GameObject/Convention Keeper/Quick Rename/Add Prefix 'GO_'", false, 20)]
        public static void QuickAddPrefixGameObject(MenuCommand command)
        {
            if (!IsValidMenuCommand(command)) return;
            QuickRename("GO_", "", "", "", false);
        }

        [MenuItem("GameObject/Convention Keeper/Quick Rename/Add Suffix '_Obj'", false, 21)]
        public static void QuickAddSuffixGameObject(MenuCommand command)
        {
            if (!IsValidMenuCommand(command)) return;
            QuickRename("", "_Obj", "", "", false);
        }

        [MenuItem("GameObject/Convention Keeper/Quick Rename/Add Index", false, 22)]
        public static void QuickAddIndexGameObject(MenuCommand command)
        {
            if (!IsValidMenuCommand(command)) return;
            QuickRename("", "", "", "", true);
        }

        [MenuItem("GameObject/Convention Keeper/Quick Rename/Remove Numbers", false, 23)]
        public static void QuickRemoveNumbersGameObject(MenuCommand command)
        {
            if (!IsValidMenuCommand(command)) return;
            QuickRename("", "", @"\d+", "", false, true);
        }

        [MenuItem("Assets/Convention Keeper/Rename Group...", false, 0)]
        public static void OpenFromProject(MenuCommand command)
        {
            if (!IsValidMenuCommand(command)) return;
            ConventionKeeperWindow.ShowWindow();
        }

        [MenuItem("Assets/Convention Keeper/Quick Rename/Add Prefix 'Asset_'", false, 20)]
        public static void QuickAddPrefixAsset(MenuCommand command)
        {
            if (!IsValidMenuCommand(command)) return;
            QuickRename("Asset_", "", "", "", false);
        }

        [MenuItem("Assets/Convention Keeper/Quick Rename/Add Suffix '_Data'", false, 21)]
        public static void QuickAddSuffixAsset(MenuCommand command)
        {
            if (!IsValidMenuCommand(command)) return;
            QuickRename("", "_Data", "", "", false);
        }

        [MenuItem("Assets/Convention Keeper/Quick Rename/Normalize Names", false, 22)]
        public static void QuickNormalizeAsset(MenuCommand command)
        {
            if (!IsValidMenuCommand(command)) return;
            QuickRename("", "", @"[^a-zA-Z0-9_]", "_", false, true);
        }

        [MenuItem("Assets/Convention Keeper/Validate Naming Conventions", false, 50)]
        public static void ValidateNamingFromProject(MenuCommand command)
        {
            if (!IsValidMenuCommand(command)) return;
            ConventionValidatorWindow.ShowWindow();
        }

        [MenuItem("GameObject/Convention Keeper/Validate Naming Conventions", false, 50)]
        public static void ValidateNamingFromHierarchy(MenuCommand command)
        {
            if (!IsValidMenuCommand(command)) return;
            ConventionValidatorWindow.ShowWindow();
        }

        [MenuItem("GameObject/Convention Keeper/Undo Last Operation", false, 70)]
        public static void UndoLastOperationFromHierarchy(MenuCommand command)
        {
            if (!IsValidMenuCommand(command)) return;
            OperationHistory.UndoLastOperation();
        }

        [MenuItem("Assets/Convention Keeper/Undo Last Operation", false, 70)]
        public static void UndoLastOperationFromProject(MenuCommand command)
        {
            if (!IsValidMenuCommand(command)) return;
            OperationHistory.UndoLastOperation();
        }

        [MenuItem("GameObject/Convention Keeper/Rename Group...", true)]
        public static bool ValidateOpenFromHierarchy()
        {
            return Selection.gameObjects.Length > 0;
        }

        [MenuItem("GameObject/Convention Keeper/Quick Rename/Add Prefix 'GO_'", true)]
        [MenuItem("GameObject/Convention Keeper/Quick Rename/Add Suffix '_Obj'", true)]
        [MenuItem("GameObject/Convention Keeper/Quick Rename/Add Index", true)]
        [MenuItem("GameObject/Convention Keeper/Quick Rename/Remove Numbers", true)]
        public static bool ValidateQuickRenameGameObject()
        {
            return Selection.gameObjects.Length > 0;
        }

        [MenuItem("Assets/Convention Keeper/Rename Group...", true)]
        public static bool ValidateOpenFromProject()
        {
            return Selection.assetGUIDs.Length > 0;
        }

        [MenuItem("Assets/Convention Keeper/Quick Rename/Add Prefix 'Asset_'", true)]
        [MenuItem("Assets/Convention Keeper/Quick Rename/Add Suffix '_Data'", true)]
        [MenuItem("Assets/Convention Keeper/Quick Rename/Normalize Names", true)]
        public static bool ValidateQuickRenameAsset()
        {
            return Selection.assetGUIDs.Length > 0;
        }

        [MenuItem("GameObject/Convention Keeper/Undo Last Operation", true)]
        public static bool ValidateUndoFromHierarchy()
        {
            return OperationHistory.HasUndoableOperations();
        }

        [MenuItem("Assets/Convention Keeper/Undo Last Operation", true)]
        public static bool ValidateUndoFromProject()
        {
            return OperationHistory.HasUndoableOperations();
        }

        private static bool IsValidMenuCommand(MenuCommand command)
        {
            return command?.context == null || command.context == Selection.objects.FirstOrDefault();
        }

        private static void QuickRename(string prefix, string suffix, string search, string replace, 
            bool addIndex, bool useRegex = false)
        {
            List<Object> targets = GetSelectedTargets();

            if (targets.Count == 0)
            {
                Debug.LogWarning("Convention Keeper: No objects selected for quick rename.");
                return;
            }

            QuickRenameResult operationResult = ExecuteQuickRename(targets, prefix, suffix, search, replace, addIndex, useRegex);
            
            RecordOperationHistory(operationResult, prefix, suffix, search, replace, addIndex, useRegex);
            
            if (operationResult.HasAssets)
            {
                RenameUtility.RefreshProjectWindow();
            }

            Debug.Log($"Convention Keeper: Quick renamed {operationResult.RenamedCount} objects.");
        }

        private static List<Object> GetSelectedTargets()
        {
            List<Object> targets = new List<Object>();
            
            if (Selection.gameObjects.Length > 0)
            {
                targets.AddRange(Selection.gameObjects);
            }
            else if (Selection.objects.Length > 0)
            {
                targets.AddRange(Selection.objects);
            }

            return targets;
        }

        private static QuickRenameResult ExecuteQuickRename(List<Object> targets, string prefix, string suffix, 
            string search, string replace, bool addIndex, bool useRegex)
        {
            QuickRenameResult result = new QuickRenameResult();
            HashSet<string> usedNames = new HashSet<string>();

            Undo.RecordObjects(targets.ToArray(), "Convention Keeper: Quick Rename");

            for (int i = 0; i < targets.Count; i++)
            {
                string currentDisplayName = RenameUtility.GetDisplayName(targets[i]);
                string newName = ProcessQuickRename(currentDisplayName, prefix, suffix, search, replace, i, addIndex, useRegex);

                newName = EnsureUniqueName(newName, usedNames, i);
                usedNames.Add(newName);

                if (TryRenameObject(targets[i], currentDisplayName, newName, result))
                {
                    if (RenameUtility.IsAsset(targets[i]))
                    {
                        result.HasAssets = true;
                    }
                }
            }

            return result;
        }

        private static string EnsureUniqueName(string name, HashSet<string> usedNames, int index)
        {
            if (usedNames.Contains(name))
            {
                return $"{name}_{index + 1}";
            }
            return name;
        }

        private static bool TryRenameObject(Object target, string oldName, string newName, QuickRenameResult result)
        {
            if (oldName == newName) return false;

            if (RenameUtility.RenameObject(target, newName))
            {
                result.RenamedObjects.Add(target);
                result.OldNames.Add(oldName);
                result.NewNames.Add(newName);
                result.RenamedCount++;
                return true;
            }

            return false;
        }

        private static void RecordOperationHistory(QuickRenameResult result, string prefix, string suffix, 
            string search, string replace, bool addIndex, bool useRegex)
        {
            if (result.RenamedObjects.Count > 0)
            {
                const string operationType = "Quick Rename";
                string description = GetQuickRenameDescription(prefix, suffix, search, replace, addIndex, useRegex);
                
                OperationHistory.RecordRenameOperation(operationType, description, 
                    result.RenamedObjects, result.OldNames, result.NewNames);
            }
        }

        private static string ProcessQuickRename(string currentDisplayName, string prefix, string suffix, 
            string search, string replace, int index, bool addIndex, bool useRegex)
        {
            string result = currentDisplayName;

            if (!string.IsNullOrEmpty(search))
            {
                result = ApplySearchReplace(result, search, replace, useRegex, currentDisplayName);
            }

            result = $"{prefix}{result}{suffix}";

            if (addIndex)
            {
                result += $"_{(index + 1):00}";
            }

            return result;
        }

        private static string ApplySearchReplace(string text, string search, string replace, bool useRegex, string fallback)
        {
            if (useRegex)
            {
                try
                {
                    return System.Text.RegularExpressions.Regex.Replace(text, search, replace);
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"Convention Keeper: Regex error - {e.Message}");
                    return fallback;
                }
            }
            
            return text.Replace(search, replace);
        }

        private static string GetQuickRenameDescription(string prefix, string suffix, string search, 
            string replace, bool addIndex, bool useRegex)
        {
            List<string> parts = new List<string>();
            
            if (!string.IsNullOrEmpty(prefix)) parts.Add($"prefix='{prefix}'");
            if (!string.IsNullOrEmpty(suffix)) parts.Add($"suffix='{suffix}'");
            if (!string.IsNullOrEmpty(search)) 
            {
                string searchType = useRegex ? "regex" : "text";
                parts.Add($"{searchType}_replace='{search}'->'{replace}'");
            }
            if (addIndex) parts.Add("add_index");
            
            return $"Quick rename: {(parts.Count > 0 ? string.Join(", ", parts) : "basic_rename")}";
        }

        private class QuickRenameResult
        {
            public List<Object> RenamedObjects { get; } = new List<Object>();
            public List<string> OldNames { get; } = new List<string>();
            public List<string> NewNames { get; } = new List<string>();
            public int RenamedCount { get; set; }
            public bool HasAssets { get; set; }
        }
    }
}