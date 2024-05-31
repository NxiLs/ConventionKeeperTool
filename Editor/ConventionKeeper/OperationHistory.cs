using System.Collections.Generic;
using System.Linq;
using System;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ConventionKeeper
{
    public static class OperationHistory
    {
        private const int MAX_HISTORY_SIZE = 20;
        
        [Serializable]
        public class OperationRecord
        {
            public string OperationType;
            public DateTime Timestamp;
            public List<ObjectRenameRecord> RenamedObjects;
            public string Description;
            public int UndoGroupId;

            public OperationRecord()
            {
                RenamedObjects = new List<ObjectRenameRecord>();
                Timestamp = DateTime.Now;
            }
        }

        [Serializable]
        public class ObjectRenameRecord
        {
            public string ObjectPath;
            public string ObjectType;
            public string OldName;
            public string NewName;
            public bool IsAsset;
            public int InstanceId;
            
            public Object CachedObject;
        }

        private static List<OperationRecord> _operationHistory = new List<OperationRecord>();
        
        public static OperationRecord RecordRenameOperation(string operationType, string description, 
            List<Object> objects, List<string> oldNames, List<string> newNames)
        {
            OperationRecord record = new OperationRecord
            {
                OperationType = operationType,
                Description = description,
                UndoGroupId = Undo.GetCurrentGroup()
            };

            for (int i = 0; i < objects.Count; i++)
            {
                if (i >= oldNames.Count || i >= newNames.Count) break;

                Object obj = objects[i];
                if (obj == null) continue;

                ObjectRenameRecord renameRecord = new ObjectRenameRecord
                {
                    ObjectType = obj.GetType().Name,
                    OldName = oldNames[i],
                    NewName = newNames[i],
                    CachedObject = obj
                };

                string assetPath = AssetDatabase.GetAssetPath(obj);
                if (!string.IsNullOrEmpty(assetPath))
                {
                    renameRecord.IsAsset = true;
                    renameRecord.ObjectPath = AssetDatabase.AssetPathToGUID(assetPath);
                }
                else
                {
                    renameRecord.IsAsset = false;
                    renameRecord.InstanceId = obj.GetInstanceID();
                    
                    if (obj is GameObject go)
                    {
                        renameRecord.ObjectPath = GetGameObjectPath(go);
                    }
                }

                record.RenamedObjects.Add(renameRecord);
            }

            _operationHistory.Add(record);

            if (_operationHistory.Count > MAX_HISTORY_SIZE)
            {
                _operationHistory.RemoveAt(0);
            }

            SaveHistory();
            
            return record;
        }
        
        public static bool UndoLastOperation()
        {
            if (_operationHistory.Count == 0)
            {
                Debug.LogWarning("Convention Keeper: No operations to undo.");
                return false;
            }

            OperationRecord lastOperation = _operationHistory[_operationHistory.Count - 1];
            return UndoOperation(lastOperation);
        }
        
        public static bool UndoOperation(OperationRecord operation)
        {
            if (operation == null || operation.RenamedObjects.Count == 0)
            {
                Debug.LogWarning("Convention Keeper: Invalid operation for undo.");
                return false;
            }

            List<Object> objectsToUndo = new List<Object>();
            List<ObjectRenameRecord> validRecords = new List<ObjectRenameRecord>();

            foreach (ObjectRenameRecord record in operation.RenamedObjects)
            {
                Object obj = FindObject(record);
                if (obj != null && obj.name == record.NewName)
                {
                    objectsToUndo.Add(obj);
                    validRecords.Add(record);
                }
            }

            if (objectsToUndo.Count == 0)
            {
                Debug.LogWarning("Convention Keeper: Objects for undo not found or already changed.");
                return false;
            }

            Undo.IncrementCurrentGroup();
            Undo.RecordObjects(objectsToUndo.ToArray(), $"Convention Keeper: Undo {operation.OperationType}");

            int undoneCount = 0;
            bool hasAssets = false;
            
            for (int i = 0; i < objectsToUndo.Count; i++)
            {
                if (RenameUtility.RenameObject(objectsToUndo[i], validRecords[i].OldName))
                {
                    undoneCount++;
                    
                    if (validRecords[i].IsAsset)
                    {
                        hasAssets = true;
                    }
                }
                else
                {
                    Debug.LogWarning($"Convention Keeper: Failed to undo renaming of object '{objectsToUndo[i].name}'");
                }
            }

            if (hasAssets)
            {
                RenameUtility.RefreshProjectWindow();
            }

            _operationHistory.Remove(operation);
            SaveHistory();

            Debug.Log($"Convention Keeper: Undone renaming of {undoneCount} objects.");
            return true;
        }
        
        public static List<OperationRecord> GetRecentOperations(int count = 10)
        {
            return _operationHistory.TakeLast(count).Reverse().ToList();
        }
        
        public static void ClearHistory()
        {
            _operationHistory.Clear();
            SaveHistory();
            Debug.Log("Convention Keeper: Operation history cleared.");
        }
        
        public static bool HasUndoableOperations()
        {
            return _operationHistory.Count > 0;
        }
        
        public static string GetLastOperationDescription()
        {
            if (_operationHistory.Count == 0) return "No operations";
            
            OperationRecord lastOp = _operationHistory[_operationHistory.Count - 1];
            return $"{lastOp.Description} ({lastOp.RenamedObjects.Count} objects, {lastOp.Timestamp:HH:mm:ss})";
        }
        
        public static string GetDisplayName(Object obj)
        {
            if (obj == null) return "";
            return RenameUtility.GetDisplayName(obj);
        }
        
        public static void ExportHistory(string filePath)
        {
            System.Text.StringBuilder report = new System.Text.StringBuilder();
            report.AppendLine("Convention Keeper - Operation History");
            report.AppendLine($"Exported: {DateTime.Now}");
            report.AppendLine($"Total operations: {_operationHistory.Count}");
            report.AppendLine();

            foreach (OperationRecord operation in _operationHistory.AsEnumerable().Reverse())
            {
                report.AppendLine($"[{operation.Timestamp:yyyy-MM-dd HH:mm:ss}] {operation.OperationType}");
                report.AppendLine($"Description: {operation.Description}");
                report.AppendLine($"Objects changed: {operation.RenamedObjects.Count}");
                
                foreach (ObjectRenameRecord record in operation.RenamedObjects)
                {
                    report.AppendLine($"  • {record.ObjectType}: '{record.OldName}' → '{record.NewName}'");
                }
                
                report.AppendLine();
            }

            System.IO.File.WriteAllText(filePath, report.ToString());
        }

        private static Object FindObject(ObjectRenameRecord record)
        {
            if (record.CachedObject != null)
            {
                return record.CachedObject;
            }

            if (record.IsAsset)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(record.ObjectPath);
                if (!string.IsNullOrEmpty(assetPath))
                {
                    return AssetDatabase.LoadAssetAtPath<Object>(assetPath);
                }
            }
            else
            {
                Object obj = EditorUtility.InstanceIDToObject(record.InstanceId);
                if (obj != null) return obj;

                if (!string.IsNullOrEmpty(record.ObjectPath))
                {
                    return FindGameObjectByPath(record.ObjectPath);
                }
            }

            return null;
        }

        private static string GetGameObjectPath(GameObject go)
        {
            string path = go.name;
            Transform parent = go.transform.parent;
            
            while (parent != null)
            {
                path = parent.name + "/" + path;
                parent = parent.parent;
            }
            
            return path;
        }

        private static GameObject FindGameObjectByPath(string path)
        {
            string[] parts = path.Split('/');
            
            GameObject root = null;
            foreach (GameObject go in Object.FindObjectsOfType<GameObject>())
            {
                if (go.transform.parent == null && go.name == parts[0])
                {
                    root = go;
                    break;
                }
            }

            if (root == null) return null;

            Transform current = root.transform;
            for (int i = 1; i < parts.Length; i++)
            {
                Transform child = current.Find(parts[i]);
                if (child == null) return null;
                current = child;
            }

            return current.gameObject;
        }

        private static void SaveHistory()
        {
            try
            {
                List<OperationRecord> historyData = new List<OperationRecord>();
                foreach (OperationRecord op in _operationHistory)
                {
                    OperationRecord savedOp = new OperationRecord
                    {
                        OperationType = op.OperationType,
                        Timestamp = op.Timestamp,
                        Description = op.Description,
                        UndoGroupId = op.UndoGroupId
                    };

                    foreach (ObjectRenameRecord record in op.RenamedObjects)
                    {
                        savedOp.RenamedObjects.Add(new ObjectRenameRecord
                        {
                            ObjectPath = record.ObjectPath,
                            ObjectType = record.ObjectType,
                            OldName = record.OldName,
                            NewName = record.NewName,
                            IsAsset = record.IsAsset,
                            InstanceId = record.InstanceId
                        });
                    }

                    historyData.Add(savedOp);
                }

                string historyJson = JsonUtility.ToJson(new SerializableList<OperationRecord> { Items = historyData });
                EditorPrefs.SetString("ConventionKeeper.OperationHistory", historyJson);
            }
            catch (Exception e)
            {
                Debug.LogError($"Convention Keeper: Error saving history: {e.Message}");
            }
        }

        private static void LoadHistory()
        {
            try
            {
                string historyJson = EditorPrefs.GetString("ConventionKeeper.OperationHistory", "");
                if (!string.IsNullOrEmpty(historyJson))
                {
                    SerializableList<OperationRecord> historyData = JsonUtility.FromJson<SerializableList<OperationRecord>>(historyJson);
                    _operationHistory = historyData?.Items ?? new List<OperationRecord>();
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Convention Keeper: Error loading history: {e.Message}");
                _operationHistory = new List<OperationRecord>();
            }
        }

        [Serializable]
        private class SerializableList<T>
        {
            public List<T> Items;
        }

        [InitializeOnLoadMethod]
        private static void Initialize()
        {
            LoadHistory();
        }
    }
} 