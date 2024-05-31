using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ConventionKeeper
{
    /// <summary>
    /// Utility class for correctly renaming assets and scene objects
    /// </summary>
    public static class RenameUtility
    {
        /// <summary>
        /// Correctly renames an object (asset or scene object)
        /// </summary>
        /// <param name="obj">Object to rename</param>
        /// <param name="newName">New name</param>
        /// <returns>True if renaming was successful</returns>
        public static bool RenameObject(Object obj, string newName)
        {
            if (obj == null || string.IsNullOrEmpty(newName)) return false;

            string assetPath = AssetDatabase.GetAssetPath(obj);
            
            if (!string.IsNullOrEmpty(assetPath))
            {
                string result = AssetDatabase.RenameAsset(assetPath, newName);
                
                if (string.IsNullOrEmpty(result))
                {
                    return true;
                }
                else
                {
                    Debug.LogWarning($"Convention Keeper: Failed to rename asset '{assetPath}': {result}");
                    return false;
                }
            }
            else
            {
                obj.name = newName;
                EditorUtility.SetDirty(obj);
                return true;
            }
        }
        
        public static bool IsAsset(Object obj)
        {
            if (obj == null) return false;
            string assetPath = AssetDatabase.GetAssetPath(obj);
            return !string.IsNullOrEmpty(assetPath);
        }

        /// <summary>
        /// Gets the display name of an object (for assets may differ from obj.name)
        /// </summary>
        /// <param name="obj">Object</param>
        /// <returns>Display name</returns>
        public static string GetDisplayName(Object obj)
        {
            if (obj == null) return string.Empty;

            if (IsAsset(obj))
            {
                string assetPath = AssetDatabase.GetAssetPath(obj);
                
                return System.IO.Path.GetFileNameWithoutExtension(assetPath);
            }
            else
            {
                return obj.name;
            }
        }

        /// <summary>
        /// Updates the Project window display for assets
        /// </summary>
        public static void RefreshProjectWindow()
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            
            EditorUtility.FocusProjectWindow();
            
            EditorApplication.delayCall += () =>
            {
                EditorUtility.FocusProjectWindow();
            };
        }

        /// <summary>
        /// Validates the name for renaming (checks for invalid characters)
        /// </summary>
        /// <param name="name">Name to check</param>
        /// <param name="isAsset">Is the target object an asset</param>
        /// <returns>True if valid</returns>
        public static bool ValidateName(string name, bool isAsset)
        {
            if (string.IsNullOrEmpty(name)) return false;

            if (isAsset)
            {
                char[] invalidChars = System.IO.Path.GetInvalidFileNameChars();
                return name.IndexOfAny(invalidChars) == -1;
            }
            else
            {
                char[] problematicChars = { '/', '\\', ':', '*', '?', '"', '<', '>', '|' };
                return name.IndexOfAny(problematicChars) == -1;
            }
        }

        /// <summary>
        /// Clears the name from invalid characters
        /// </summary>
        /// <param name="name">Original name</param>
        /// <param name="isAsset">Is the target object an asset</param>
        /// <returns>Sanitized name</returns>
        public static string SanitizeName(string name, bool isAsset)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;

            char[] invalidChars;
            
            if (isAsset)
            {
                invalidChars = System.IO.Path.GetInvalidFileNameChars();
            }
            else
            {
                invalidChars = new char[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' };
            }

            foreach (char c in invalidChars)
            {
                name = name.Replace(c, '_');
            }

            return name;
        }
    }
} 