using System.Diagnostics;
using Unity.Assertions;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;

namespace Unity.Entities
{
    // Stores change version numbers, shared component indices, and entity count for all chunks belonging to an archetype in SOA layout
    [DebuggerTypeProxy(typeof(ArchetypeChunkDataDebugView))]
    internal unsafe struct ArchetypeChunkData
    {
        private Chunk** p;
        public int Capacity { get; private set; } // maximum number of chunks that can be tracked before Grow() must be called
        public int Count { get; private set; } // number of chunks currently tracked [0..Capacity]
        private int ChunkEntityCapacity; // capacity of each chunk, in entities

        readonly int SharedComponentCount;
        readonly int ComponentCount; // including shared components

        // ChangeVersions and SharedComponentValues stored like:
        //    type0: chunk0 chunk1 chunk2 ...
        //    type1: chunk0 chunk1 chunk2 ...
        //    type2: chunk0 chunk1 chunk2 ...
        //    ...

        ulong ChunkPtrSize => (ulong)(sizeof(Chunk*) * Capacity);
        ulong ChangeVersionSize  => (ulong)(sizeof(uint) * ComponentCount * Capacity);
        ulong EntityCountSize => (ulong)(sizeof(int) * Capacity);
        ulong SharedComponentValuesSize => (ulong)(sizeof(int) * SharedComponentCount * Capacity);
        ulong ComponentEnabledBitsSizePerComponentInChunk => (ulong)(((ChunkEntityCapacity + 63) / 64) * sizeof(ulong)); // size of bits for ONE component in a chunk
        ulong ComponentEnabledBitsSizeTotalPerChunk => ComponentEnabledBitsSizePerComponentInChunk * (ulong)ComponentCount; // size of bits for ALL components in a chunk
        ulong ComponentEnabledBitsSize => ComponentEnabledBitsSizeTotalPerChunk * (ulong)Capacity; // size of bits for ALL components of ALL chunks
        ulong ComponentEnabledBitsHierarchicalDataSizePerChunk => (ulong)(sizeof(int) * ComponentCount); // size of enabled bits hierarchical data for ONE chunk
        ulong ComponentEnabledBitsHierarchicalDataSize => (ComponentEnabledBitsHierarchicalDataSizePerChunk * (ulong) Capacity); // size of enabled bits hierarchical data for ALL chunks
        ulong BufferSize => ChunkPtrSize + ChangeVersionSize + EntityCountSize + SharedComponentValuesSize + ComponentEnabledBitsSize + ComponentEnabledBitsHierarchicalDataSize;

        // ChangeVersions[ComponentCount * Capacity]
        //   - Order version is ChangeVersion[0] which is ChangeVersion[Entity]
        uint* ChangeVersions => (uint*)(((ulong)p) + ChunkPtrSize);

        // EntityCount[Capacity]
        int* EntityCount => (int*)(((ulong)ChangeVersions) + ChangeVersionSize);

        // SharedComponentValues[SharedComponentCount * Capacity]
        int* SharedComponentValues => (int*)(((ulong)EntityCount) + EntityCountSize);

        // ComponentEnabledBits[(RoundUp(ChunkEntityCapacity, 64)/64 * ComponentCount * Capacity]
        //    chunk0: type0 type1 type2 ...
        //    chunk1: type0 type1 type2 ...
        //    chunk1: type0 type1 type2 ...
        // Each type's bits are rounded up to a multiple of 64 bits, so that we can create an UnsafeBitArray of a single
        // type within a single chunk.
        byte* ComponentEnabledBits => (byte*)(((ulong)SharedComponentValues) + SharedComponentValuesSize);

        // Starts with single int32 for # of disabled components in entire archetype
        // ComponentEnabledBitsHierarchicalData[ComponentCount * Capacity]
        //    chunk0: type0 type1 type2 ...
        //    chunk1: type0 type1 type2 ...
        //    chunk1: type0 type1 type2 ...
        int* ComponentEnabledBitsHierarchicalData => (int*)(((ulong)ComponentEnabledBits) + ComponentEnabledBitsSize);

        public ArchetypeChunkData(int componentCount, int sharedComponentCount, int chunkEntityCapacity)
        {
            p = null;
            Capacity = 0;
            Count = 0;
            ChunkEntityCapacity = chunkEntityCapacity;
            SharedComponentCount = sharedComponentCount;
            ComponentCount = componentCount;
        }

