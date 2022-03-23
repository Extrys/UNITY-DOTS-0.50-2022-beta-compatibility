using System;
using System.Collections.Generic;
#if !UNITY_DOTSRUNTIME
using System.Reflection;
#endif
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Unity.Assertions;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.IO.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;

// Remove this once DOTSRuntime can use Unity.Properties
[assembly: InternalsVisibleTo("Unity.TinyConversion")]
[assembly: InternalsVisibleTo("Unity.Entities.Runtime.Build")]

namespace Unity.Entities.Serialization
{
    struct StableArchetypeCompare : IComparer<IntPtr>
    {
        unsafe public int Compare(IntPtr x, IntPtr y)
        {
            var hashX = ((Archetype*)x)->StableHash;
            var hashY = ((Archetype*)y)->StableHash;

            return hashX.CompareTo(hashY);
        }
    }

    [GenerateBurstMonoInterop("SerializeUtility")]
    [BurstCompile]
    internal unsafe partial struct SerializeUtilityInterop
    {
        [BurstMonoInteropMethod(MakePublic = false)]
        internal static void _AllocateConsecutiveEntitiesForLoading(EntityComponentStore* store, int entityCount)
        {
            store->AllocateConsecutiveEntitiesForLoading(entityCount);
        }

        [BurstMonoInteropMethod(MakePublic = false)]
        internal static int _AllocAndQueueReadChunkCommands(long readOffset, int totalChunkCount, UnsafeList<SerializeUtility.MegaChunkInfo>* megaChunkInfo, UnsafeList<ReadCommand>* readCommands)
        {
            int chunkCountRead = 0;
            while (totalChunkCount != chunkCountRead)
            {
                if (EntityComponentStore.AllocateContiguousChunk(totalChunkCount - chunkCountRead, out var chunks, out var allocatedCount) != 0)
                {
                    return totalChunkCount;
                }

                ReadCommand cmd;
                cmd.Buffer = chunks;
                cmd.Offset = readOffset;
                cmd.Size = Chunk.kChunkSize * allocatedCount;
                readCommands->Add(cmd);

                megaChunkInfo->Add(new SerializeUtility.MegaChunkInfo((byte*)chunks, allocatedCount));
                chunkCountRead += allocatedCount;
                readOffset += Chunk.kChunkSize * allocatedCount;
            }

            return totalChunkCount;
        }

        [BurstMonoInteropMethod(MakePublic = false)]
        internal static void _AddExistingChunk(Chunk* chunk, int* sharedComponentIndices, byte* enabledBitsValuesForChunk, int* perComponentDisabledBitCount)
        {
            ChunkDataUtility.AddExistingChunk(chunk, sharedComponentIndices, enabledBitsValuesForChunk, perComponentDisabledBitCount);
        }

        [BurstMonoInteropMethod(MakePublic = false)]
        internal static void _ImportChunks(SerializeUtility.WorldDeserializationStatus* status, ref BurstableMemoryBinaryReader bufferReader, UnsafeList<EntityArchetype>* archetypes, int* sharedComponentArray, int numSharedComponents, int* sharedComponentRemap, UnsafeList<ArchetypeChunk>* blobAssetRefChunks, byte* componentEnabledBits, int* enabledBitsHierarchicalData, EntityComponentStore* ecs, UnsafeList<ArchetypeChunk>* chunksWithMetaChunkEntities)
        {
            var tc = bufferReader.ReadInt();
            var totalChunkCount = status->TotalChunkCount;
            var totalBlobAssetSize = status->BlobAssetSize;
            var blobAssetBuffer = status->BlobAssetBuffer;

            if (status->TotalChunkCount != tc)
            {
                throw new InvalidOperationException("Internal deserialization error: total chunk count doesn't match");
            }

            var curMegaChunkIndex = 0;
            var megaChunkInfo = status->MegaChunkInfoList;
            var chunkLeftToLoad = megaChunkInfo[0].MegaChunkSize;
            var chunk = (Chunk*) megaChunkInfo[0].MegaChunkAddress;
            var sharedComponentArraysIndex = 0;
            var enabledBitsForChunk = componentEnabledBits;
            var enabledBitsHierarchicalDataForChunk = enabledBitsHierarchicalData;
            var remapedSharedComponentValues = stackalloc int[EntityComponentStore.kMaxSharedComponentCount];
            for (int i = 0; i < totalChunkCount; ++i)
            {
                var archetype = chunk->Archetype = archetypes->ElementAt((int) chunk->Archetype).Archetype;
                var numSharedComponentsInArchetype = chunk->Archetype->NumSharedComponents;
                int* sharedComponentValueArray = sharedComponentArray + sharedComponentArraysIndex;

                for (int j = 0; j < numSharedComponentsInArchetype; ++j)
                {
                    // The shared component 0 is not part of the array, so an index equal to the array size is valid.
                    if (sharedComponentValueArray[j] > numSharedComponents)
                    {
                        throw new ArgumentException(
                            $"Archetype uses shared component at index {sharedComponentValueArray[j]} but only {numSharedComponents} are available, check if the shared scene has been properly loaded.");
                    }
                }

                RemapSharedComponentIndices(remapedSharedComponentValues, archetype, sharedComponentRemap, sharedComponentValueArray);
                sharedComponentArraysIndex += numSharedComponentsInArchetype;

                // Allocate additional heap memory for buffers that have overflown into the heap, and read their data.
                int bufferAllocationCount = bufferReader.ReadInt();
                if (bufferAllocationCount > 0)
                {
                    // TODO: PERF: Batch malloc interface.
                    for (int pi = 0; pi < bufferAllocationCount; ++pi)
                    {
                        var chunkOffset = bufferReader.ReadInt();
                        var allocSizeBytes = bufferReader.ReadInt();
                        var target = (BufferHeader*) OffsetFromPointer(chunk->Buffer, chunkOffset);

                        // TODO: Alignment
                        target->Pointer = (byte*) Memory.Unmanaged.Allocate(allocSizeBytes, 8, Allocator.Persistent);

                        bufferReader.ReadBytes(target->Pointer, allocSizeBytes);
                    }
                }

                if (totalBlobAssetSize != 0 && archetype->HasBlobAssetRefs)
                {
                    blobAssetRefChunks->Add(new ArchetypeChunk(chunk, ecs));
                    PatchBlobAssetsInChunkAfterLoad(chunk, (byte*) blobAssetBuffer);
                }

                chunk->SequenceNumber = ecs->AllocateSequenceNumber();

                SerializeUtilityInterop.AddExistingChunk(chunk, remapedSharedComponentValues, enabledBitsForChunk, enabledBitsHierarchicalDataForChunk);
                enabledBitsForChunk += archetype->TypesCount * (((archetype->ChunkCapacity + 63) / 64) * sizeof(ulong));;
                enabledBitsHierarchicalDataForChunk += archetype->TypesCount;

                if (chunk->metaChunkEntity != Entity.Null)
                {
                    chunksWithMetaChunkEntities->Add(new ArchetypeChunk(chunk, ecs));
                }

                chunk = (Chunk*) ((byte*) chunk + Chunk.kChunkSize);
                if (--chunkLeftToLoad == 0 && (i + 1) != totalChunkCount)
                {
                    chunk = (Chunk*) megaChunkInfo[++curMegaChunkIndex].MegaChunkAddress;
                    chunkLeftToLoad = megaChunkInfo[curMegaChunkIndex].MegaChunkSize;
                }
            }
        }
        static void RemapSharedComponentIndices(int* destValues, Archetype* archetype, int* remappedIndices, int* sourceValues)
        {
            int i = 0;
            for (int iType = 1; iType < archetype->TypesCount; ++iType)
            {
                int orderedIndex = archetype->TypeMemoryOrder[iType] - archetype->FirstSharedComponent;
                if (0 <= orderedIndex && orderedIndex < archetype->NumSharedComponents)
                    destValues[orderedIndex] = remappedIndices[sourceValues[i++]];
            }
        }
        static byte* OffsetFromPointer(void* ptr, int offset)
        {
            return ((byte*)ptr) + offset;
        }

        private static void PatchBlobAssetsInChunkAfterLoad(Chunk* chunk, byte* allBlobAssetData)
        {
            var archetype = chunk->Archetype;
            var typeCount = archetype->TypesCount;
            var entityCount = chunk->Count;
            for (var unordered_ti = 0; unordered_ti < typeCount; ++unordered_ti)
            {
                var ti = archetype->TypeMemoryOrder[unordered_ti];
                var type = archetype->Types[ti];
                if (type.IsZeroSized)
                    continue;

                ref readonly var ct = ref TypeManager.GetTypeInfo(type.TypeIndex);
                var blobAssetRefCount = ct.BlobAssetRefOffsetCount;
                if (blobAssetRefCount == 0)
                    continue;

                var blobAssetRefOffsets = TypeManager.GetBlobAssetRefOffsets(ct);
                var chunkBuffer = chunk->Buffer;
                int subArrayOffset = archetype->Offsets[ti];
                byte* componentArrayStart = OffsetFromPointer(chunkBuffer, subArrayOffset);

                if (type.IsBuffer)
                {
                    BufferHeader* header = (BufferHeader*)componentArrayStart;
                    int strideSize = archetype->SizeOfs[ti];
                    var elementSize = ct.ElementSize;

                    for (int bi = 0; bi < entityCount; ++bi)
                    {
                        var bufferStart = BufferHeader.GetElementPointer(header);
                        for (int ei = 0; ei < header->Length; ++ei)
                        {
                            byte* componentData = bufferStart + ei * elementSize;
                            for (int i = 0; i < blobAssetRefCount; ++i)
                            {
                                var offset = blobAssetRefOffsets[i].Offset;
                                var blobAssetRefPtr = (BlobAssetReferenceData*)(componentData + offset);
                                int value = (int)blobAssetRefPtr->m_Ptr;
                                byte* ptr = null;
                                if (value != -1)
                                {
                                    ptr = allBlobAssetData + value;
                                }
                                blobAssetRefPtr->m_Ptr = ptr;
                            }
                        }

                        header = (BufferHeader*)OffsetFromPointer(header, strideSize);
                    }
                }
                else if (blobAssetRefCount > 0)
                {
                    int size = archetype->SizeOfs[ti];
                    byte* end = componentArrayStart + size * entityCount;
                    for (var componentData = componentArrayStart; componentData < end; componentData += size)
                    {
                        for (int i = 0; i < blobAssetRefCount; ++i)
                        {
                            var offset = blobAssetRefOffsets[i].Offset;
                            var blobAssetRefPtr = (BlobAssetReferenceData*)(componentData + offset);
                            int value = (int)blobAssetRefPtr->m_Ptr;
                            byte* ptr = null;
                            if (value != -1)
                            {
                                ptr = allBlobAssetData + value;
                            }
                            blobAssetRefPtr->m_Ptr = ptr;
                        }
                    }
                }
            }
        }
    }

    public static partial class SerializeUtility
    {
        internal struct Settings
        {
            internal static readonly Settings Default = new Settings
            {
                SerializeComponentTypeNames = true
            };

            /// <summary>
            /// Serialize into the "Debug" Node a Node that contains the type name of each ComponentType
            /// </summary>
            /// <remarks>Set to true if you need a more accurate exception during the Deserialization about missing types</remarks>
            internal bool SerializeComponentTypeNames;

