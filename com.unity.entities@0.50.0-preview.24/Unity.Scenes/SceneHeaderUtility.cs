using System;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Entities;
using Unity.Entities.Serialization;
using Unity.IO.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;
using Hash128 = Unity.Entities.Hash128;

#if UNITY_DOTSRUNTIME
using Unity.Runtime.IO;
#endif

namespace Unity.Scenes
{
    internal unsafe struct RequestSceneHeader : ISystemStateComponentData
    {
#if UNITY_DOTSRUNTIME
        // DOTS Runtime IO handle. When the AsyncReadManager provides a mechanism to read files without knowing the size
        // this type can be changed to a common type.
        public int IOHandle;
        public bool IsCompleted
        {
            get
            {
                return new AsyncOp() {m_Handle = IOHandle}.GetStatus() > AsyncOp.Status.InProgress;
            }
        }

        public bool Succeeded => new AsyncOp() {m_Handle = IOHandle}.GetStatus() == AsyncOp.Status.Success;

        public void GetFileData(out byte* data, out long fileSize)
        {
            new AsyncOp() {m_Handle = IOHandle}.GetData(out data, out var intFileSize);
            fileSize = intFileSize;
        }

        public void Dispose()
        {
            new AsyncOp() {m_Handle = IOHandle}.Dispose();
        }
#else
        public SceneHeaderUtility.HeaderData* HeaderData;
        public bool IsCompleted => HeaderData->JobHandle.IsCompleted;

        public void Complete()
        {
            HeaderData->JobHandle.Complete();
        }

        public void Dispose()
        {
            ref var headerData = ref HeaderData;
#if UNITY_EDITOR
            if(headerData->ManagedHeaderData.IsAllocated)
                headerData->ManagedHeaderData.Free();
#endif
            Memory.Unmanaged.Free(headerData->Data, Allocator.Persistent);
            Memory.Unmanaged.Free(headerData, Allocator.Persistent);
        }
#endif
    }

    internal struct SceneHeaderUtility
    {
        EntityQuery _CleanupHeaderQuery;
#if !UNITY_DOTSRUNTIME
        private NativeList<RequestSceneHeader> _PendingCleanups;
#endif
        public SceneHeaderUtility(SystemBase system)
        {
#if !UNITY_DOTSRUNTIME
            _PendingCleanups = new NativeList<RequestSceneHeader>(0, Allocator.Persistent);
#endif
            _CleanupHeaderQuery = system.GetEntityQuery(
                new EntityQueryDesc
                {
                    All = new[] {ComponentType.ReadOnly<RequestSceneHeader>()},
                    None = new[] {ComponentType.ReadOnly<SceneReference>()}
                },
                new EntityQueryDesc
                {
                    All = new[] {ComponentType.ReadOnly<RequestSceneHeader>()},
                    None = new[] {ComponentType.ReadOnly<ResolvedSceneHash>()}
                },
                new EntityQueryDesc
                {
                    All = new[] {ComponentType.ReadOnly<RequestSceneHeader>()},
                    None = new[] {ComponentType.ReadOnly<RequestSceneLoaded>()}
                },
                new EntityQueryDesc
                {
                    All = new[]
                    {
                        ComponentType.ReadOnly<RequestSceneHeader>(),
                        ComponentType.ReadOnly<DisableSceneResolveAndLoad>()
                    }
                },
                new EntityQueryDesc
                {
                    All = new[]
                    {
                        ComponentType.ReadOnly<RequestSceneHeader>(), ComponentType.ReadOnly<Disabled>()
                    }
                }
            );
        }

        public void Dispose(EntityManager entityManager)
        {
            CleanupHeaders(entityManager);
#if !UNITY_DOTSRUNTIME
            for (int i = 0; i < _PendingCleanups.Length; ++i)
            {
                _PendingCleanups[i].Complete();
                _PendingCleanups[i].Dispose();
            }
            _PendingCleanups.Dispose();
#endif
        }

        public void CleanupHeaders(EntityManager entityManager)
        {
#if !UNITY_DOTSRUNTIME
            for (int i=0, length=_PendingCleanups.Length; i<length;)
            {
                if (_PendingCleanups[i].IsCompleted)
                {
                    _PendingCleanups[i].Dispose();
                    _PendingCleanups.RemoveAtSwapBack(i);
                    --length;
                }
                else
                {
                    ++i;
                }
            }
#endif
            if (!_CleanupHeaderQuery.IsEmptyIgnoreFilter)
            {
                using (var requestSceneHeaders = _CleanupHeaderQuery.ToComponentDataArray<RequestSceneHeader>(Allocator.TempJob))
                {
                    for (int i = 0; i < requestSceneHeaders.Length; ++i)
                    {
#if !UNITY_DOTSRUNTIME

                        if (requestSceneHeaders[i].IsCompleted)
                        {
                            requestSceneHeaders[i].Dispose();
                        }
                        else
                        {
                            _PendingCleanups.Add(requestSceneHeaders[i]);
                        }
#else
                        requestSceneHeaders[i].Dispose();
#endif
                    }
                }
                entityManager.RemoveComponent<RequestSceneHeader>(_CleanupHeaderQuery);
            }
        }