        public Chunk* this[int index]
        {
            get { return p[index]; }
        }


        public void Grow(int nextCapacity)
        {
            Assert.IsTrue(nextCapacity > Capacity);

            ulong nextChunkPtrSize = (ulong)(sizeof(Chunk*) * nextCapacity);
            ulong nextChangeVersionSize  = (ulong)(sizeof(uint) * ComponentCount * nextCapacity);
            ulong nextEntityCountSize = (ulong)(sizeof(int) * nextCapacity);
            ulong nextSharedComponentValuesSize = (ulong)(sizeof(int) * SharedComponentCount * nextCapacity);
            ulong nextComponentEnabledBitsSize = ComponentEnabledBitsSizeTotalPerChunk * (ulong)nextCapacity;
            ulong nextComponentEnabledBitsHierarchicalDataSize = ComponentEnabledBitsHierarchicalDataSizePerChunk * (ulong) nextCapacity;
            ulong nextBufferSize = nextChunkPtrSize + nextChangeVersionSize + nextEntityCountSize + nextSharedComponentValuesSize + nextComponentEnabledBitsSize + nextComponentEnabledBitsHierarchicalDataSize;
            ulong nextBufferPtr = (ulong)Memory.Unmanaged.Allocate((long)nextBufferSize, 16, Allocator.Persistent);

            Chunk** nextChunkData = (Chunk**)nextBufferPtr;
            nextBufferPtr += nextChunkPtrSize;
            uint* nextChangeVersions = (uint*)nextBufferPtr;
            nextBufferPtr += nextChangeVersionSize;
            int* nextEntityCount = (int*)nextBufferPtr;
            nextBufferPtr += nextEntityCountSize;
            int* nextSharedComponentValues = (int*)nextBufferPtr;
            nextBufferPtr += nextSharedComponentValuesSize;
            byte* nextComponentEnabledBitsValues = (byte*)nextBufferPtr;
            nextBufferPtr += nextComponentEnabledBitsSize;
            int* nextComponentEnabledBitsHierarchicalDataValues = (int*)nextBufferPtr;
            nextBufferPtr += nextComponentEnabledBitsHierarchicalDataSize;

            int prevCount = Count;
            int prevCapacity = Capacity;
            Chunk** prevChunkData = p;
            uint* prevChangeVersions = ChangeVersions;
            int* prevEntityCount = EntityCount;
            int* prevSharedComponentValues = SharedComponentValues;
            byte* prevComponentEnabledBitsValues = ComponentEnabledBits;
            int* prevComponentEnabledBitsHierarchicalDataValues = ComponentEnabledBitsHierarchicalData;

            UnsafeUtility.MemCpy(nextChunkData, prevChunkData, (sizeof(Chunk*) * prevCount));

            for (int i = 0; i < ComponentCount; i++)
                UnsafeUtility.MemCpy(nextChangeVersions + (i * nextCapacity), prevChangeVersions + (i * prevCapacity), sizeof(uint) * Count);

            for (int i = 0; i < SharedComponentCount; i++)
                UnsafeUtility.MemCpy(nextSharedComponentValues + (i * nextCapacity), prevSharedComponentValues + (i * prevCapacity), sizeof(uint) * Count);

            UnsafeUtility.MemCpy(nextEntityCount, prevEntityCount, sizeof(int) * Count);

            UnsafeUtility.MemCpy(nextComponentEnabledBitsValues, prevComponentEnabledBitsValues, (long)ComponentEnabledBitsSize);

            UnsafeUtility.MemCpy(nextComponentEnabledBitsHierarchicalDataValues, prevComponentEnabledBitsHierarchicalDataValues, (long)ComponentEnabledBitsHierarchicalDataSize);

            Memory.Unmanaged.Free(p, Allocator.Persistent);

            p = nextChunkData;
            Capacity = nextCapacity;
        }

        public bool InsideAllocation(ulong addr)
        {
            ulong startAddr = (ulong)p;
            return (addr >= startAddr) && (addr <= (startAddr + BufferSize));
        }

        public int* GetSharedComponentValueArrayForType(int sharedComponentIndexInArchetype)
        {
            return SharedComponentValues + (sharedComponentIndexInArchetype * Capacity);
        }