            internal bool RequiresDebugSection => SerializeComponentTypeNames;
            internal Entity PrefabRoot;
        }

#if !UNITY_DOTSRUNTIME
        /// <summary>
        /// Custom adapter used during serialization to add special type handling for <see cref="Entity"/> and <see cref="BlobAssetReference{T}"/>.
        /// </summary>
        unsafe class ManagedObjectWriterAdapter :
            Unity.Serialization.Binary.Adapters.IBinaryAdapter<Entity>,
            Unity.Serialization.Binary.Adapters.IBinaryAdapter<BlobAssetReferenceData>,
            Unity.Serialization.Binary.Adapters.IBinaryAdapter<UntypedWeakReferenceId>
        {
            public bool SerializeEntityReferences { get; set; }

            /// <summary>
            /// Entity remapping which is applied during serialization.
            /// </summary>
            readonly EntityRemapUtility.EntityRemapInfo* m_EntityRemapInfo;

            /// <summary>
            /// A map of <see cref="BlobAssetReferenceData"/> to index in the serialized batch.
            /// </summary>
            readonly NativeHashMap<BlobAssetPtr, int> m_BlobAssetMap;

            readonly NativeHashSet<UntypedWeakReferenceId> m_WeakAssetRefs;

            /// <summary>
            /// An array of absolute byte offsets for all blob assets within the serialized batch.
            /// </summary>
            readonly NativeArray<int> m_BlobAssetOffsets;

            public ManagedObjectWriterAdapter(
                EntityRemapUtility.EntityRemapInfo* entityRemapInfo,
                NativeHashMap<BlobAssetPtr, int> blobAssetMap,
                NativeArray<int> blobAssetOffsets,
                NativeHashSet<UntypedWeakReferenceId> weakAssetRefs)
            {
                SerializeEntityReferences = true;
                m_EntityRemapInfo = entityRemapInfo;
                m_BlobAssetMap = blobAssetMap;
                m_BlobAssetOffsets = blobAssetOffsets;
                m_WeakAssetRefs = weakAssetRefs;
            }

            void Unity.Serialization.Binary.Adapters.IBinaryAdapter<Entity>.Serialize(UnsafeAppendBuffer* writer, Entity value)
            {
                if (SerializeEntityReferences)
                {
                    value = EntityRemapUtility.RemapEntity(m_EntityRemapInfo, value);
                    writer->Add(value.Index);
                    writer->Add(value.Version);
                }
                else
                    throw new ArgumentException("Tried to serialized an Entity reference however entity reference serialization has been explicitly disabled.");
            }


            void Unity.Serialization.Binary.Adapters.IBinaryAdapter<BlobAssetReferenceData>.Serialize(UnsafeAppendBuffer* writer, BlobAssetReferenceData value)
            {
                var offset = -1;

                if (value.m_Ptr != null)
                {
                    if (!m_BlobAssetMap.TryGetValue(new BlobAssetPtr(value.Header), out var index))
                        throw new InvalidOperationException($"Trying to serialize a BlobAssetReference but the asset has not been included in the batch.");

                    offset = m_BlobAssetOffsets[index];
                }

                writer->Add(offset);
            }

            void Unity.Serialization.Binary.Adapters.IBinaryAdapter<UntypedWeakReferenceId>.Serialize(UnsafeAppendBuffer* writer, UntypedWeakReferenceId value)
            {
                if(m_WeakAssetRefs.IsCreated)
                    m_WeakAssetRefs.Add(value);
                writer->Add(value);
            }

            Entity Unity.Serialization.Binary.Adapters.IBinaryAdapter<Entity>.Deserialize(UnsafeAppendBuffer.Reader* reader)
                => throw new InvalidOperationException($"{nameof(ManagedObjectWriterAdapter)} should only be used for writing and never for reading!");

            BlobAssetReferenceData Unity.Serialization.Binary.Adapters.IBinaryAdapter<BlobAssetReferenceData>.Deserialize(UnsafeAppendBuffer.Reader* reader)
                => throw new InvalidOperationException($"{nameof(ManagedObjectWriterAdapter)} should only be used for writing and never for reading!");

            UntypedWeakReferenceId Unity.Serialization.Binary.Adapters.IBinaryAdapter<UntypedWeakReferenceId>.Deserialize(UnsafeAppendBuffer.Reader* reader)
                => throw new InvalidOperationException($"{nameof(ManagedObjectWriterAdapter)} should only be used for writing and never for reading!");
        }

        /// <summary>
        /// Custom adapter used during de-serialization to add special type handling for <see cref="Entity"/> and <see cref="BlobAssetReference{T}"/>.
        /// </summary>
        unsafe class ManagedObjectReaderAdapter :
            Unity.Serialization.Binary.Adapters.IBinaryAdapter<Entity>,
            Unity.Serialization.Binary.Adapters.IBinaryAdapter<BlobAssetReferenceData>
        {
            readonly byte* m_BlobAssetBatch;

            public ManagedObjectReaderAdapter(byte* blobAssetBatch)
            {
                m_BlobAssetBatch = blobAssetBatch;
            }

            void Unity.Serialization.Binary.Adapters.IBinaryAdapter<BlobAssetReferenceData>.Serialize(UnsafeAppendBuffer* writer, BlobAssetReferenceData value)
                => throw new InvalidOperationException($"{nameof(ManagedObjectReaderAdapter)} should only be used for reading and never for writing!");

            void Unity.Serialization.Binary.Adapters.IBinaryAdapter<Entity>.Serialize(UnsafeAppendBuffer* writer, Entity value)
                => throw new InvalidOperationException($"{nameof(ManagedObjectReaderAdapter)} should only be used for reading and never for writing!");

            Entity Unity.Serialization.Binary.Adapters.IBinaryAdapter<Entity>.Deserialize(UnsafeAppendBuffer.Reader* reader)
            {
                reader->ReadNext(out int index);
                reader->ReadNext(out int version);
                return new Entity {Index = index, Version = version};
            }

            BlobAssetReferenceData Unity.Serialization.Binary.Adapters.IBinaryAdapter<BlobAssetReferenceData>.Deserialize(UnsafeAppendBuffer.Reader* reader)
            {
                reader->ReadNext(out int offset);
                return offset == -1 ? default : new BlobAssetReferenceData {m_Ptr = m_BlobAssetBatch + offset};
            }
        }
#endif

