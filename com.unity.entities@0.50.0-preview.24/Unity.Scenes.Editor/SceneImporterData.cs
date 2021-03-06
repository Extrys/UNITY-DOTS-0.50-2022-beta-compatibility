using System;
using System.Collections.Generic;
using System.IO;
using Unity.Collections;
using Unity.Entities;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Hash128 = Unity.Entities.Hash128;

namespace Unity.Scenes.Editor
{
    /// <summary>
    /// Contains scene data that is stored in the userData field of the importer.
    /// </summary>
    public struct SceneImporterData
    {
        /// <summary>
        /// Get the importer data for a scene given its path.
        /// </summary>
        /// <param name="path">The scene path.</param>
        /// <returns>The data for the scene.</returns>
        public static SceneImporterData GetAtPath(string path)
        {
            var importer = AssetImporter.GetAtPath(path);
            if (importer == null || string.IsNullOrEmpty(importer.userData))
                return default;
            return JsonUtility.FromJson<SceneImporterData>(importer.userData);
        }

        /// <summary>
        /// Set the scene data for the scene at the given path.
        /// </summary>
        /// <param name="path">The scene path.</param>
        /// <param name="data">The scene data.</param>
        public static void SetAtPath(string path, SceneImporterData data)
        {
            var importer = AssetImporter.GetAtPath(path);
            if (importer == null)
                return;
            importer.userData = JsonUtility.ToJson(data);
        }
    }
}