        public int GetSharedComponentValue(int sharedComponentIndexInArchetype, int chunkIndex)
        {
            var sharedValues = GetSharedComponentValueArrayForType(sharedComponentIndexInArchetype);
            return sharedValues[chunkIndex];
        }

        public void SetSharedComponentValue(int sharedComponentIndexInArchetype, int chunkIndex, int value)
        {
            var sharedValues = GetSharedComponentValueArrayForType(sharedComponentIndexInArchetype);
            sharedValues[chunkIndex] = value;
        }

        public SharedComponentValues GetSharedComponentValues(int chunkIndex)
        {
            return new SharedComponentValues
            {
                firstIndex = SharedComponentValues + chunkIndex,
                stride = Capacity * sizeof(int)
            };
        }

        // Returns the bits for all types within a single chunk. Each type's bits are padded to a
        // multiple of 64 entities.
        public UnsafeBitArray GetComponentEnabledArrayForChunk(int chunkIndex)
        {
            Assert.IsTrue(chunkIndex >= 0 && chunkIndex < Capacity);

            var bits = ComponentEnabledBits
                       + (ComponentEnabledBitsSizeTotalPerChunk * (ulong) chunkIndex);
            return new UnsafeBitArray(bits, (int)ComponentEnabledBitsSizeTotalPerChunk);
        }

        // Returns the bits for a single type in a single chunk. Array count will be padded to a multiple of 64 entities.
        public UnsafeBitArray GetComponentEnabledArrayForTypeInChunk(int typeIndexInArchetype, int chunkIndex)
        {
            Assert.IsTrue(typeIndexInArchetype >= 0);

            var bits = ComponentEnabledBits
                       + (ComponentEnabledBitsSizeTotalPerChunk * (ulong)chunkIndex)
                       + (ComponentEnabledBitsSizePerComponentInChunk * (ulong) typeIndexInArchetype);
            return new UnsafeBitArray(bits, (int)ComponentEnabledBitsSizePerComponentInChunk);
        }

        public UnsafeBitArray GetComponentEnabledArrayForArchetype()
        {
            var bits = ComponentEnabledBits;
            return new UnsafeBitArray(bits, (int)ComponentEnabledBitsSizeTotalPerChunk * Count);
        }

        public UnsafeBitArray GetComponentEnabledArrayForArchetypeAndChunkOffset(int chunkOffset)
        {
            var bits = ComponentEnabledBits + ComponentEnabledBitsSizeTotalPerChunk * (ulong)chunkOffset;
            return new UnsafeBitArray(bits, (int)ComponentEnabledBitsSizeTotalPerChunk * (Count - chunkOffset));
        }

        public void InitializeDisabledCountForChunk(int chunkIndex)
        {
            var typeArrayForChunk = ComponentEnabledBitsHierarchicalData + chunkIndex * ComponentCount;
            UnsafeUtility.MemClear(typeArrayForChunk, (long)ComponentEnabledBitsHierarchicalDataSizePerChunk);
        }

        public void SetEnabledBitsAndHierarchicalData(int chunkIndex, byte* componentEnabledBitValues, int* perComponentDisabledBitCount)
        {
            var bits = ComponentEnabledBits + ComponentEnabledBitsSizeTotalPerChunk * (ulong)chunkIndex;
            UnsafeUtility.MemCpy(bits, componentEnabledBitValues, (long)ComponentEnabledBitsSizeTotalPerChunk);

            var typeArrayForChunk = ComponentEnabledBitsHierarchicalData + chunkIndex * ComponentCount;
            UnsafeUtility.MemCpy(typeArrayForChunk, perComponentDisabledBitCount, (long)ComponentEnabledBitsHierarchicalDataSizePerChunk);
        }

        public int GetChunkDisabledCountForType(int typeIndexInArchetype, int chunkIndex)
        {
            Assert.IsTrue(chunkIndex >= 0 && chunkIndex < Capacity);
            Assert.IsTrue(typeIndexInArchetype >= 0);

            var typeArrayForChunk = ComponentEnabledBitsHierarchicalData + chunkIndex * ComponentCount;
            return typeArrayForChunk[typeIndexInArchetype];
        }

        public byte* GetPointerToComponentEnabledArrayForArchetype()
        {
            return ComponentEnabledBits;
        }

        public int* GetPointerToChunkDisabledCountForArchetype()
        {
            return ComponentEnabledBitsHierarchicalData;
        }