        [StructLayout(LayoutKind.Sequential)]
        internal struct BufferPatchRecord
        {
            public int ChunkOffset;
            public int AllocSizeBytes;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct BlobAssetRefPatchRecord
        {
            public int ChunkOffset;
            public int BlobDataOffset;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct SharedComponentRecord
        {
            public ulong StableTypeHash;
            public int ComponentSize;
        }

        public const int CurrentFileFormatVersion = 73;
        internal const int MaxSubsceneHeaderSize = 1<<16;

        private static unsafe UnsafeList<EntityArchetype> ReadArchetypes(BinaryReader reader, NativeArray<int> types, ExclusiveEntityTransaction entityManager,
            out int totalEntityCount)
        {
            int archetypeCount = reader.ReadInt();
            var archetypes = new UnsafeList<EntityArchetype>(archetypeCount, Allocator.Temp);
            archetypes.Length = archetypeCount;
            totalEntityCount = 0;
            var tempComponentTypes = new NativeList<ComponentType>(Allocator.Temp);
            for (int i = 0; i < archetypeCount; ++i)
            {
                var archetypeEntityCount = reader.ReadInt();
                totalEntityCount += archetypeEntityCount;
                int archetypeComponentTypeCount = reader.ReadInt();
                tempComponentTypes.Clear();
                for (int iType = 0; iType < archetypeComponentTypeCount; ++iType)
                {
                    int typeHashIndexInFile = reader.ReadInt();
                    int typeHashIndexInFileNoFlags = typeHashIndexInFile & TypeManager.ClearFlagsMask;
                    int typeIndex = types[typeHashIndexInFileNoFlags];
                    if (TypeManager.IsChunkComponent(typeHashIndexInFile))
                        typeIndex = TypeManager.MakeChunkComponentTypeIndex(typeIndex);

                    tempComponentTypes.Add(ComponentType.FromTypeIndex(typeIndex));
                }

                archetypes[i] = entityManager.CreateArchetype((ComponentType*)tempComponentTypes.GetUnsafePtr(), tempComponentTypes.Length);
            }

            tempComponentTypes.Dispose();
            return archetypes;
        }

        private static NativeArray<int> ReadTypeArray(BinaryReader reader, DotsSerializationReader dotsReader)
        {
            var isNameLoaded = false;
            var hasTypesName = false;
            UnsafeHashMap<ulong, int> namesFromStableHash = default;
            StringTableReaderHandle stringStable = default;

            // Unknown type rarely occur, so we built the type hash/name map only at the first call of this method
            // If the debug section is not present in the file, we can't build the map and return false
            bool GetTypeName(ulong stableHash, out FixedString512Bytes name)
            {
                if (isNameLoaded == false)
                {
                    isNameLoaded = true;
                    var debugSectionNode = dotsReader.RootNode.FindNode(DebugSectionNodeType, 2);

                    if (debugSectionNode.IsValid)
                    {
                        hasTypesName = true;
                        stringStable = dotsReader.OpenStringTableNode(debugSectionNode.FindNode<DotsSerialization.StringTableNode>());
                        var typesNameNode = debugSectionNode.FindNode<DotsSerialization.TypeNamesNode>();
                        var typeCount = typesNameNode.As<DotsSerialization.TypeNamesNode>().TypeCount;
                        namesFromStableHash = new UnsafeHashMap<ulong, int>(typeCount, Allocator.Temp);
                        using (var readerHandle = typesNameNode.GetReaderHandle())
                        {
                            var r = readerHandle.Reader;
                            for (var i = 0; i < typeCount; i++)
                            {
                                namesFromStableHash.Add(r.ReadULong(), r.ReadInt());
                            }
                        }
                    }
                }

                if (!hasTypesName)
                {
                    name = default;
                    return false;
                }

                name = stringStable.GetString512(namesFromStableHash[stableHash]);
                return true;
            }

            {
                var typeCount = reader.ReadInt();

                var types = new NativeArray<int>(typeCount, Allocator.Temp);
                for (int i = 0; i < typeCount; ++i)
                {
                    var stableTypeHash = reader.ReadULong();
                    var typeIndex = TypeManager.GetTypeIndexFromStableTypeHash(stableTypeHash);
                    if (typeIndex < 0)
                    {
                        var res = GetTypeName(stableTypeHash, out var typeName);
                        if (res == false)
                        {
                            throw new ArgumentException($"Cannot find TypeIndex for type hash {stableTypeHash.ToString()}. Ensure your runtime depends on all assemblies defining the Component types your data uses.");
                        }

                        throw new ArgumentException($"Cannot find Type {typeName} for type hash {stableTypeHash.ToString()}. Ensure your runtime depends on all assemblies defining the Component types your data uses.");
                    }

                    types[i] = typeIndex;
                }

                if (hasTypesName)
                {
                    namesFromStableHash.Dispose();
                    stringStable.Dispose();
                }
                return types;
            }
        }

        internal static unsafe UnsafePtrList<Archetype> GetAllArchetypes(EntityComponentStore* entityComponentStore, Allocator allocator)
        {
            int count = 0;
            for (var i = 0; i < entityComponentStore->m_Archetypes.Length; ++i)
            {
                var archetype = entityComponentStore->m_Archetypes.Ptr[i];
                if (archetype->EntityCount > 0)
                    count++;
            }

            var archetypes = new UnsafePtrList<Archetype>(count, allocator);
            archetypes.Resize(count, NativeArrayOptions.UninitializedMemory);
            count = 0;
            for (var i = 0; i < entityComponentStore->m_Archetypes.Length; ++i)
            {
                var archetype = entityComponentStore->m_Archetypes.Ptr[i];
                if (archetype->EntityCount > 0)
                    archetypes.Ptr[count++] = entityComponentStore->m_Archetypes.Ptr[i];
            }

            NativeSortExtension.Sort((IntPtr*)archetypes.Ptr, archetypes.Length, default(StableArchetypeCompare));

            return archetypes;
        }

        public static void SerializeWorld(EntityManager entityManager, BinaryWriter writer)
        {
            var entityRemapInfos = new NativeArray<EntityRemapUtility.EntityRemapInfo>(entityManager.EntityCapacity, Allocator.Temp);
            SerializeWorldInternal(entityManager, writer, out var referencedObjects, entityRemapInfos, default, Settings.Default);
            entityRemapInfos.Dispose();
        }

        public static void SerializeWorld(EntityManager entityManager, BinaryWriter writer, out object[] referencedObjects)
        {
            var entityRemapInfos = new NativeArray<EntityRemapUtility.EntityRemapInfo>(entityManager.EntityCapacity, Allocator.Temp);
            SerializeWorldInternal(entityManager, writer, out referencedObjects, entityRemapInfos,default, Settings.Default);
            entityRemapInfos.Dispose();
        }

        public static unsafe void SerializeWorld(EntityManager entityManager, BinaryWriter writer, NativeArray<EntityRemapUtility.EntityRemapInfo> entityRemapInfos)
        {
            SerializeWorldInternal(entityManager, writer, out var referencedObjects, entityRemapInfos, default, Settings.Default);
        }

        public static void SerializeWorld(EntityManager entityManager, BinaryWriter writer, out object[] referencedObjects, NativeArray<EntityRemapUtility.EntityRemapInfo> entityRemapInfos)
        {
            SerializeWorldInternal(entityManager, writer, out referencedObjects, entityRemapInfos, default, Settings.Default);
        }

        /// <summary>
        /// Gets the entity representing the scene section with the index passed in.
        /// If createIfMissing is true the section entity is created if it doesn't already exist.
        /// Metadata components added to this section entity will be serialized into the entity scene header.
        /// At runtime these components will be added to the scene section entities when the scene is resolved.
        /// Only struct IComponentData components without BlobAssetReferences or Entity members are supported.
        /// </summary>
        /// <param name="sectionIndex">The section index for which to get the scene section entity</param>
        /// <param name="manager">The entity manager to which the entity belongs</param>
        /// <param name="cachedSceneSectionEntityQuery">The EntityQuery used to find the entity. Initially an null query should be passed in,
        /// the same query can the be passed in for subsequent calls to avoid recreating the query</param>
        /// <param name="createIfMissing">If true the section entity is created if it doesn't already exist. If false Entity.Null is returned for missing section entities</param>
        /// <returns>The entity representing the scene section</returns>
        public static Entity GetSceneSectionEntity(int sectionIndex, EntityManager manager, ref EntityQuery cachedSceneSectionEntityQuery, bool createIfMissing = true)
        {
            if (cachedSceneSectionEntityQuery == default)
                cachedSceneSectionEntityQuery = manager.CreateEntityQuery(ComponentType.ReadOnly<SectionMetadataSetup>());
            var sectionComponent = new SectionMetadataSetup {SceneSectionIndex = sectionIndex};
            cachedSceneSectionEntityQuery.SetSharedComponentFilter(sectionComponent);
            using (var sectionEntities = cachedSceneSectionEntityQuery.ToEntityArray(Allocator.TempJob))
            {
                if (sectionEntities.Length == 0)
                {
                    if (!createIfMissing)
                        return Entity.Null;
                    var sceneSectionEntity = manager.CreateEntity();
                    manager.AddSharedComponentData(sceneSectionEntity, sectionComponent);
                    return sceneSectionEntity;
                }

                if (sectionEntities.Length == 1)
                    return sectionEntities[0];

                throw new InvalidOperationException($"Multiple scene section entities with section index {sectionIndex} found");
            }
        }

        internal static Hash128 WorldFileType                      = new Hash128("7F090F7311DF4BA5BA3094AA33D0FAA7");
        internal static Hash128 WorldNodeType                      = new Hash128("E9FDFDEB4775443ABB236D4858232894");
        internal static Hash128 DebugSectionNodeType               = new Hash128("582BAFE96EC44A72ACD9CCBC850FDBC4");
        internal static Hash128 TypesNameStringTableNodeType       = new Hash128("4847374145504072849AAC7B1921E7FD");
        internal static Hash128 TypesNameNodeType                  = new Hash128("828510FAD38F4EA78CD908D4D780388B");
        internal static Hash128 ArchetypesNodeType                 = new Hash128("F5364E1CCB62466A9883F6F9554D4F0C");
        internal static Hash128 SharedAndManagedComponentsNodeType = new Hash128("D565355C5CF34C0DBBD4A06ADDA948B1");
        internal static Hash128 EnabledBitsNodeType                = new Hash128("5846E500EA614C1DA94AFB85AFD8F4F4");
        internal static Hash128 BlobAssetsNodeType                 = new Hash128("9A26954FF1ED4CC5A64E8AAD4F64773A");
        internal static Hash128 ChunksNodeType                     = new Hash128("2EA7CE3325F04D7A84CEAB46790C628A");
        internal static Hash128 BufferDataNodeType                 = new Hash128("E33124BFAC2649D792DE36E4611DDD70");
        internal static Hash128 PrefabNodeType                     = new Hash128("2A84A183583A4FAD8CB7105AE3C47598");

        internal unsafe struct MegaChunkInfo
        {
            public byte* MegaChunkAddress;
            public int MegaChunkSize;

            public MegaChunkInfo(byte* chunks, int size)
            {
                MegaChunkAddress = chunks;
                MegaChunkSize = size;
            }
        }

        internal unsafe struct WorldDeserializationStatus
        {
            internal UnsafeList<MegaChunkInfo> MegaChunkInfoList;
            public DotsSerializationReader.NodeHandle.PrefetchState ArchetypePrefetchState;
            [NativeDisableUnsafePtrRestriction]
            public void* BlobAssetBuffer;
            public int BlobAssetSize;
            public DotsSerializationReader.NodeHandle.PrefetchState SharedComponentPrefetchState;
            public DotsSerializationReader.NodeHandle.PrefetchState EnabledBitsPrefetchState;
            public DotsSerializationReader.NodeHandle.PrefetchState BufferElementPrefetchState;
            public DotsSerializationReader.NodeHandle.PrefetchState PrefabPrefetchState;
            public int TotalChunkCount;

            public void Dispose()
            {
                MegaChunkInfoList.Dispose();
                ArchetypePrefetchState.Dispose();
                SharedComponentPrefetchState.Dispose();
                EnabledBitsPrefetchState.Dispose();
                BufferElementPrefetchState.Dispose();
                PrefabPrefetchState.Dispose();
                Memory.Unmanaged.Free(BlobAssetBuffer, Allocator.Persistent);
            }
        }

        internal struct WorldDeserializationResult
        {
            internal Entity PrefabRoot;
        }

        unsafe static void FillReadCommands(DotsSerializationReader dotsReader, UnsafeList<ReadCommand>* readCommands, out WorldDeserializationStatus status)
        {
            status = new WorldDeserializationStatus();

            var worldNodeHandle = dotsReader.RootNode.FindNode(WorldNodeType);

            // Fetch Archetypes data from disk
            {
                var archetypesNodeHandle = worldNodeHandle.FindNode(ArchetypesNodeType);
                if (archetypesNodeHandle.IsValid == false)
                {
                    throw new InvalidOperationException($"Binary entity scene file must contain a ArchetypesNodeType");
                }

                status.ArchetypePrefetchState = archetypesNodeHandle.PrefetchRawDataRead(out var rc);
                readCommands->Add(rc);
            }

            // Read BlobAsset from disk to final memory buffer
            {
                if (worldNodeHandle.TryFindNode(BlobAssetsNodeType, out var blobAssetNodeHandle))
                {
                    status.BlobAssetSize = (int)blobAssetNodeHandle.DataSize;
                    status.BlobAssetBuffer = Memory.Unmanaged.Allocate(status.BlobAssetSize, 16, Allocator.Persistent);

                    ReadCommand rc = default;
                    rc.Buffer = status.BlobAssetBuffer;
                    rc.Offset = blobAssetNodeHandle.DataStartingOffset;
                    rc.Size = status.BlobAssetSize;
                    readCommands->Add(rc);
                }
            }

            // Fetch Shared & Managed component data from disk
            {
                var sharedComponentsNodeHandle = worldNodeHandle.FindNode(SharedAndManagedComponentsNodeType);
                if (sharedComponentsNodeHandle.IsValid == false)
                {
                    throw new InvalidOperationException($"Binary entity scene file must contain a SharedAndManagedComponentsNodeType");
                }
                status.SharedComponentPrefetchState = sharedComponentsNodeHandle.PrefetchRawDataRead(out var rc);
                readCommands->Add(rc);
            }

            // Read Enabled bits data from disk
            {
                var enabledBitsNodeHandle = worldNodeHandle.FindNode(EnabledBitsNodeType);
                if (enabledBitsNodeHandle.IsValid == false)
                {
                    throw new InvalidOperationException($"Binary entity scene file must contain an EnabledBitsNodeType");
                }
                status.EnabledBitsPrefetchState = enabledBitsNodeHandle.PrefetchRawDataRead(out var rc);
                readCommands->Add(rc);
            }

            // Read Chunk from disk to final memory buffer
            {
                var chunksNodeHandle = worldNodeHandle.FindNode(ChunksNodeType);
                if (chunksNodeHandle.IsValid == false)
                {
                    throw new InvalidOperationException($"Binary entity scene file must contain a ChunksNodeType");
                }

                var totalChunkCount = (int)(chunksNodeHandle.DataSize / Chunk.kChunkSize);

                long readOffset = chunksNodeHandle.DataStartingOffset;

                var megaChunkInfo = new UnsafeList<MegaChunkInfo>(8, Allocator.Persistent);

                if (SerializeUtilityInterop.AllocAndQueueReadChunkCommands(readOffset, totalChunkCount, &megaChunkInfo, readCommands) != totalChunkCount)
                {
                    throw new Exception("Contiguous Chunk allocation failed");
                }

                status.MegaChunkInfoList = megaChunkInfo;
                status.TotalChunkCount = totalChunkCount;
            }

            // Fetch Buffer Element data from disk
            {
                var bufferDataNodeHandle = worldNodeHandle.FindNode(BufferDataNodeType);
                if (bufferDataNodeHandle.IsValid == false)
                {
                    throw new InvalidOperationException($"Binary entity scene file must contain a BufferDataNodeType");
                }
                status.BufferElementPrefetchState = bufferDataNodeHandle.PrefetchRawDataRead(out var rc);
                readCommands->Add(rc);
            }

            // Fetch Prefab
            {
                if (worldNodeHandle.TryFindNode(PrefabNodeType, out var prefabNodeHandle))
                {
                    status.PrefabPrefetchState = prefabNodeHandle.PrefetchRawDataRead(out var rc);
                    readCommands->Add(rc);
                }
            }
        }


        internal static unsafe ReadHandle BeginDeserializeWorld(string serializationFilePathName, DotsSerializationReader dotsReader, out WorldDeserializationStatus status, out UnsafeList<ReadCommand> readCommands)
        {
            var rc = new UnsafeList<ReadCommand>(1, Allocator.Persistent);

            FillReadCommands(dotsReader, &rc, out status);
            readCommands = rc;

#if ENABLE_PROFILER && UNITY_2020_2_OR_NEWER
            // When AsyncReadManagerMetrics are available, mark up the file read for more informative IO metrics.
            // Metrics can be retrieved by AsyncReadManagerMetrics.GetMetrics
            var res = AsyncReadManager.Read(serializationFilePathName, readCommands.Ptr, (uint)readCommands.Length, subsystem: AssetLoadingSubsystem.EntitiesScene);
#else
            var res = AsyncReadManager.Read(serializationFilePathName, readCommands.Ptr, (uint)readCommands.Length);
#endif
            return res;
        }

        internal static unsafe void EndDeserializeWorld(ExclusiveEntityTransaction manager, DotsSerializationReader dotsReader, ref WorldDeserializationStatus status, out WorldDeserializationResult deserializationResult ,object[] unityObjects = null)
        {
            deserializationResult = default;
            var access = manager.EntityManager.GetCheckedEntityDataAccess();
            var ecs = access->EntityComponentStore;
            var mcs = access->ManagedComponentStore;

            if (ecs->CountEntities() != 0)
            {
                throw new ArgumentException(
                    $"DeserializeWorld can only be used on completely empty EntityManager. Please create a new empty World and use EntityManager.MoveEntitiesFrom to move the loaded entities into the destination world instead.");
            }

            int totalEntityCount;
            UnsafeList<EntityArchetype> archetypes;
            NativeArray<int> sharedComponentArray;
            int numSharedComponents;
            NativeArray<int> sharedComponentRemap;
            UnsafeList<ArchetypeChunk> blobAssetRefChunks;
            var blobAssetOwner = default(BlobAssetOwner);
            NativeArray<int> types;
#if !UNITY_DOTSRUNTIME
            UnsafeAppendBuffer sharedAndManagedBuffer;
#endif
            // Read Archetypes
            using (var stream = status.ArchetypePrefetchState.CreateStream())
            {
                types = ReadTypeArray(stream, dotsReader);
                archetypes = ReadArchetypes(stream, types, manager, out totalEntityCount);
            }
            types.Dispose();

            // Read BlobAssets
            var totalBlobAssetSize = status.BlobAssetSize;
            var blobAssetBuffer = status.BlobAssetBuffer;
            if (totalBlobAssetSize != 0)
            {
                blobAssetOwner = new BlobAssetOwner(blobAssetBuffer, status.BlobAssetSize);
            }
            blobAssetRefChunks = new UnsafeList<ArchetypeChunk>(32, Allocator.Temp);

            // Read Shared and Managed components
            using (var reader = status.SharedComponentPrefetchState.CreateStream())
            {
                numSharedComponents = ReadSharedComponentMetadata(reader, out sharedComponentArray, out var sharedComponentRecordArray);
                sharedComponentRemap = new NativeArray<int>(numSharedComponents + 1, Allocator.Temp);

                int sharedAndManagedDataSize = reader.ReadInt();
                int managedComponentCount = reader.ReadInt();
#if !UNITY_DOTSRUNTIME
                sharedAndManagedBuffer = new UnsafeAppendBuffer(sharedAndManagedDataSize, 16, Allocator.Temp);
                sharedAndManagedBuffer.ResizeUninitialized(sharedAndManagedDataSize);
                reader.ReadBytes(sharedAndManagedBuffer.Ptr, sharedAndManagedDataSize);
                var sharedAndManagedStream = sharedAndManagedBuffer.AsReader();
                var managedDataReader = new ManagedObjectBinaryReader(&sharedAndManagedStream, (UnityEngine.Object[])unityObjects);
                managedDataReader.AddAdapter(new ManagedObjectReaderAdapter((byte*) blobAssetBuffer));
                ReadSharedComponents(manager, managedDataReader, sharedComponentRemap, sharedComponentRecordArray);
                mcs.ResetManagedComponentStoreForDeserialization(managedComponentCount, ref *ecs);

                // Deserialize all managed components
                for (int i = 0; i < managedComponentCount; ++i)
                {
                    ulong typeHash = sharedAndManagedStream.ReadNext<ulong>();
                    int typeIndex = TypeManager.GetTypeIndexFromStableTypeHash(typeHash);
                    Type managedType = TypeManager.GetTypeInfo(typeIndex).Type;
                    object obj = managedDataReader.ReadObject(managedType);
                    mcs.SetManagedComponentValue(i + 1, obj);
                }
#else
                ReadSharedComponents(manager, reader, sharedAndManagedDataSize, sharedComponentRemap, sharedComponentRecordArray);
#endif
#if !UNITY_DOTSRUNTIME
                sharedAndManagedBuffer.Dispose();
#endif
            }

            // Get pointers to enabled bits data
            var enabledBitsData = (byte*)status.EnabledBitsPrefetchState._buffer;
            var enabledBitsSizeInBytes = *((int*) enabledBitsData);
            enabledBitsData += sizeof(int);
            var enabledBitsHierarchicalData = (int*)(enabledBitsData + enabledBitsSizeInBytes + sizeof(int));

            // Chunk initialization
            SerializeUtilityInterop.AllocateConsecutiveEntitiesForLoading(ecs, totalEntityCount);

            // Read Chunk Buffer data elements
            var totalChunkCount = status.TotalChunkCount;
            var chunksWithMetaChunkEntities = new UnsafeList<ArchetypeChunk>(totalChunkCount, Allocator.Temp);
            using (var bufferReader = (BurstableMemoryBinaryReader)status.BufferElementPrefetchState.CreateStream())
            {
                var reader = bufferReader;

                SerializeUtilityInterop.ImportChunks((WorldDeserializationStatus*)UnsafeUtility.AddressOf(ref status),
                    ref reader,
                    &archetypes,
                    (int*)sharedComponentArray.GetUnsafePtr(),
                    numSharedComponents,
                    (int*)sharedComponentRemap.GetUnsafePtr(),
                    &blobAssetRefChunks,
                    enabledBitsData,
                    enabledBitsHierarchicalData,
                    ecs,
                    &chunksWithMetaChunkEntities);

                sharedComponentRemap.Dispose();
                status.BlobAssetBuffer = null; // BlobAssetOwner takes ownership of BlobAssetBuffer
                for (int i = 0; i < chunksWithMetaChunkEntities.Length; ++i)
                {
                    var chunkw = chunksWithMetaChunkEntities[i].m_Chunk;
                    manager.SetComponentData(chunkw->metaChunkEntity, new ChunkHeader {ArchetypeChunk = chunksWithMetaChunkEntities[i]});
                }
                chunksWithMetaChunkEntities.Dispose();
                archetypes.Dispose();
            }

            mcs.Playback(ref ecs->ManagedChangesTracker);
            ecs->InvalidateChunkListCacheForChangedArchetypes();

            if (totalBlobAssetSize != 0)
            {
                var barc = new NativeArray<ArchetypeChunk>(blobAssetRefChunks.Length, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
                UnsafeUtility.MemCpy(barc.GetUnsafePtr(), blobAssetRefChunks.Ptr, sizeof(ArchetypeChunk)*blobAssetRefChunks.Length);
                manager.AddSharedComponent(barc, blobAssetOwner);
                blobAssetRefChunks.Dispose();
                barc.Dispose();
            }
            if (status.PrefabPrefetchState._buffer != null)
            {
                if(status.PrefabPrefetchState._size != sizeof(Entity))
                    throw new InvalidOperationException($"Internal deserialization error: Unexpected size of PrefabPrefetchState Expected:{sizeof(Entity)} Actual:{status.PrefabPrefetchState._size}");
                deserializationResult.PrefabRoot = *(Entity*)status.PrefabPrefetchState._buffer;
            }
            status.Dispose();
            blobAssetOwner.Release();

            // Chunks have now taken over ownership of the shared components (reference counts have been added)
            // so remove the ref that was added on deserialization
            for (int i = 0; i < numSharedComponents; ++i)
            {
                access->RemoveSharedComponentReference(i + 1);
            }
        }

        public static unsafe void DeserializeWorld(ExclusiveEntityTransaction manager, BinaryReader reader, object[] unityObjects = null)
        {
            DeserializeWorld(manager, reader, out _, unityObjects);
        }

        internal static unsafe void DeserializeWorld(ExclusiveEntityTransaction manager, BinaryReader reader, out WorldDeserializationResult deserializationResult, object[] unityObjects = null)
        {

#if !UNITY_DOTSRUNTIME
            if (reader is StreamBinaryReader)
            {
                using (var dotsReader = DotsSerialization.CreateReader(reader))
                {
                    var filePath = ((StreamBinaryReader) reader).FilePath;
                    var readHandle = BeginDeserializeWorld(filePath, dotsReader, out var status, out var readCommands);
                    readHandle.JobHandle.Complete();
                    readCommands.Dispose();
                    EndDeserializeWorld(manager, dotsReader, ref status, out deserializationResult, unityObjects);
                    return;
                }
            }
#endif
            using(var dotsReader = DotsSerialization.CreateReader(reader))
            {
                var readCommands = new UnsafeList<ReadCommand>(1, Allocator.Temp);
                FillReadCommands(dotsReader, &readCommands, out var status);
                for (int i = 0; i < readCommands.Length; ++i)
                {
                    reader.Position = readCommands[i].Offset;
                    reader.ReadBytes(readCommands[i].Buffer, (int)readCommands[i].Size);
                }
                EndDeserializeWorld(manager, dotsReader, ref status, out deserializationResult, unityObjects);
            }
        }

        internal static void SerializeWorldInternal(EntityManager entityManager, BinaryWriter writer, out object[] referencedObjects,
            NativeArray<EntityRemapUtility.EntityRemapInfo> entityRemapInfos, bool isDOTSRuntime = false)
        {
            SerializeWorldInternal(entityManager, writer, out referencedObjects, entityRemapInfos, default, new Settings(), isDOTSRuntime);
        }

        internal static unsafe BlobAssetReference<DotsSerialization.BlobHeader> SerializeWorldInternal(EntityManager entityManager, BinaryWriter writer, out object[] referencedObjects, NativeArray<EntityRemapUtility.EntityRemapInfo> entityRemapInfos, NativeHashSet<UntypedWeakReferenceId> weakAssetRefs, Settings settings, bool isDOTSRuntime = false, bool buildBlobHeader = false)
        {
            BlobAssetReference<DotsSerialization.BlobHeader> blobHeader = default;
            using (var dotsWriter = DotsSerialization.CreateWriter(writer, WorldFileType, "EntityBinaryFile"))
            {
                using (dotsWriter.CreateNode<DotsSerialization.FolderNode>(WorldNodeType))
                {
                    var access = entityManager.GetCheckedEntityDataAccess();
                    var entityComponentStore = access->EntityComponentStore;
                    var mcs = access->ManagedComponentStore;

                    // Write Debug Section if needed
                    if (settings.RequiresDebugSection)
                    {
                        using (dotsWriter.CreateNode<DotsSerialization.FolderNode>(DebugSectionNodeType))
                        {
                            WriteTypeNames(dotsWriter, entityComponentStore);
                        }
                    }

                    // Write the archetype node
                    using(var archetypeArray = WriteArchetypesNode(dotsWriter, entityComponentStore))
                    {
                        // Build ShareComponent mapping tables
                        BuildSharedComponents(archetypeArray, out var sharedComponentArrays,
                            out var sharedComponentsToSerialize);

                        // Write the BlobAssets
                        WriteBlobAssetNode(entityManager, isDOTSRuntime, dotsWriter, sharedComponentsToSerialize,
                            archetypeArray, out var blobAssetMap, out var blobAssetOffsets);
                        using (blobAssetMap)
                        using (blobAssetOffsets)
                        {

                            var totalChunkCount = GenerateRemapInfo(entityManager, archetypeArray, entityRemapInfos);

                            // Write Shared and Managed components
                            referencedObjects = WriteSharedAndManagedComponents(entityManager,
                                entityRemapInfos,
                                isDOTSRuntime,
                                dotsWriter,
                                sharedComponentArrays,
                                archetypeArray,
                                sharedComponentsToSerialize,
                                blobAssetMap,
                                blobAssetOffsets,
                                weakAssetRefs);

                            WriteEnabledBits(dotsWriter, archetypeArray);

                            //TODO: ensure chunks are defragged?
                            using(var bufferPatches = new NativeList<BufferPatchRecord>(128, Allocator.Temp))
                            using(var bufferDataList = new NativeList<IntPtr>(128, Allocator.Temp))
                            using(var bufferPatchesCountPerChunk = new NativeList<int>(128, Allocator.Temp))
                            {
                                var stackBytes = stackalloc byte[Chunk.kChunkSize];
                                var tempChunk = (Chunk*) stackBytes;

                                // Write chunks
                                WriteChunks(entityRemapInfos, dotsWriter, archetypeArray, tempChunk, mcs, blobAssetOffsets,
                                    blobAssetMap, bufferPatches, bufferPatchesCountPerChunk, bufferDataList, weakAssetRefs);

                                var j = 0;
                                for (int i = 0; i < bufferPatchesCountPerChunk.Length; i++)
                                {
                                    j += bufferPatchesCountPerChunk[i];
                                }

                                // Write Buffer Data Elements heaps
                                WriteBufferDataElements(dotsWriter, totalChunkCount, archetypeArray,
                                    bufferPatchesCountPerChunk,
                                    bufferPatches, bufferDataList);
                            }

                            var prefabRoot = EntityRemapUtility.RemapEntity(ref entityRemapInfos, settings.PrefabRoot);
                            if (prefabRoot != Entity.Null)
                            {
                                WritePrefabNode(dotsWriter, prefabRoot);
                            }
                        }
                    }

                    // NB if we are not building a blob header, the header will be written automatically when the
                    // writer is disposed.
                    if (buildBlobHeader)
                        blobHeader = dotsWriter.WriteHeaderAndBlob();
                }
            }

            return blobHeader;
        }

        private static unsafe void WriteBufferDataElements(DotsSerializationWriter dotsWriter, int totalChunkCount,
            UnsafePtrList<Archetype> archetypeArray, NativeList<int> bufferPatchesCountPerChunk,
            NativeList<BufferPatchRecord> bufferPatches, NativeList<IntPtr> bufferDataList)
        {
            using (var chunkNode = dotsWriter.CreateNode<DotsSerialization.RevisionedRawDataNode>(BufferDataNodeType))
            using (var writerHandle = chunkNode.GetWriterHandle())
            {
                chunkNode.NodeHeader.Revision = 1;

                var w = writerHandle.Writer;
                w.Write(totalChunkCount);
                var curChunkIndex = 0;
                var curRecordStartIndex = 0;
                var bufferDataRecordIndex = 0;

                for (int a = 0; a < archetypeArray.Length; ++a)
                {
                    var archetype = archetypeArray.Ptr[a];

                    for (var ci = 0; ci < archetype->Chunks.Count; ++ci)
                    {
                        // Write the count of patch records for this chunk
                        var recordCount = bufferPatchesCountPerChunk[curChunkIndex++];
                        w.Write(recordCount);

                        if (recordCount > 0)
                        {
                            // Write heap backed data for each required patch.
                            // TODO: PERF: Investigate static-only deserialization could manage one block and mark in pointers somehow that they are not individual
                            for (int i = 0; i < recordCount; ++i)
                            {
                                var patch = bufferPatches[curRecordStartIndex + i];
                                var bufferData = (void*)bufferDataList[bufferDataRecordIndex++];
                                w.Write(patch.ChunkOffset);
                                w.Write(patch.AllocSizeBytes);
                                w.WriteBytes(bufferData, patch.AllocSizeBytes);
                                Memory.Unmanaged.Free(bufferData, Allocator.TempJob);
                            }
                            curRecordStartIndex += recordCount;
                        }
                    }
                }
            }
        }

        private static unsafe void WriteChunks(NativeArray<EntityRemapUtility.EntityRemapInfo> entityRemapInfos,
            DotsSerializationWriter dotsWriter,
            UnsafePtrList<Archetype> archetypeArray,
            Chunk* tempChunk,
            ManagedComponentStore mcs,
            NativeArray<int> blobAssetOffsets,
            NativeHashMap<BlobAssetPtr, int> blobAssetMap,
            NativeList<BufferPatchRecord> bufferPatches,
            NativeList<int> bufferPatchesCountPerChunk,
            NativeList<IntPtr> bufferDataList,
            NativeHashSet<UntypedWeakReferenceId> weakAssetRefs)
        {
            using (var chunkNode = dotsWriter.CreateNode<DotsSerialization.RevisionedRawDataNode>(ChunksNodeType))
            using (var writerHandle = chunkNode.GetWriterHandle())
            {
                chunkNode.NodeHeader.Revision = 1;

                var w = writerHandle.Writer;
                //w.Write(totalChunkCount);

                int currentManagedComponentIndex = 1;
                for (int a = 0; a < archetypeArray.Length; ++a)
                {
                    var archetype = archetypeArray.Ptr[a];

                    for (var ci = 0; ci < archetype->Chunks.Count; ++ci)
                    {
                        var chunk = archetype->Chunks[ci];

                        UnsafeUtility.MemCpy(tempChunk, chunk, Chunk.kChunkSize);
                        tempChunk->metaChunkEntity = EntityRemapUtility.RemapEntity(ref entityRemapInfos, tempChunk->metaChunkEntity);

                        // Prevent patching from touching buffers allocated memory
                        BufferHeader.PatchAfterCloningChunk(tempChunk);
                        PatchManagedComponentIndices(tempChunk, archetype, ref currentManagedComponentIndex, mcs);

                        byte* tempChunkBuffer = tempChunk->Buffer;
                        EntityRemapUtility.PatchEntities(archetype->ScalarEntityPatches, archetype->ScalarEntityPatchCount, archetype->BufferEntityPatches, archetype->BufferEntityPatchCount,
                            tempChunkBuffer, tempChunk->Count, ref entityRemapInfos);
                        if (archetype->HasBlobAssetRefs)
                            PatchBlobAssetsInChunkBeforeSave(tempChunk, chunk, blobAssetOffsets, blobAssetMap);

                        if (archetype->HasWeakAssetRefs)
                            GetWeakAssetRefsInChunk(tempChunk, weakAssetRefs);

                        ClearChunkHeaderComponents(tempChunk);
                        ChunkDataUtility.MemsetUnusedChunkData(tempChunk, 0);
                        var startPatchesIndex = bufferPatches.Length;
                        FillBufferPatchRecordsAndClearBufferPointer(tempChunk, bufferPatches, bufferDataList);

                        var recordCount = bufferPatches.Length - startPatchesIndex;
                        bufferPatchesCountPerChunk.Add(recordCount);

                        tempChunk->Archetype = (Archetype*) a;

                        w.WriteBytes(tempChunk, Chunk.kChunkSize);
                    }
                }
            }
        }

        private static unsafe object[] WriteSharedAndManagedComponents(EntityManager entityManager, NativeArray<EntityRemapUtility.EntityRemapInfo> entityRemapInfos, bool isDOTSRuntime, DotsSerializationWriter dotsWriter,
            NativeArray<int> sharedComponentArrays, UnsafePtrList<Archetype> archetypeArray, int[] sharedComponentsToSerialize, NativeHashMap<BlobAssetPtr, int> blobAssetMap, NativeArray<int> blobAssetOffsets,
			 NativeHashSet<UntypedWeakReferenceId> weakAssetRefs)
        {
            object[] referencedObjects;
            using (var sharedComponentsNode = dotsWriter.CreateNode<DotsSerialization.RevisionedRawDataNode>(SharedAndManagedComponentsNodeType))
            using (var writerHandle = sharedComponentsNode.GetWriterHandle())
            {
                var w = writerHandle.Writer;
                w.Write(sharedComponentArrays.Length);
                w.WriteArray(sharedComponentArrays);
                sharedComponentArrays.Dispose();

                referencedObjects = null;

                WriteSharedAndManagedComponents(
                    entityManager,
                    archetypeArray,
                    sharedComponentsToSerialize,
                    w,
                    out referencedObjects,
                    isDOTSRuntime,
                    (EntityRemapUtility.EntityRemapInfo*) entityRemapInfos.GetUnsafePtr(),
                    blobAssetMap,
                    blobAssetOffsets,
                    weakAssetRefs);
            }

            return referencedObjects;
        }

        private static unsafe void WriteEnabledBits(DotsSerializationWriter dotsWriter, UnsafePtrList<Archetype> archetypeArray)
        {
            var enabledBitsDataSizeInBytes = 0;
            var enabledBitsHierarchicalDataSizeInBytes = 0;
            for (int archetypeIndex = 0; archetypeIndex < archetypeArray.Length; ++archetypeIndex)
            {
                var archetype = archetypeArray[archetypeIndex];
                enabledBitsDataSizeInBytes += archetype->Chunks.Count * archetype->TypesCount * (((archetype->ChunkCapacity + 63) / 64) * sizeof(ulong));
                enabledBitsHierarchicalDataSizeInBytes += archetype->TypesCount * archetype->Chunks.Count * sizeof(int);
            }

            using (var enabledBitsNode = dotsWriter.CreateNode<DotsSerialization.RevisionedRawDataNode>(EnabledBitsNodeType))
            using (var writerHandle = enabledBitsNode.GetWriterHandle())
            {
                var w = writerHandle.Writer;

                w.Write(enabledBitsDataSizeInBytes);
                for (int archetypeIndex = 0; archetypeIndex < archetypeArray.Length; ++archetypeIndex)
                {
                    var archetype = archetypeArray[archetypeIndex];
                    var chunks = archetype->Chunks;

                    var bitsPtr = chunks.GetPointerToComponentEnabledArrayForArchetype();
                    var bitsSizeInBytesForArchetype = chunks.Count * archetype->TypesCount * (((archetype->ChunkCapacity + 63) / 64) * sizeof(ulong));
                    w.WriteBytes(bitsPtr, bitsSizeInBytesForArchetype);
                }

                w.Write(enabledBitsHierarchicalDataSizeInBytes);
                for (int archetypeIndex = 0; archetypeIndex < archetypeArray.Length; ++archetypeIndex)
                {
                    var archetype = archetypeArray[archetypeIndex];
                    var chunks = archetype->Chunks;

                    var hierarchicalDataPtr = chunks.GetPointerToChunkDisabledCountForArchetype();
                    var hierarchicalDataSizeInBytesForArchetype = archetype->TypesCount * chunks.Count * sizeof(int);
                    w.WriteBytes(hierarchicalDataPtr, hierarchicalDataSizeInBytesForArchetype);
                }
            }
        }

        private static void WriteBlobAssetNode(EntityManager entityManager, bool isDOTSRuntime, DotsSerializationWriter dotsWriter, int[] sharedComponentsToSerialize,
            UnsafePtrList<Archetype> archetypeArray, out NativeHashMap<BlobAssetPtr, int> blobAssetMap, out NativeArray<int> blobAssetOffsets)
        {
            using (var blobAssetNode = dotsWriter.CreateNode<DotsSerialization.RevisionedRawDataNode>(BlobAssetsNodeType))
            using (var writerHandle = blobAssetNode.GetWriterHandle())
            {
                blobAssetNode.NodeHeader.Revision = 1;
                GatherAllUsedBlobAssets(entityManager, sharedComponentsToSerialize, isDOTSRuntime, archetypeArray, out var blobAssets, out blobAssetMap);
                WriteBlobAssetBatch(writerHandle.Writer, blobAssets, out blobAssetOffsets);
                blobAssets.Dispose();
            }
        }

        private static unsafe void WritePrefabNode(DotsSerializationWriter dotsWriter, Entity prefabRoot)
        {
            using (var prefabNode = dotsWriter.CreateNode<DotsSerialization.RevisionedRawDataNode>(PrefabNodeType))
            using (var writerHandle = prefabNode.GetWriterHandle())
            {
                prefabNode.NodeHeader.Revision = 1;
                writerHandle.Writer.WriteBytes(&prefabRoot, sizeof(Entity));
            }
        }

        internal unsafe static int CalculateBlobAssetBatchTotalSize(NativeArray<BlobAssetPtr> blobAssets, out NativeArray<int> blobAssetOffsets)
        {
            blobAssetOffsets = new NativeArray<int>(blobAssets.Length, Allocator.Temp);
            int totalBlobAssetSize = sizeof(BlobAssetBatch);

            for (int i = 0; i < blobAssets.Length; ++i)
            {
                totalBlobAssetSize += sizeof(BlobAssetHeader);
                blobAssetOffsets[i] = totalBlobAssetSize;
                totalBlobAssetSize += Align16(blobAssets[i].Header->Length);
            }

            return totalBlobAssetSize;
        }

        internal static unsafe void WriteBlobAssetBatch(BinaryWriter writer, NativeArray<BlobAssetPtr> blobAssets, int totalBlobAssetBatchSize)
        {
            var blobAssetBatch = BlobAssetBatch.CreateForSerialize(blobAssets.Length, totalBlobAssetBatchSize);
            writer.WriteBytes(&blobAssetBatch, sizeof(BlobAssetBatch));
            var zeroBytes = int4.zero;
            for (int i = 0; i < blobAssets.Length; ++i)
            {
                var blobAssetLength = blobAssets[i].Header->Length;
                var blobAssetHash = blobAssets[i].Header->Hash;
                var header = BlobAssetHeader.CreateForSerialize(Align16(blobAssetLength), blobAssetHash);
                writer.WriteBytes(&header, sizeof(BlobAssetHeader));
                writer.WriteBytes(blobAssets[i].Header + 1, blobAssetLength);
                writer.WriteBytes(&zeroBytes, header.Length - blobAssetLength);
            }
        }

        internal static void WriteBlobAssetBatch(BinaryWriter writer, NativeArray<BlobAssetPtr> blobAssets, out NativeArray<int> blobAssetOffsets)
        {
            var totalBlobAssetBatchSize = CalculateBlobAssetBatchTotalSize(blobAssets, out blobAssetOffsets);
            WriteBlobAssetBatch(writer, blobAssets, totalBlobAssetBatchSize);
        }

        private static void BuildSharedComponents(UnsafePtrList<Archetype> archetypeArray, out NativeArray<int> sharedComponentArrays, out int[] sharedComponentsToSerialize)
        {
            var sharedComponentMapping = GatherSharedComponents(archetypeArray, out var sharedComponentArraysTotalCount);
            sharedComponentArrays = new NativeArray<int>(sharedComponentArraysTotalCount, Allocator.Temp);
            FillSharedComponentArrays(sharedComponentArrays, archetypeArray, sharedComponentMapping);

            sharedComponentsToSerialize = new int[sharedComponentMapping.Count() - 1];
            using (var keyArray = sharedComponentMapping.GetKeyArray(Allocator.Temp))
            {
                foreach (var key in keyArray)
                {
                    if (key == 0)
                        continue;

                    if (sharedComponentMapping.TryGetValue(key, out var val))
                        sharedComponentsToSerialize[val - 1] = key;
                }
            }
        }

        private static unsafe void WriteTypeNames(DotsSerializationWriter dotsWriter, EntityComponentStore* entityComponentStore)
        {
            using (var stringTable = dotsWriter.CreateStringTableNode(TypesNameStringTableNodeType))
            using (var typesNameNode = dotsWriter.CreateNode<DotsSerialization.TypeNamesNode>(TypesNameNodeType))
            using (var writerHandle = typesNameNode.GetWriterHandle())
            {
                var typesHashSet = new UnsafeHashSet<int>(1024, Allocator.Temp);
                var archetypeArray = GetAllArchetypes(entityComponentStore, Allocator.Temp);
                for (int i = 0; i != archetypeArray.Length; i++)
                {
                    var archetype = archetypeArray.Ptr[i];
                    for (int iType = 0; iType < archetype->TypesCount; ++iType)
                    {
                        var typeIndex = archetype->Types[iType].TypeIndex;
                        typesHashSet.Add(typeIndex);
                    }
                }

                var writer = writerHandle.Writer;

                var typeCount = typesHashSet.Count();
                typesNameNode.NodeHeader.TypeCount = typeCount;

                foreach (var typeIndex in typesHashSet)
                {
                    ref readonly var typeInfo = ref TypeManager.GetTypeInfo(typeIndex);
                    var name = typeInfo.DebugTypeName;
                    writer.Write(typeInfo.StableTypeHash);
                    writer.Write(stringTable.WriteString(name));
                }

                typesHashSet.Dispose();
            }
        }

        private static unsafe UnsafePtrList<Archetype> WriteArchetypesNode(DotsSerializationWriter dotsWriter, EntityComponentStore* entityComponentStore)
        {
            UnsafePtrList<Archetype> archetypeArray;
            using (var archetypeNode = dotsWriter.CreateNode<DotsSerialization.RevisionedRawDataNode>(ArchetypesNodeType))
            using (var writerHandle = archetypeNode.GetWriterHandle())
            {
                archetypeNode.NodeHeader.Revision = 1;

                var writer = writerHandle.Writer;

                archetypeArray = GetAllArchetypes(entityComponentStore, Allocator.Temp);

                var typeHashToIndexMap = new UnsafeHashMap<ulong, int>(1024, Allocator.Temp);
                for (int i = 0; i != archetypeArray.Length; i++)
                {
                    var archetype = archetypeArray.Ptr[i];
                    for (int iType = 0; iType < archetype->TypesCount; ++iType)
                    {
                        var typeIndex = archetype->Types[iType].TypeIndex;
                        var typeInfo = TypeManager.GetTypeInfo(typeIndex);
                        var hash = typeInfo.StableTypeHash;
#if !UNITY_DOTSRUNTIME
                        ValidateTypeForSerialization(typeInfo);
#endif
                        typeHashToIndexMap.TryAdd(hash, i);
                    }
                }

                using (var typeHashSet = typeHashToIndexMap.GetKeyArray(Allocator.Temp))
                {
                    writer.Write(typeHashSet.Length);
                    foreach (ulong hash in typeHashSet)
                        writer.Write(hash);

                    for (int i = 0; i < typeHashSet.Length; ++i)
                        typeHashToIndexMap[typeHashSet[i]] = i;
                }

                WriteArchetypes(writer, archetypeArray, typeHashToIndexMap);
                typeHashToIndexMap.Dispose();
            }

            return archetypeArray;
        }

        static int Align16(int x)
        {
            return (x + 15) & ~15;
        }

        unsafe static void PatchManagedComponentIndices(Chunk* chunk, Archetype* archetype, ref int currentManagedIndex, ManagedComponentStore managedComponentStore)
        {
            for (int i = 0; i < archetype->NumManagedComponents; ++i)
            {
                var index = archetype->TypeMemoryOrder[i + archetype->FirstManagedComponent];
                var managedComponentIndices = (int*)ChunkDataUtility.GetComponentDataRO(chunk, 0, index);
                for (int ei = 0; ei < chunk->Count; ++ei)
                {
                    if (managedComponentIndices[ei] == 0)
                        continue;

                    var obj = managedComponentStore.GetManagedComponent(managedComponentIndices[ei]);
                    if (obj == null)
                        managedComponentIndices[ei] = 0;
                    else
                        managedComponentIndices[ei] = currentManagedIndex++;
                }
            }
        }

        static unsafe void WriteSharedAndManagedComponents(
            EntityManager entityManager,
            UnsafePtrList<Archetype> archetypeArray,
            int[] sharedComponentIndicies,
            BinaryWriter writer,
            out object[] referencedObjects,
            bool isDOTSRuntime,
            EntityRemapUtility.EntityRemapInfo* remapping,
            NativeHashMap<BlobAssetPtr, int> blobAssetMap,
            NativeArray<int> blobAssetOffsets,
            NativeHashSet<UntypedWeakReferenceId> weakAssetRefs)
        {
            int managedComponentCount = 0;
            referencedObjects = null;
            var allManagedObjectsBuffer = new UnsafeAppendBuffer(0, 16, Allocator.Temp);

            // We only support serialization in dots runtime for some unit tests but we currently can't support shared component serialization so skip it
#if !UNITY_DOTSRUNTIME
            var access = entityManager.GetCheckedEntityDataAccess();
            var mcs = access->ManagedComponentStore;
            var managedObjectClone = new ManagedObjectClone();
            var managedObjectRemap = new ManagedObjectRemap();

            var sharedComponentRecordArray = new NativeArray<SharedComponentRecord>(sharedComponentIndicies.Length, Allocator.Temp);
            if (!isDOTSRuntime)
            {
                var propertiesWriter = new ManagedObjectBinaryWriter(&allManagedObjectsBuffer);

                // Custom handling for blob asset fields. This adapter will take care of writing out the byte offset for each blob asset encountered.
                var adapter = new ManagedObjectWriterAdapter(remapping, blobAssetMap, blobAssetOffsets, weakAssetRefs);
                propertiesWriter.AddAdapter(adapter);

                // Do not allow shared components to serialize entity references
                adapter.SerializeEntityReferences = false;
                for (int i = 0; i < sharedComponentIndicies.Length; ++i)
                {
                    var index = sharedComponentIndicies[i];
                    var sharedData = mcs.GetSharedComponentDataNonDefaultBoxed(index);
                    var type = sharedData.GetType();
                    var typeIndex = TypeManager.GetTypeIndex(type);
                    ref readonly var typeInfo = ref TypeManager.GetTypeInfo(typeIndex);
                    var managedObject = Convert.ChangeType(sharedData, type);

                    propertiesWriter.WriteObject(managedObject);

                    sharedComponentRecordArray[i] = new SharedComponentRecord()
                    {
                        StableTypeHash = typeInfo.StableTypeHash,
                        ComponentSize = -1
                    };
                }

                // Ensure we allow non-shared components to have their entity references serialized
                adapter.SerializeEntityReferences = true;
                for (int a = 0; a < archetypeArray.Length; ++a)
                {
                    var archetype = archetypeArray.Ptr[a];
                    if (archetype->NumManagedComponents == 0)
                        continue;

                    for (var ci = 0; ci < archetype->Chunks.Count; ++ci)
                    {
                        var chunk = archetype->Chunks[ci];

                        for (int i = 0; i < archetype->NumManagedComponents; ++i)
                        {
                            var index = archetype->TypeMemoryOrder[i + archetype->FirstManagedComponent];
                            var managedComponentIndices = (int*)ChunkDataUtility.GetComponentDataRO(chunk, 0, index);
                            ref readonly var cType = ref TypeManager.GetTypeInfo(archetype->Types[index].TypeIndex);

                            for (int ei = 0; ei < chunk->Count; ++ei)
                            {
                                if (managedComponentIndices[ei] == 0)
                                    continue;

                                var obj = mcs.GetManagedComponent(managedComponentIndices[ei]);
                                if (obj == null)
                                    continue;

                                if (obj.GetType() != cType.Type)
                                {
                                    throw new InvalidOperationException($"Managed object type {obj.GetType()} doesn't match component type in archetype {cType.Type}");
                                }

                                managedComponentCount++;
                                allManagedObjectsBuffer.Add<ulong>(cType.StableTypeHash);
                                propertiesWriter.WriteObject(obj);
                            }
                        }
                    }
                }
                referencedObjects = propertiesWriter.GetUnityObjects();
            }
            else
            {
                for (int i = 0; i < sharedComponentIndicies.Length; ++i)
                {
                    var index = sharedComponentIndicies[i];
                    object obj = mcs.GetSharedComponentDataNonDefaultBoxed(index);

                    Type type = obj.GetType();
                    var typeIndex = TypeManager.GetTypeIndex(type);
                    ref readonly var typeInfo = ref TypeManager.GetTypeInfo(typeIndex);
                    int size = UnsafeUtility.SizeOf(type);
                    Assert.IsTrue(size >= 0);

                    sharedComponentRecordArray[i] = new SharedComponentRecord()
                    {
                        StableTypeHash = typeInfo.StableTypeHash,
                        ComponentSize = size
                    };

                    var dataPtr = (byte*)UnsafeUtility.PinGCObjectAndGetAddress(obj, out ulong handle);
                    dataPtr += TypeManager.ObjectOffset;

                    if (typeInfo.EntityOffsetCount != 0)
                    {
                        var offsets = TypeManager.GetEntityOffsets(typeInfo);
                        for (var offsetIndex = 0; offsetIndex < typeInfo.EntityOffsetCount; offsetIndex++)
                            *(Entity*) (dataPtr + offsets[offsetIndex].Offset) = EntityRemapUtility.RemapEntity(remapping, *(Entity*) (dataPtr + offsets[offsetIndex].Offset));
                    }

                    if (typeInfo.BlobAssetRefOffsetCount > 0)
                    {
                        PatchBlobAssetRefInfoBeforeSave(dataPtr, TypeManager.GetBlobAssetRefOffsets(typeInfo), typeInfo.BlobAssetRefOffsetCount, blobAssetOffsets, blobAssetMap);
                    }

                    allManagedObjectsBuffer.Add(dataPtr, size);
                    UnsafeUtility.ReleaseGCObject(handle);
                }
            }
#else
            var sharedComponentRecordArray = new NativeArray<SharedComponentRecord>(0, Allocator.Temp);
#endif

            writer.Write(sharedComponentRecordArray.Length);
            writer.WriteArray(sharedComponentRecordArray);

            writer.Write(allManagedObjectsBuffer.Length);

            writer.Write(managedComponentCount);
            writer.WriteBytes(allManagedObjectsBuffer.Ptr, allManagedObjectsBuffer.Length);

            sharedComponentRecordArray.Dispose();
            allManagedObjectsBuffer.Dispose();
        }

#if UNITY_DOTSRUNTIME
        static unsafe void ReadSharedComponents(ExclusiveEntityTransaction manager, BinaryReader reader, int expectedReadSize, NativeArray<int> sharedComponentRemap, NativeArray<SharedComponentRecord> sharedComponentRecordArray)
        {
            int tempBufferSize = 0;
            for (int i = 0; i < sharedComponentRecordArray.Length; ++i)
                tempBufferSize = math.max(sharedComponentRecordArray[i].ComponentSize, tempBufferSize);
            var buffer = stackalloc byte[tempBufferSize];

            sharedComponentRemap[0] = 0;

            var access = manager.EntityManager.GetCheckedEntityDataAccess();
            var mcs = access->ManagedComponentStore;

            int sizeRead = 0;
            for (int i = 0; i < sharedComponentRecordArray.Length; ++i)
            {
                var record = sharedComponentRecordArray[i];

                reader.ReadBytes(buffer, record.ComponentSize);
                sizeRead += record.ComponentSize;

                var typeIndex = TypeManager.GetTypeIndexFromStableTypeHash(record.StableTypeHash);
                if (typeIndex == -1)
                {
                    Console.WriteLine($"Can't find type index for type hash {record.StableTypeHash.ToString()}");
                    throw new InvalidOperationException();
                }

                var data = TypeManager.ConstructComponentFromBuffer(typeIndex, buffer);

                // TODO: this recalculation should be removed once we merge the UNITY_DOTSRUNTIME and non UNITY_DOTSRUNTIME hashcode calculations
                var hashCode = TypeManager.GetHashCode(data, typeIndex); // record.hashCode;
                int runtimeIndex = mcs.InsertSharedComponentAssumeNonDefault(typeIndex, hashCode, data);

                sharedComponentRemap[i + 1] = runtimeIndex;
            }

            Assert.AreEqual(expectedReadSize, sizeRead, "The amount of shared component data we read doesn't match the amount we serialized.");
        }

#else
        static unsafe void ReadSharedComponents(ExclusiveEntityTransaction manager, ManagedObjectBinaryReader managedDataReader, NativeArray<int> sharedComponentRemap, NativeArray<SharedComponentRecord> sharedComponentRecordArray)
        {
            // 0 index is special and means default shared component value
            // Also see below the offset + 1 indices for the same reason
            sharedComponentRemap[0] = 0;

            ManagedComponentStore mcs = manager.EntityManager.GetCheckedEntityDataAccess()->ManagedComponentStore;

            for (int i = 0; i < sharedComponentRecordArray.Length; ++i)
            {
                var record = sharedComponentRecordArray[i];
                var typeIndex = TypeManager.GetTypeIndexFromStableTypeHash(record.StableTypeHash);
                ref readonly var typeInfo = ref TypeManager.GetTypeInfo(typeIndex);
                var managedObject = managedDataReader.ReadObject(typeInfo.Type);
                var currentHash = TypeManager.GetHashCode(managedObject, typeInfo.TypeIndex);
                var sharedComponentIndex = mcs.InsertSharedComponentAssumeNonDefault(typeIndex, currentHash, managedObject);

                // When deserialization a shared component it is possible that it's hashcode changes if for example the referenced object (a UnityEngine.Object for example) becomes null.
                // This can result in the sharedComponentIndex at serialize time being different from the sharedComponentIndex at load time.
                // Thus we keep a remap table to handle this potential remap.
                // NOTE: in most cases the remap table will always be all indices matching,
                // But it doesn't look like it's worth optimizing this away at this point.
                sharedComponentRemap[i + 1] = sharedComponentIndex;
            }
        }

        // True when a component is valid to using in world serialization. A component IsSerializable when it is valid to blit
        // the data across storage media. Thus components containing pointers have an IsSerializable of false as the component
        // is blittable but no longer valid upon deserialization.
        private static bool IsTypeValidForSerialization(Type type)
        {
            if (type.GetCustomAttribute<ChunkSerializableAttribute>() != null)
                return true;

            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public))
            {
                if (field.IsStatic)
                    continue;

                if (field.FieldType.IsPointer || (field.FieldType == typeof(UIntPtr) || field.FieldType == typeof(IntPtr)))
                {
                    return false;
                }
                else if (field.FieldType.IsValueType && !field.FieldType.IsPrimitive && !field.FieldType.IsEnum)
                {
                    if (!IsTypeValidForSerialization(field.FieldType))
                        return false;
                }
            }

            return true;
        }

