#if !UNITY_DOTSRUNTIME
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Serialization;
using UnityEngine;
using UnityEngine.Scripting.APIUpdating;
using UnityObject = UnityEngine.Object;

namespace Unity.Scenes
{
    [MovedFrom(true, "Unity.Entities.Serialization", "Unity.Entities.Hybrid")]
    public static class SerializeUtilityHybrid
    {
        public static void Serialize(EntityManager manager, BinaryWriter writer, out ReferencedUnityObjects objRefs)
        {
            SerializeUtility.SerializeWorld(manager, writer, out var referencedObjects);
            SerializeObjectReferences((UnityEngine.Object[])referencedObjects, out objRefs);
        }

        public static void Serialize(EntityManager manager, BinaryWriter writer, out ReferencedUnityObjects objRefs, NativeArray<EntityRemapUtility.EntityRemapInfo> entityRemapInfos)
        {
            SerializeUtility.SerializeWorld(manager, writer, out var referencedObjects, entityRemapInfos);
            SerializeObjectReferences((UnityEngine.Object[])referencedObjects, out objRefs);
        }

        public static void Deserialize(EntityManager manager, BinaryReader reader, ReferencedUnityObjects objRefs)
        {
            DeserializeObjectReferences(objRefs, out var objectReferences);
            var transaction = manager.BeginExclusiveEntityTransaction();
            SerializeUtility.DeserializeWorld(transaction, reader, objectReferences);
            manager.EndExclusiveEntityTransaction();
        }

        public static void SerializeObjectReferences(UnityEngine.Object[] referencedObjects, out ReferencedUnityObjects objRefs)
        {
            objRefs = null;

            if (referencedObjects?.Length > 0)
            {
                objRefs = UnityEngine.ScriptableObject.CreateInstance<ReferencedUnityObjects>();
                objRefs.Array = referencedObjects;

                var companionObjectIndices = new List<int>();

                for (int i = 0; i != objRefs.Array.Length; i++)
                {
                    var obj = objRefs.Array[i];
                    if (obj != null && obj is GameObject gameObject)
                    {
                        if (gameObject.scene.IsValid())
                        {
                            // Add companion entry, this allows us to differentiate Prefab references and Companion Objects at runtime deserialization
                            companionObjectIndices.Add(i);
                        }
                    }
                }

                objRefs.CompanionObjectIndices = companionObjectIndices.ToArray();
            }
        }

        public static void DeserializeObjectReferences(ReferencedUnityObjects objRefs, out UnityEngine.Object[] objectReferences)
        {
            if (objRefs == null)
            {
                objectReferences = null;
                return;
            }

            objectReferences = new UnityEngine.Object[objRefs.Array.Length];

            // NOTE: Object references must not include fake object references, they must be real null.
            // The Unity.Properties deserializer can't handle them correctly.
            // We might want to add support for handling fake null,
            // but it would require tight integration in the deserialize function so that a correct fake null unityengine.object can be constructed on deserialize
            for (int i = 0; i != objRefs.Array.Length; i++)
            {
                if (objRefs.Array[i] != null)
                    objectReferences[i] = objRefs.Array[i];
            }

#if UNITY_EDITOR && !UNITY_DISABLE_MANAGED_COMPONENTS
            foreach (var companionIndex in objRefs.CompanionObjectIndices)
            {
                var source = (UnityEngine.GameObject) objectReferences[companionIndex];
                CompanionGameObjectUtility.MoveToCompanionScene(source, false);
            }
#else
            // Companion Objects
            // When using bundles, the Companion GameObjects cannot be directly used (prefabs), so we need to instantiate everything.
            var sourceToInstance = new Dictionary<UnityEngine.GameObject, UnityEngine.GameObject>();
            foreach (var companionIndex in objRefs.CompanionObjectIndices)
            {
                var source = (UnityEngine.GameObject) objectReferences[companionIndex];
                var instance = UnityEngine.Object.Instantiate(source);
                objectReferences[companionIndex] = instance;
                sourceToInstance.Add(source, instance);
            }

            for (int i = 0; i != objectReferences.Length; i++)
            {
                if (objectReferences[i] is UnityEngine.Component component)
                {
                    objectReferences[i] = sourceToInstance[component.gameObject].GetComponent(component.GetType());
                }
            }
#endif
        }
    }
}
#endif