        internal unsafe struct HeaderLoadResult : IDisposable
        {
            public HeaderLoadStatus Status;
            public UnsafeList<ResolvedSectionPath> SectionPaths;
            public BlobAssetReference<SceneMetaData> SceneMetaData;
            public BlobAssetOwner HeaderBlobOwner;

            public bool Success => Status==HeaderLoadStatus.Success;

            public void Dispose()
            {
                if (!Success)
                    return;
                SectionPaths.Dispose();
                HeaderBlobOwner.Release();
            }
        }

#if !UNITY_DOTSRUNTIME
        internal static unsafe HeaderLoadResult FinishHeaderLoad(RequestSceneHeader requestHeader, Hash128 sceneGUID, string sceneLoadDir)
        {
            var loadResult = requestHeader.HeaderData->HeaderLoadResult;
            if(!loadResult.Success)
                LogHeaderLoadError(loadResult.Status, sceneGUID);
            return loadResult;
        }
#else
        internal static unsafe HeaderLoadResult FinishHeaderLoad(RequestSceneHeader requestHeader, Hash128 sceneGUID, string sceneLoadDir)
        {
            var headerLoadResult = new HeaderLoadResult();
            if(requestHeader.Succeeded)
            {
                headerLoadResult.Status = HeaderLoadStatus.MissingFile;
            }

            requestHeader.GetFileData(out var headerData, out var headerSize);

            if(!BlobAssetReference<SceneMetaData>.TryReadInplace(headerData, headerSize, SceneMetaDataSerializeUtility.CurrentFileFormatVersion, out var sceneMetaDataBlobRef, out var numBytesRead))
            {
                headerLoadResult.Status = HeaderLoadStatus.WrongVersion;
                return headerLoadResult;
            }

            headerLoadResult.Status = HeaderLoadStatus.Success;

            headerLoadResult.SceneMetaData = sceneMetaDataBlobRef;
            ref var sceneMetaData = ref sceneMetaDataBlobRef.Value;

            //Load header blob batch
            var dataSize = sceneMetaData.HeaderBlobAssetBatchSize;
            void* blobAssetBatch = Memory.Unmanaged.Allocate(dataSize, 16, Allocator.Persistent);
            UnsafeUtility.MemCpy(blobAssetBatch, headerData + numBytesRead, dataSize);

            var headerBlobOwner =  new BlobAssetOwner(blobAssetBatch, dataSize);

            var sectionCount = sceneMetaData.Sections.Length;
            for (int i = 0; i < sectionCount; ++i)
            {
                sceneMetaData.Sections[i].BlobHeader =
                    sceneMetaData.Sections[i].BlobHeader.Resolve(headerBlobOwner);
            }

            headerLoadResult.HeaderBlobOwner = headerBlobOwner;
            headerLoadResult.SectionPaths = new UnsafeList<ResolvedSectionPath>(sectionCount, Allocator.TempJob);
            BuildSectionPathsForAssetBundles(ref headerLoadResult.SectionPaths, ref sceneMetaData, sceneGUID, sceneLoadDir);
            return headerLoadResult;
        }
#endif

#if !UNITY_DOTSRUNTIME

        internal class ManagedHeaderData
        {
            public string[] Paths;
        }

        internal unsafe struct HeaderData
        {
            public byte* Data;
            public ReadHandle ReadHandle;
            public FixedString512Bytes SceneLoadDir;
#if UNITY_EDITOR
            public GCHandle ManagedHeaderData;
#endif
            public JobHandle JobHandle;
            public ReadCommand ReadCommand;

            public HeaderLoadResult HeaderLoadResult;
        }