        private static void ValidateTypeForSerialization(in TypeManager.TypeInfo typeInfo)
        {
            // Shared Components are expected to be handled specially and are not required to be blittable
            if (typeInfo.Category == TypeManager.TypeCategory.ISharedComponentData)
            {
                if (TypeManager.HasEntityReferences(typeInfo.TypeIndex))
                {
                    throw new ArgumentException(
                        $"Shared component type '{TypeManager.GetType(typeInfo.TypeIndex)}' might contain a (potentially nested) Entity field. " +
                        $"Serializing of shared components with Entity fields is not supported");
                }
                return;
            }

            if (!IsTypeValidForSerialization(typeInfo.Type))
            {
                throw new ArgumentException($"Blittable component type '{TypeManager.GetType(typeInfo.TypeIndex)}' contains a (potentially nested) pointer field. " +
                    $"Serializing bare pointers will likely lead to runtime errors. Remove this field and consider serializing the data " +
                    $"it points to another way such as by using a BlobAssetReference or a [Serializable] ISharedComponent. If for whatever " +
                    $"reason the pointer field should in fact be serialized, add the [ChunkSerializable] attribute to your type to bypass this error.");
            }
        }

#endif

        static int ReadSharedComponentMetadata(BinaryReader reader, out NativeArray<int> sharedComponentArrays, out NativeArray<SharedComponentRecord> sharedComponentRecordArray)
        {
            int sharedComponentArraysLength = reader.ReadInt();
            sharedComponentArrays = new NativeArray<int>(sharedComponentArraysLength, Allocator.Temp);
            reader.ReadArray(sharedComponentArrays, sharedComponentArraysLength);

            var sharedComponentRecordArrayLength = reader.ReadInt();
            sharedComponentRecordArray = new NativeArray<SharedComponentRecord>(sharedComponentRecordArrayLength, Allocator.Temp);
            reader.ReadArray(sharedComponentRecordArray, sharedComponentRecordArrayLength);

            return sharedComponentRecordArrayLength;
        }