        public int* GetPointerToChunkDisabledCountForType(int typeIndexInArchetype, int chunkIndex)
        {
            Assert.IsTrue(chunkIndex >= 0 && chunkIndex < Capacity);
            Assert.IsTrue(typeIndexInArchetype >= 0);

            var typeArrayForChunk = ComponentEnabledBitsHierarchicalData + chunkIndex * ComponentCount;
            return typeArrayForChunk + typeIndexInArchetype;
        }

        public void AdjustChunkDisabledCountForType(int typeIndexInArchetype, int chunkIndex, int value)
        {
            Assert.IsTrue(chunkIndex >= 0 && chunkIndex < Capacity);
            Assert.IsTrue(typeIndexInArchetype >= 0);

            var typeArrayForChunk = ComponentEnabledBitsHierarchicalData + chunkIndex * ComponentCount;
            typeArrayForChunk[typeIndexInArchetype] += value;
        }

        public uint* GetChangeVersionArrayForType(int indexInArchetype)
        {
            return ChangeVersions + (indexInArchetype * Capacity);
        }

        public uint GetChangeVersion(int indexInArchetype, int chunkIndex)
        {
            var changeVersions = GetChangeVersionArrayForType(indexInArchetype);
            return changeVersions[chunkIndex];
        }

        public uint GetOrderVersion(int chunkIndex)
        {
            return GetChangeVersion(0, chunkIndex);
        }

        public void SetChangeVersion(int indexInArchetype, int chunkIndex, uint version)
        {
            var changeVersions = GetChangeVersionArrayForType(indexInArchetype);
            changeVersions[chunkIndex] = version;
        }

        public void SetAllChangeVersion(int chunkIndex, uint version)
        {
            for (int i = 1; i < ComponentCount; ++i)
                ChangeVersions[(i * Capacity) + chunkIndex] = version;
        }

        public void SetOrderVersion(int chunkIndex, uint changeVersion)
        {
            SetChangeVersion(0, chunkIndex, changeVersion);
        }

        public int* GetChunkEntityCountArray()
        {
            return EntityCount;
        }

        public int GetChunkEntityCount(int chunkIndex)
        {
            return EntityCount[chunkIndex];
        }

        public void SetChunkEntityCount(int chunkIndex, int count)
        {
            EntityCount[chunkIndex] = count;
        }

        public void Add(Chunk* chunk, SharedComponentValues sharedComponentIndices, uint changeVersion)
        {
            var chunkIndex = Count++;

            p[chunkIndex] = chunk;

            for (int i = 0; i < SharedComponentCount; i++)
                SharedComponentValues[(i * Capacity) + chunkIndex] = sharedComponentIndices[i];

            // New chunk, so all versions are reset.
            for (int i = 0; i < ComponentCount; i++)
                ChangeVersions[(i * Capacity) + chunkIndex] = changeVersion;

            EntityCount[chunkIndex] = chunk->Count;
        }

        public void MoveChunks(in ArchetypeChunkData srcChunks)
        {
            if (Capacity < Count + srcChunks.Count)
                Grow(Count + srcChunks.Count);

            UnsafeUtility.MemCpy(p + Count, srcChunks.p, sizeof(Chunk*) * srcChunks.Count);

            Count += srcChunks.Count;
        }

        public void AddToCachedChunkList(ref UnsafeCachedChunkList chunkList, int matchingArchetypeIndex, int startIndex = 0)
        {
            chunkList.Append(p + startIndex, Count - startIndex, matchingArchetypeIndex);
        }

        public void RemoveAtSwapBack(int chunkIndex)
        {
            Count--;

            if (chunkIndex == Count)
                return;

            p[chunkIndex] = p[Count];

            for (int i = 0; i < SharedComponentCount; i++)
                SharedComponentValues[(i * Capacity) + chunkIndex] = SharedComponentValues[(i * Capacity) + Count];

            // On *chunk order* change, no versions changed, just moved to new location.
            for (int i = 0; i < ComponentCount; i++)
                ChangeVersions[(i * Capacity) + chunkIndex] = ChangeVersions[(i * Capacity) + Count];

            EntityCount[chunkIndex] = EntityCount[Count];
        }

        public void Dispose()
        {
            Memory.Unmanaged.Free(p, Allocator.Persistent);
            p = null;
            Capacity = 0;
            Count = 0;
        }
    }
}