        unsafe struct DeserializeHeaderJob : IJob
        {
            [NativeDisableUnsafePtrRestriction]
            public HeaderData* HeaderData;
            public Hash128 SceneGUID;
            public void Execute()
            {
                ref var headerLoadResult = ref HeaderData->HeaderLoadResult;

                var loadStatus = HeaderData->ReadHandle.Status;
                var headerSize = HeaderData->ReadHandle.GetBytesRead();
                HeaderData->ReadHandle.Dispose();

                if (loadStatus == ReadStatus.Failed)
                {
                    headerLoadResult.Status = HeaderLoadStatus.MissingFile;
                    return;
                }

                var headerData = HeaderData->Data;
                if(!BlobAssetReference<SceneMetaData>.TryReadInplace(headerData, headerSize, SceneMetaDataSerializeUtility.CurrentFileFormatVersion, out var sceneMetaDataBlobRef, out var numBytesRead))
                {
                    headerLoadResult.Status = HeaderLoadStatus.WrongVersion;
                    return;
                }

                headerLoadResult.Status = HeaderLoadStatus.Success;

                headerLoadResult.SceneMetaData = sceneMetaDataBlobRef;
                ref var sceneMetaData = ref sceneMetaDataBlobRef.Value;

                //Load header blob batch
                var dataSize = sceneMetaData.HeaderBlobAssetBatchSize;
                void* blobAssetBatch = Memory.Unmanaged.Allocate(dataSize, 16, Allocator.Persistent);
                UnsafeUtility.MemCpy(blobAssetBatch, headerData + numBytesRead, dataSize);

                var headerBlobOwner =  new BlobAssetOwner(blobAssetBatch, dataSize);

                var sectionCount = sceneMetaData.Sections.Length;
                for (int i = 0; i < sectionCount; ++i)
                {
                    sceneMetaData.Sections[i].BlobHeader =
                        sceneMetaData.Sections[i].BlobHeader.Resolve(headerBlobOwner);
                }

                headerLoadResult.HeaderBlobOwner = headerBlobOwner;
                headerLoadResult.SectionPaths = new UnsafeList<ResolvedSectionPath>(sectionCount, Allocator.TempJob);
#if UNITY_EDITOR
                var managedHeaderData = (ManagedHeaderData)HeaderData->ManagedHeaderData.Target;
                BuildSectionPaths(ref headerLoadResult.SectionPaths, ref sceneMetaData, managedHeaderData.Paths);
#else
                BuildSectionPathsForAssetBundles(ref headerLoadResult.SectionPaths, ref sceneMetaData, SceneGUID, HeaderData->SceneLoadDir.ToString());
#endif
            }
        }
#endif

        internal static unsafe RequestSceneHeader CreateRequestSceneHeader(Hash128 sceneGUID, RequestSceneLoaded requestSceneLoaded, Hash128 artifactHash, string sceneLoadDir)
        {
#if UNITY_DOTSRUNTIME
            var sceneHeaderPath = EntityScenesPaths.GetLoadPath(sceneGUID, EntityScenesPaths.PathType.EntitiesHeader, -1, sceneLoadDir);
            var iohandle = IOService.RequestAsyncRead(sceneHeaderPath).m_Handle;
            return new RequestSceneHeader {IOHandle = iohandle};
#else
            var headerData = (HeaderData*)Memory.Unmanaged.Allocate(sizeof(HeaderData), 16, Allocator.Persistent);
            *headerData = default;

            headerData->SceneLoadDir = sceneLoadDir;
#if UNITY_EDITOR
            var managedHeaderData = new ManagedHeaderData();
            headerData->ManagedHeaderData = GCHandle.Alloc(managedHeaderData);
            AssetDatabaseCompatibility.GetArtifactPaths(artifactHash, out var paths);
            managedHeaderData.Paths = paths;
#endif

#if UNITY_EDITOR
            string sceneHeaderPath = EntityScenesPaths.GetHeaderPathFromArtifactPaths(managedHeaderData.Paths);
#else
            string sceneHeaderPath = EntityScenesPaths.GetLoadPath(sceneGUID, EntityScenesPaths.PathType.EntitiesHeader, -1, sceneLoadDir);
#endif
            var data = (byte*) Memory.Unmanaged.Allocate(SerializeUtility.MaxSubsceneHeaderSize, 64, Allocator.Persistent);
            headerData->ReadCommand = new ReadCommand
            {
                Size = SerializeUtility.MaxSubsceneHeaderSize, Offset = 0, Buffer = data
            };

            var handle = AsyncReadManager.Read(sceneHeaderPath, &headerData->ReadCommand, 1);

            headerData->JobHandle = default;
            headerData->ReadHandle = handle;
            headerData->Data = data;
            headerData->JobHandle = new DeserializeHeaderJob
            {
                HeaderData = headerData,
                SceneGUID = sceneGUID
            }.Schedule(handle.JobHandle);

            return new RequestSceneHeader {HeaderData = headerData};
#endif
        }