        static unsafe void GatherAllUsedBlobAssets(
            EntityManager entityManager,
            int[] sharedComponentIndices,
            bool isDOTSRuntime,
            UnsafePtrList<Archetype> archetypeArray,
            out NativeList<BlobAssetPtr> blobAssets,
            out NativeHashMap<BlobAssetPtr, int> blobAssetMap)
        {
            blobAssetMap = new NativeHashMap<BlobAssetPtr, int>(100, Allocator.TempJob);

            blobAssets = new NativeList<BlobAssetPtr>(100, Allocator.TempJob);
            for (int a = 0; a < archetypeArray.Length; ++a)
            {
                var archetype = archetypeArray.Ptr[a];
                if (!archetype->HasBlobAssetRefs)
                    continue;

                var typeCount = archetype->TypesCount;
                for (var ci = 0; ci < archetype->Chunks.Count; ++ci)
                {
                    var chunk = archetype->Chunks[ci];
                    var entityCount = chunk->Count;
                    for (var unordered_ti = 0; unordered_ti < typeCount; ++unordered_ti)
                    {
                        var ti = archetype->TypeMemoryOrder[unordered_ti];
                        var type = archetype->Types[ti];
                        if (type.IsZeroSized || type.IsManagedComponent)
                            continue;

                        ref readonly var ct = ref TypeManager.GetTypeInfo(type.TypeIndex);
                        var blobAssetRefCount = ct.BlobAssetRefOffsetCount;
                        if (blobAssetRefCount == 0)
                            continue;

                        var blobAssetRefOffsets = TypeManager.GetBlobAssetRefOffsets(ct);
                        var chunkBuffer = chunk->Buffer;

                        if (blobAssetRefCount > 0)
                        {
                            int subArrayOffset = archetype->Offsets[ti];
                            byte* componentArrayStart = OffsetFromPointer(chunkBuffer, subArrayOffset);

                            if (type.IsBuffer)
                            {
                                BufferHeader* header = (BufferHeader*)componentArrayStart;
                                int strideSize = archetype->SizeOfs[ti];
                                int elementSize = ct.ElementSize;

                                for (int bi = 0; bi < entityCount; ++bi)
                                {
                                    var bufferStart = BufferHeader.GetElementPointer(header);
                                    var bufferEnd = bufferStart + header->Length * elementSize;
                                    for (var componentData = bufferStart; componentData < bufferEnd; componentData += elementSize)
                                    {
                                        AddBlobAssetRefInfo(componentData, blobAssetRefOffsets, blobAssetRefCount, ref blobAssetMap, ref blobAssets);
                                    }

                                    header = (BufferHeader*)OffsetFromPointer(header, strideSize);
                                }
                            }
                            else
                            {
                                int componentSize = archetype->SizeOfs[ti];
                                byte* end = componentArrayStart + componentSize * entityCount;
                                for (var componentData = componentArrayStart; componentData < end; componentData += componentSize)
                                {
                                    AddBlobAssetRefInfo(componentData, blobAssetRefOffsets, blobAssetRefCount, ref blobAssetMap, ref blobAssets);
                                }
                            }
                        }
                    }
                }
            }

#if !UNITY_DOTSRUNTIME
            var access = entityManager.GetCheckedEntityDataAccess();
            var mcs = access->ManagedComponentStore;
            var managedObjectBlobs = new ManagedObjectBlobs();

            if (!isDOTSRuntime)
            {
                for (var i = 0; i < sharedComponentIndices.Length; i++)
                {
                    var sharedComponentIndex = sharedComponentIndices[i];
                    if (!mcs.HasBlobReferences(sharedComponentIndex))
                        continue;
                    var sharedComponentValue = mcs.GetSharedComponentDataNonDefaultBoxed(sharedComponentIndex);
                    managedObjectBlobs.GatherBlobAssetReferences(sharedComponentValue, blobAssets, blobAssetMap);
                }

                for (var archetypeIndex = 0; archetypeIndex < archetypeArray.Length; archetypeIndex++)
                {
                    var archetype = archetypeArray.Ptr[archetypeIndex];

                    if (archetype->NumManagedComponents == 0)
                        continue;

                    for (var chunkIndex = 0; chunkIndex < archetype->Chunks.Count; chunkIndex++)
                    {
                        var chunk = archetype->Chunks[chunkIndex];

                        for (var unorderedTypeIndexInArchetype = 0; unorderedTypeIndexInArchetype < archetype->NumManagedComponents; ++unorderedTypeIndexInArchetype)
                        {
                            var typeIndexInArchetype = archetype->TypeMemoryOrder[archetype->FirstManagedComponent + unorderedTypeIndexInArchetype];
                            var managedComponentIndices = (int*)ChunkDataUtility.GetComponentDataRO(chunk, 0, typeIndexInArchetype);
                            ref readonly var typeInfo = ref TypeManager.GetTypeInfo(archetype->Types[typeIndexInArchetype].TypeIndex);

                            for (var entityIndex = 0; entityIndex < chunk->Count; entityIndex++)
                            {
                                if (managedComponentIndices[entityIndex] == 0)
                                    continue;

                                var managedComponentValue = mcs.GetManagedComponent(managedComponentIndices[entityIndex]);

                                if (managedComponentValue == null)
                                    continue;

                                if (managedComponentValue.GetType() != typeInfo.Type)
                                {
                                    throw new InvalidOperationException($"Managed object type {managedComponentValue.GetType()} doesn't match component type in archetype {typeInfo.Type}");
                                }

                                if (typeInfo.HasBlobAssetRefs)
                                    managedObjectBlobs.GatherBlobAssetReferences(managedComponentValue, blobAssets, blobAssetMap);
                            }
                        }
                    }
                }
            }
            else
            {
                for (var i = 0; i < sharedComponentIndices.Length; i++)
                {
                    var sharedComponentIndex = sharedComponentIndices[i];
                    if (mcs.HasBlobReferences(sharedComponentIndex))
                    {
                        var sharedComponentValue = mcs.GetSharedComponentDataNonDefaultBoxed(sharedComponentIndex);
                        managedObjectBlobs.GatherBlobAssetReferences(sharedComponentValue, blobAssets, blobAssetMap);
                    }
                }
            }
#endif
        }

