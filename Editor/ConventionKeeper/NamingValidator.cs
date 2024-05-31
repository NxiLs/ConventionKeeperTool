using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEditor;
using UnityEngine.Serialization;

namespace ConventionKeeper
{
    /// <summary>
    /// Validator for checking object names compliance with established conventions
    /// </summary>
    public static class NamingValidator
    {
        [System.Serializable]
        public class NamingRule
        {
            public string RuleName;
            public string Pattern;
            public string ObjectType;
            public bool IsRegex;
            public string ErrorMessage;
            public bool IsEnabled = true;
        }

        private static readonly List<NamingRule> DefaultRules = new List<NamingRule>
        {
            new NamingRule
            {
                RuleName = "No Spaces",
                Pattern = @"\s",
                ObjectType = "All",
                IsRegex = true,
                ErrorMessage = "Names should not contain spaces",
                IsEnabled = true
            },
            new NamingRule
            {
                RuleName = "PascalCase for GameObjects",
                Pattern = @"^[A-Z][a-zA-Z0-9]*$",
                ObjectType = "GameObject",
                IsRegex = true,
                ErrorMessage = "GameObjects should use PascalCase",
                IsEnabled = false
            },
            new NamingRule
            {
                RuleName = "No Special Characters",
                Pattern = @"[^a-zA-Z0-9_\-]",
                ObjectType = "All",
                IsRegex = true,
                ErrorMessage = "Names should not contain special characters",
                IsEnabled = false
            },
            new NamingRule
            {
                RuleName = "Prefab Suffix",
                Pattern = "_Prefab$",
                ObjectType = "Prefab",
                IsRegex = true,
                ErrorMessage = "Prefabs should end with '_Prefab'",
                IsEnabled = false
            },
            new NamingRule
            {
                RuleName = "Material Suffix",
                Pattern = "_Mat$",
                ObjectType = "Material",
                IsRegex = true,
                ErrorMessage = "Materials should end with '_Mat'",
                IsEnabled = false
            },
            new NamingRule
            {
                RuleName = "Texture Suffix",
                Pattern = "_Tex$",
                ObjectType = "Texture2D",
                IsRegex = true,
                ErrorMessage = "Textures should end with '_Tex'",
                IsEnabled = false
            }
        };

        public static List<string> ValidateObject(Object obj, List<NamingRule> customRules = null)
        {
            List<string> violations = new List<string>();
            List<NamingRule> rules = customRules ?? GetActiveRules();

            foreach (NamingRule rule in rules)
            {
                if (!rule.IsEnabled) continue;

                if (rule.ObjectType != "All" && !IsObjectOfType(obj, rule.ObjectType))
                    continue;

                if (IsViolation(obj.name, rule))
                {
                    violations.Add($"{rule.RuleName}: {rule.ErrorMessage}");
                }
            }

            return violations;
        }

        public static Dictionary<Object, List<string>> ValidateSelection()
        {
            Dictionary<Object, List<string>> results = new Dictionary<Object, List<string>>();
            Object[] selectedObjects = Selection.objects;

            foreach (Object obj in selectedObjects)
            {
                List<string> violations = ValidateObject(obj);
                if (violations.Count > 0)
                {
                    results[obj] = violations;
                }
            }

            return results;
        }

        public static Dictionary<Object, List<string>> ValidateScene()
        {
            Dictionary<Object, List<string>> results = new Dictionary<Object, List<string>>();
            GameObject[] gameObjects = Object.FindObjectsOfType<GameObject>();

            foreach (GameObject go in gameObjects)
            {
                List<string> violations = ValidateObject(go);
                if (violations.Count > 0)
                {
                    results[go] = violations;
                }
            }

            return results;
        }

        public static Dictionary<Object, List<string>> ValidateProject()
        {
            Dictionary<Object, List<string>> results = new Dictionary<Object, List<string>>();
            string[] guids = AssetDatabase.FindAssets("t:Object");

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                Object asset = AssetDatabase.LoadAssetAtPath<Object>(path);
                
                if (asset != null)
                {
                    List<string> violations = ValidateObject(asset);
                    if (violations.Count > 0)
                    {
                        results[asset] = violations;
                    }
                }
            }

            return results;
        }

        private static bool IsViolation(string name, NamingRule rule)
        {
            if (rule.IsRegex)
            {
                try
                {
                    Regex regex = new Regex(rule.Pattern);
                    
                    if (rule.RuleName.Contains("PascalCase") || rule.RuleName.Contains("Suffix"))
                    {
                        return !regex.IsMatch(name);
                    }
                    else
                    {
                        return regex.IsMatch(name);
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"Invalid regex pattern in rule '{rule.RuleName}': {e.Message}");
                    return false;
                }
            }
            else
            {
                return name.Contains(rule.Pattern);
            }
        }

        private static bool IsObjectOfType(Object obj, string typeName)
        {
            if (typeName == "All") return true;
            
            string objectType = obj.GetType().Name;
            
            if (typeName == "Prefab")
            {
                return PrefabUtility.GetPrefabAssetType(obj) != PrefabAssetType.NotAPrefab;
            }
            
            return objectType == typeName || obj.GetType().IsSubclassOf(System.Type.GetType("UnityEngine." + typeName));
        }

        private static List<NamingRule> GetActiveRules()
        {
            // Here you can load custom rules from EditorPrefs
            
            return DefaultRules;
        }

        public static List<NamingRule> GetDefaultRules()
        {
            return new List<NamingRule>(DefaultRules);
        }

        public static string GenerateSuggestedName(string originalName, string objectType)
        {
            string suggested = originalName;

            suggested = suggested.Replace(" ", "");

            suggested = Regex.Replace(suggested, @"[^a-zA-Z0-9_\-]", "");

            if (!string.IsNullOrEmpty(suggested))
            {
                suggested = char.ToUpper(suggested[0]) + suggested.Substring(1);
            }

            switch (objectType)
            {
                case "Material":
                    if (!suggested.EndsWith("_Mat"))
                        suggested += "_Mat";
                    break;
                case "Texture2D":
                    if (!suggested.EndsWith("_Tex"))
                        suggested += "_Tex";
                    break;
                case "Prefab":
                    if (!suggested.EndsWith("_Prefab"))
                        suggested += "_Prefab";
                    break;
            }

            return suggested;
        }
    }
} 