        public static void ScheduleHeaderLoadOnEntity(EntityManager EntityManager, Entity sceneEntity, Hash128 sceneGUID, RequestSceneLoaded requestSceneLoaded, Hash128 artifactHash, string sceneLoadDir)
        {
            EntityManager.AddComponentData(sceneEntity, new ResolvedSceneHash { ArtifactHash = artifactHash });
            EntityManager.AddComponentData(sceneEntity, CreateRequestSceneHeader(sceneGUID, requestSceneLoaded, artifactHash, sceneLoadDir));
        }

        internal static unsafe void BuildSectionPaths(ref UnsafeList<ResolvedSectionPath> sectionPaths, ref SceneMetaData sceneMetaData, Hash128 sceneGUID, RequestSceneHeader requestHeader, string sceneLoadDir)
        {
#if UNITY_EDITOR
            var managedHeaderData = (SceneHeaderUtility.ManagedHeaderData)requestHeader.HeaderData->ManagedHeaderData.Target;
            BuildSectionPaths(ref sectionPaths, ref sceneMetaData, managedHeaderData.Paths);
#else
            BuildSectionPathsForAssetBundles(ref sectionPaths, ref sceneMetaData, sceneGUID, sceneLoadDir);
#endif
        }

        internal static void BuildSectionPathsForAssetBundles(ref UnsafeList<ResolvedSectionPath> sectionPaths, ref SceneMetaData sceneMetaData, Hash128 sceneGUID, string sceneLoadDir)
        {
            sectionPaths.Resize(sceneMetaData.Sections.Length, NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < sceneMetaData.Sections.Length; ++i)
            {
                var sectionIndex = sceneMetaData.Sections[i].SubSectionIndex;
                var hybridPath = EntityScenesPaths.GetLoadPath(sceneGUID, EntityScenesPaths.PathType.EntitiesUnityObjectReferencesBundle, sectionIndex, sceneLoadDir);
                var scenePath = EntityScenesPaths.GetLoadPath(sceneGUID, EntityScenesPaths.PathType.EntitiesBinary, sectionIndex, sceneLoadDir);

                var sectionPath = new ResolvedSectionPath();
                sectionPath.ScenePath = scenePath;
                sectionPath.HybridPath = hybridPath;
                sectionPaths[i] = sectionPath;
            }
        }

#if UNITY_EDITOR
        internal static void BuildSectionPaths(ref UnsafeList<ResolvedSectionPath> sectionPaths, ref SceneMetaData sceneMetaData, string[] Paths)
        {
            sectionPaths.Resize(sceneMetaData.Sections.Length, NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < sceneMetaData.Sections.Length; ++i)
            {
                var sectionIndex = sceneMetaData.Sections[i].SubSectionIndex;
                var scenePath = EntityScenesPaths.GetLoadPathFromArtifactPaths(Paths, EntityScenesPaths.PathType.EntitiesBinary, sectionIndex);
                var hybridPath = EntityScenesPaths.GetLoadPathFromArtifactPaths(Paths, EntityScenesPaths.PathType.EntitiesUnityObjectReferences, sectionIndex);

                var sectionPath = new ResolvedSectionPath();
                sectionPath.ScenePath = scenePath;
                if(hybridPath != null)
                    sectionPath.HybridPath = hybridPath;
                sectionPaths[i] = sectionPath;
            }
        }
#endif

        internal enum HeaderLoadStatus
        {
            Success,
            MissingFile,
            WrongVersion
        }

        internal static void LogHeaderLoadError(HeaderLoadStatus status, Hash128 sceneGUID)
        {
            switch (status)
            {
                case HeaderLoadStatus.MissingFile:
#if UNITY_EDITOR
                    var scenePath = AssetDatabaseCompatibility.GuidToPath(sceneGUID);
                    var logPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(UnityEngine.Application.dataPath, "../Logs"));
                    Debug.LogError($"Loading Entity Scene failed because the entity header file couldn't be resolved. This might be caused by a failed import of the entity scene. Please take a look at the SubScene MonoBehaviour that references this scene or at the asset import worker log in {logPath}. scenePath={scenePath} guid={sceneGUID}");
#else
                    Debug.LogError($"Loading Entity Scene failed because the entity header file couldn't be resolved: guid={sceneGUID}.");
#endif
                    break;
                case HeaderLoadStatus.WrongVersion:
#if UNITY_EDITOR
                    Debug.LogError($"Loading Entity Scene failed because the entity header file was an old version: guid={sceneGUID}");
#else
                    Debug.LogError($"Loading Entity Scene failed because the entity header file was an old version: {sceneGUID}\nNOTE: In order to load SubScenes in the player you have to use the new BuildConfiguration asset based workflow to build & run your player.");
#endif
                    break;
            }
        }
    }
}