        private static unsafe void AddBlobAssetRefInfo(byte* componentData, TypeManager.EntityOffsetInfo* blobAssetRefOffsets, int blobAssetRefCount,
            ref NativeHashMap<BlobAssetPtr, int> blobAssetMap, ref NativeList<BlobAssetPtr> blobAssets)
        {
            for (int i = 0; i < blobAssetRefCount; ++i)
            {
                var blobAssetRefOffset = blobAssetRefOffsets[i].Offset;
                var blobAssetRefPtr = (BlobAssetReferenceData*)(componentData + blobAssetRefOffset);
                if (blobAssetRefPtr->m_Ptr == null)
                    continue;

                var blobAssetPtr = new BlobAssetPtr(blobAssetRefPtr->Header);
                if (!blobAssetMap.TryGetValue(blobAssetPtr, out var blobAssetIndex))
                {
                    blobAssetIndex = blobAssets.Length;
                    blobAssets.Add(blobAssetPtr);
                    blobAssetMap.TryAdd(blobAssetPtr, blobAssetIndex);
                }
            }
        }

        private static unsafe void GetWeakAssetRefsInChunk(Chunk* chunk, NativeHashSet<UntypedWeakReferenceId> weakAssetRefs)
        {
            var archetype = chunk->Archetype;
            var typeCount = archetype->TypesCount;
            var entityCount = chunk->Count;
            for (var unordered_ti = 0; unordered_ti < typeCount; ++unordered_ti)
            {
                var ti = archetype->TypeMemoryOrder[unordered_ti];
                var type = archetype->Types[ti];
                if (type.IsZeroSized || type.IsManagedComponent)
                    continue;

                ref readonly var ct = ref TypeManager.GetTypeInfo(type.TypeIndex);
                var weakAssetRefCount = ct.WeakAssetRefOffsetCount;

                if (weakAssetRefCount == 0)
                    continue;

                var weakAssetRefOffsets = TypeManager.GetWeakAssetRefOffsets(ct);
                var chunkBuffer = chunk->Buffer;
                int subArrayOffset = archetype->Offsets[ti];
                byte* componentArrayStart = OffsetFromPointer(chunkBuffer, subArrayOffset);

                if (type.IsBuffer)
                {
                    BufferHeader* header = (BufferHeader*)componentArrayStart;
                    int strideSize = archetype->SizeOfs[ti];
                    var elementSize = ct.ElementSize;

                    for (int bi = 0; bi < entityCount; ++bi)
                    {
                        var bufferStart = BufferHeader.GetElementPointer(header);
                        var bufferEnd = bufferStart + header->Length * elementSize;
                        for (var componentData = bufferStart; componentData < bufferEnd; componentData += elementSize)
                        {
                            GetWeakAssetRefsInComponent(componentData, weakAssetRefOffsets, weakAssetRefCount, weakAssetRefs);
                        }

                        header = (BufferHeader*)OffsetFromPointer(header, strideSize);
                    }
                }
                else
                {
                    int size = archetype->SizeOfs[ti];
                    byte* end = componentArrayStart + size * entityCount;
                    for (var componentData = componentArrayStart; componentData < end; componentData += size)
                    {
                        GetWeakAssetRefsInComponent(componentData, weakAssetRefOffsets, weakAssetRefCount, weakAssetRefs);
                    }
                }
            }
        }

        private static unsafe void GetWeakAssetRefsInComponent(byte* componentData, TypeManager.EntityOffsetInfo* weakAssetRefOffsets, int weakAssetRefCount,
            NativeHashSet<UntypedWeakReferenceId> weakAssetRefs)
        {
            for (int i = 0; i < weakAssetRefCount; ++i)
            {
                var weakAssetRef = *(UntypedWeakReferenceId*)(componentData + weakAssetRefOffsets[i].Offset);
                weakAssetRefs.Add(weakAssetRef);
            }
        }

        private static unsafe void PatchBlobAssetsInChunkBeforeSave(Chunk* tempChunk, Chunk* originalChunk,
            NativeArray<int> blobAssetOffsets, NativeHashMap<BlobAssetPtr, int> blobAssetMap)
        {
            var archetype = originalChunk->Archetype;
            var typeCount = archetype->TypesCount;
            var entityCount = originalChunk->Count;
            for (var unordered_ti = 0; unordered_ti < typeCount; ++unordered_ti)
            {
                var ti = archetype->TypeMemoryOrder[unordered_ti];
                var type = archetype->Types[ti];
                if (type.IsZeroSized || type.IsManagedComponent)
                    continue;

                ref readonly var ct = ref TypeManager.GetTypeInfo(type.TypeIndex);
                var blobAssetRefCount = ct.BlobAssetRefOffsetCount;
                if (blobAssetRefCount == 0)
                    continue;

                var blobAssetRefOffsets = TypeManager.GetBlobAssetRefOffsets(ct);
                var chunkBuffer = tempChunk->Buffer;
                int subArrayOffset = archetype->Offsets[ti];
                byte* componentArrayStart = OffsetFromPointer(chunkBuffer, subArrayOffset);

                if (type.IsBuffer)
                {
                    BufferHeader* header = (BufferHeader*)componentArrayStart;
                    int strideSize = archetype->SizeOfs[ti];
                    var elementSize = ct.ElementSize;

                    for (int bi = 0; bi < entityCount; ++bi)
                    {
                        var bufferStart = BufferHeader.GetElementPointer(header);
                        var bufferEnd = bufferStart + header->Length * elementSize;
                        for (var componentData = bufferStart; componentData < bufferEnd; componentData += elementSize)
                        {
                            PatchBlobAssetRefInfoBeforeSave(componentData, blobAssetRefOffsets, blobAssetRefCount, blobAssetOffsets, blobAssetMap);
                        }

                        header = (BufferHeader*)OffsetFromPointer(header, strideSize);
                    }
                }
                else if (blobAssetRefCount > 0)
                {
                    int size = archetype->SizeOfs[ti];
                    byte* end = componentArrayStart + size * entityCount;
                    for (var componentData = componentArrayStart; componentData < end; componentData += size)
                    {
                        PatchBlobAssetRefInfoBeforeSave(componentData, blobAssetRefOffsets, blobAssetRefCount, blobAssetOffsets, blobAssetMap);
                    }
                }
            }
        }

        private static unsafe void PatchBlobAssetRefInfoBeforeSave(byte* componentData, TypeManager.EntityOffsetInfo* blobAssetRefOffsets, int blobAssetRefCount,
            NativeArray<int> blobAssetOffsets, NativeHashMap<BlobAssetPtr, int> blobAssetMap)
        {
            for (int i = 0; i < blobAssetRefCount; ++i)
            {
                var blobAssetRefOffset = blobAssetRefOffsets[i].Offset;
                var blobAssetRefPtr = (BlobAssetReferenceData*)(componentData + blobAssetRefOffset);
                int value = -1;
                if (blobAssetRefPtr->m_Ptr != null)
                {
                    value = blobAssetMap[new BlobAssetPtr(blobAssetRefPtr->Header)];
                    value = blobAssetOffsets[value];
                }
                blobAssetRefPtr->m_Ptr = (byte*)value;
            }
        }

        private static unsafe void FillBufferPatchRecordsAndClearBufferPointer(Chunk* chunk, NativeList<BufferPatchRecord> bufferPatches, NativeList<IntPtr> bufferPtrs)
        {
            byte* tempChunkBuffer = chunk->Buffer;
            int entityCount = chunk->Count;
            Archetype* archetype = chunk->Archetype;

            // Find all buffer pointer locations and work out how much memory the deserializer must allocate on load.
            for (int ti = 0; ti < archetype->TypesCount; ++ti)
            {
                int index = archetype->TypeMemoryOrder[ti];
                var type = archetype->Types[index];

                if (type.IsBuffer)
                {
                    Assert.IsFalse(type.IsZeroSized);

                    ref readonly var ct = ref TypeManager.GetTypeInfo(type.TypeIndex);
                    var elementSize = ct.ElementSize;

                    for (int bi = 0; bi < entityCount; ++bi)
                    {
                        var header = (BufferHeader*)ChunkDataUtility.GetComponentDataRO(chunk, bi, index);

                        if (header->Pointer != null)
                        {
                            int capacityInBytes = elementSize * header->Capacity;
                            bufferPatches.Add(new BufferPatchRecord
                            {
                                ChunkOffset = (int)(((byte*)header) - tempChunkBuffer),
                                AllocSizeBytes = capacityInBytes
                            });
                            bufferPtrs.Add((IntPtr)header->Pointer);
                            header->Pointer = null;
                        }
                    }
                }
            }
        }

        static unsafe void FillSharedComponentIndexRemap(int* remapArray, Archetype* archetype)
        {
            int i = 0;
            for (int iType = 1; iType < archetype->TypesCount; ++iType)
            {
                int orderedIndex = archetype->TypeMemoryOrder[iType] - archetype->FirstSharedComponent;
                if (0 <= orderedIndex && orderedIndex < archetype->NumSharedComponents)
                    remapArray[i++] = orderedIndex;
            }
        }

        private static unsafe void FillSharedComponentArrays(NativeArray<int> sharedComponentArrays, UnsafePtrList<Archetype> archetypeArray, NativeHashMap<int, int> sharedComponentMapping)
        {
            var sharedComponentIndexRemap = stackalloc int[EntityComponentStore.kMaxSharedComponentCount];
            int index = 0;
            for (int iArchetype = 0; iArchetype < archetypeArray.Length; ++iArchetype)
            {
                var archetype = archetypeArray.Ptr[iArchetype];
                int numSharedComponents = archetype->NumSharedComponents;
                if (numSharedComponents == 0)
                    continue;

                FillSharedComponentIndexRemap(sharedComponentIndexRemap, archetype);
                for (int iChunk = 0; iChunk < archetype->Chunks.Count; ++iChunk)
                {
                    var sharedComponents = archetype->Chunks[iChunk]->SharedComponentValues;
                    for (int iType = 0; iType < numSharedComponents; iType++)
                    {
                        int remappedIndex = sharedComponentIndexRemap[iType];
                        sharedComponentArrays[index++] = sharedComponentMapping[sharedComponents[remappedIndex]];
                    }
                }
            }
            Assert.AreEqual(sharedComponentArrays.Length, index);
        }

        private static unsafe NativeHashMap<int, int> GatherSharedComponents(UnsafePtrList<Archetype> archetypeArray, out int sharedComponentArraysTotalCount)
        {
            sharedComponentArraysTotalCount = 0;
            var sharedIndexToSerialize = new NativeHashMap<int, int>(1024, Allocator.Temp);
            sharedIndexToSerialize.TryAdd(0, 0); // All default values map to 0
            int nextIndex = 1;
            for (int iArchetype = 0; iArchetype < archetypeArray.Length; ++iArchetype)
            {
                var archetype = archetypeArray.Ptr[iArchetype];
                sharedComponentArraysTotalCount += archetype->Chunks.Count * archetype->NumSharedComponents;

                int numSharedComponents = archetype->NumSharedComponents;
                for (int iType = 0; iType < numSharedComponents; iType++)
                {
                    var sharedComponents = archetype->Chunks.GetSharedComponentValueArrayForType(iType);
                    for (int iChunk = 0; iChunk < archetype->Chunks.Count; ++iChunk)
                    {
                        int sharedComponentIndex = sharedComponents[iChunk];
                        if (!sharedIndexToSerialize.TryGetValue(sharedComponentIndex, out var val))
                        {
                            sharedIndexToSerialize.TryAdd(sharedComponentIndex, nextIndex++);
                        }
                    }
                }
            }

            return sharedIndexToSerialize;
        }

        private static unsafe void ClearChunkHeaderComponents(Chunk* chunk)
        {
            int chunkHeaderTypeIndex = TypeManager.GetTypeIndex<ChunkHeader>();
            var archetype = chunk->Archetype;
            var typeIndexInArchetype = ChunkDataUtility.GetIndexInTypeArray(chunk->Archetype, chunkHeaderTypeIndex);
            if (typeIndexInArchetype == -1)
                return;

            var buffer = chunk->Buffer;
            var length = chunk->Count;
            var startOffset = archetype->Offsets[typeIndexInArchetype];
            var chunkHeaders = (ChunkHeader*)(buffer + startOffset);
            for (int i = 0; i < length; ++i)
            {
                chunkHeaders[i] = ChunkHeader.Null;
            }
        }

        static unsafe byte* OffsetFromPointer(void* ptr, int offset)
        {
            return ((byte*)ptr) + offset;
        }

        static unsafe void WriteArchetypes(BinaryWriter writer, UnsafePtrList<Archetype> archetypeArray, UnsafeHashMap<ulong, int> typeHashToIndexMap)
        {
            writer.Write(archetypeArray.Length);

            for (int a = 0; a < archetypeArray.Length; ++a)
            {
                var archetype = archetypeArray.Ptr[a];

                writer.Write(archetype->EntityCount);
                writer.Write(archetype->TypesCount - 1);
                for (int i = 1; i < archetype->TypesCount; ++i)
                {
                    var componentType = archetype->Types[i];
                    int flag = componentType.IsChunkComponent ? TypeManager.ChunkComponentTypeFlag : 0;
                    var hash = TypeManager.GetTypeInfo(componentType.TypeIndex).StableTypeHash;
                    writer.Write(typeHashToIndexMap[hash] | flag);
                }
            }
        }

        static unsafe int GenerateRemapInfo(EntityManager entityManager, UnsafePtrList<Archetype> archetypeArray, NativeArray<EntityRemapUtility.EntityRemapInfo> entityRemapInfos)
        {
            int nextEntityId = 1; //0 is reserved for Entity.Null;

            int totalChunkCount = 0;
            for (int archetypeIndex = 0; archetypeIndex < archetypeArray.Length; ++archetypeIndex)
            {
                var archetype = archetypeArray.Ptr[archetypeIndex];
                for (int i = 0; i < archetype->Chunks.Count; ++i)
                {
                    var chunk = archetype->Chunks[i];
                    for (int iEntity = 0; iEntity < chunk->Count; ++iEntity)
                    {
                        var entity = *(Entity*)ChunkDataUtility.GetComponentDataRO(chunk, iEntity, 0);
                        EntityRemapUtility.AddEntityRemapping(ref entityRemapInfos, entity, new Entity { Version = 0, Index = nextEntityId });
                        ++nextEntityId;
                    }

                    totalChunkCount += 1;
                }
            }

            return totalChunkCount;
        }
    }
}
