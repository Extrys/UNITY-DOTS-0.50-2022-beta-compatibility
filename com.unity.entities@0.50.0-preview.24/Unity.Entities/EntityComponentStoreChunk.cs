using System;
using System.Threading;
using Unity.Assertions;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace Unity.Entities
{
    internal unsafe partial struct EntityComponentStore
    {
        // ----------------------------------------------------------------------------------------------------------
        // PUBLIC
        // ----------------------------------------------------------------------------------------------------------

        // ----------------------------------------------------------------------------------------------------------
        // INTERNAL
        // ----------------------------------------------------------------------------------------------------------

        internal bool IsComponentEnabled(Entity entity, int typeIndex)
        {
            var chunk = m_EntityInChunkByEntity[entity.Index].Chunk;
            var archetype = chunk->Archetype;
            var indexInChunk = m_EntityInChunkByEntity[entity.Index].IndexInChunk;
            var typeOffset = ChunkDataUtility.GetIndexInTypeArray(archetype, typeIndex);

            return IsComponentEnabled(chunk, indexInChunk, typeOffset);
        }

        internal bool IsComponentEnabled(Chunk* chunk, int indexInChunk, int typeIndexInArchetype)
        {
            // the bit array size is padded up to 64 bits, so we validate we're not indexing outside the valid data.
            Assert.IsTrue(indexInChunk < chunk->Capacity);

            var isComponentEnabled = ChunkDataUtility.GetComponentEnabledRO(chunk, typeIndexInArchetype);
            return isComponentEnabled.IsSet(indexInChunk);
        }

        internal void SetComponentEnabled(Entity entity, int typeIndex, bool value)
        {
            var chunk = m_EntityInChunkByEntity[entity.Index].Chunk;
            var archetype = chunk->Archetype;
            var indexInChunk = m_EntityInChunkByEntity[entity.Index].IndexInChunk;
            var typeOffset = ChunkDataUtility.GetIndexInTypeArray(archetype, typeIndex);

            SetComponentEnabled(chunk, indexInChunk, typeOffset, value);
        }

        internal void SetComponentEnabled(Chunk* chunk, int indexInChunk, int typeIndexInArchetype, bool value)
        {
            var archetype = chunk->Archetype;

            // the bit array size is padded up to 64 bits, so we validate we're not indexing outside the valid data.
            Assert.IsTrue(indexInChunk < chunk->Capacity);

            var bits = ChunkDataUtility.GetComponentEnabledRW(chunk, typeIndexInArchetype);
            var numStridesIntoBits = (indexInChunk / 64);
            var pBits = bits.Ptr + numStridesIntoBits;
            var indexInPBits = indexInChunk - (numStridesIntoBits * 64);
            var mask = 1L << indexInPBits;

            var oldBits = (long)*pBits;
            var newBits = 0L;
            var expectedOldBits = 0L;

            do
            {
                newBits = math.select(oldBits & ~mask, oldBits | mask, value);
                expectedOldBits = oldBits;
                oldBits = Interlocked.CompareExchange(ref UnsafeUtility.AsRef<long>(pBits), newBits, expectedOldBits);
            } while (expectedOldBits != oldBits);

            if (oldBits == newBits)
                return;

            // do we need increment or decrement?
            var adjustment = math.select(1, -1, value);
            var ptr = archetype->Chunks.GetPointerToChunkDisabledCountForType(typeIndexInArchetype, chunk->ListIndex);
            Interlocked.Add(ref UnsafeUtility.AsRef<int>(ptr), adjustment);
        }

        //                              | ChangeVersion | OrderVersion |
        // -----------------------------|---------------|--------------|
        // Remove(EntityBatchInChunk)   | NO            | YES          |

        // Write Component to Chunk     | YES           | NO           |
        // Remove Component In-Place    | NO            | NO           |
        // Add Entities in Chunk        | YES           | YES          |
        // Add Component In-Place       | YES           | NO           |
        // Move Chunk World             | YES           | YES          |
        //
        // ChangeVersion : e.g. Should I update LocalToWorld from Translation?
        // OrderVersion : e.g. Should I re-allocate a lookaside cache based on chunk data?


        internal void AllocateEntities(Archetype* arch, Chunk* chunk, int baseIndex, int count, Entity* outputEntities)
        {
            Assert.AreEqual(chunk->Archetype->Offsets[0], 0);
            Assert.AreEqual(chunk->Archetype->SizeOfs[0], sizeof(Entity));

            var entityInChunkStart = (Entity*)chunk->Buffer + baseIndex;

            for (var i = 0; i != count; i++)
            {
                var entityIndexInChunk = m_EntityInChunkByEntity[m_NextFreeEntityIndex].IndexInChunk;
                if (entityIndexInChunk == -1)
                {
                    IncreaseCapacity();
                    entityIndexInChunk = m_EntityInChunkByEntity[m_NextFreeEntityIndex].IndexInChunk;
                }

                var entityVersion = m_VersionByEntity[m_NextFreeEntityIndex];

                if (outputEntities != null)
                {
                    outputEntities[i].Index = m_NextFreeEntityIndex;
                    outputEntities[i].Version = entityVersion;
                }

                var entityInChunk = entityInChunkStart + i;

                entityInChunk->Index = m_NextFreeEntityIndex;
                entityInChunk->Version = entityVersion;

                m_EntityInChunkByEntity[m_NextFreeEntityIndex].IndexInChunk = baseIndex + i;
                m_ArchetypeByEntity[m_NextFreeEntityIndex] = arch;
                m_EntityInChunkByEntity[m_NextFreeEntityIndex].Chunk = chunk;
#if !DOTS_DISABLE_DEBUG_NAMES
                m_NameByEntity[m_NextFreeEntityIndex] = new EntityName();
#endif

                m_NextFreeEntityIndex = entityIndexInChunk;
                m_EntityCreateDestroyVersion++;
            }
        }

        internal void DeallocateDataEntitiesInChunk(Chunk* chunk, int indexInChunk, int batchCount)
        {
            DeallocateBuffers(chunk, indexInChunk, batchCount);
            DeallocateManagedComponents(chunk, indexInChunk, batchCount);

            var freeIndex = m_NextFreeEntityIndex;
            var entities = (Entity*)chunk->Buffer + indexInChunk;

            for (var i = batchCount - 1; i >= 0; --i)
            {
                var entityIndex = entities[i].Index;

                m_EntityInChunkByEntity[entityIndex].Chunk = null;
                m_VersionByEntity[entityIndex]++;
                m_EntityInChunkByEntity[entityIndex].IndexInChunk = freeIndex;
#if !DOTS_DISABLE_DEBUG_NAMES
                m_NameByEntity[entityIndex] = new EntityName();
#endif

                freeIndex = entityIndex;
            }

            m_NextFreeEntityIndex = freeIndex;
            m_EntityCreateDestroyVersion++;

            // Compute the number of things that need to moved and patched.
            int patchCount = Math.Min(batchCount, chunk->Count - indexInChunk - batchCount);

            if (0 == patchCount)
            {
                // if we're not patching, we still need to clear the padding bits for the entities we destroyed
                ChunkDataUtility.RemoveFromEnabledBitsHierarchicalData(chunk, indexInChunk, batchCount);
                ChunkDataUtility.ClearPaddingBits(chunk, indexInChunk, batchCount);
                return;
            }

            // updates indexInChunk to point to where the components will be moved to
            //Assert.IsTrue(chunk->archetype->sizeOfs[0] == sizeof(Entity) && chunk->archetype->offsets[0] == 0);
            var movedEntities = (Entity*)chunk->Buffer + (chunk->Count - patchCount);
            for (var i = 0; i != patchCount; i++)
                m_EntityInChunkByEntity[movedEntities[i].Index].IndexInChunk = indexInChunk + i;

            // Move component data from the end to where we deleted components
            var startIndex = chunk->Count - patchCount;
            ChunkDataUtility.Copy(chunk, startIndex, chunk, indexInChunk, patchCount);
            ChunkDataUtility.CloneEnabledBits(chunk, startIndex, chunk, indexInChunk, patchCount);

            var clearStartIndex = chunk->Count - batchCount;
            ChunkDataUtility.ClearPaddingBits(chunk, clearStartIndex, batchCount);
        }

        void DeallocateBuffers(Chunk* chunk, int indexInChunk, int batchCount)
        {
            var archetype = chunk->Archetype;

            for (var ti = 0; ti < archetype->TypesCount; ++ti)
            {
                var type = archetype->Types[ti];

                if (!type.IsBuffer)
                    continue;

                var basePtr = chunk->Buffer + archetype->Offsets[ti];
                var stride = archetype->SizeOfs[ti];

                for (int i = 0; i < batchCount; ++i)
                {
                    byte* bufferPtr = basePtr + stride * (indexInChunk + i);
                    BufferHeader.Destroy((BufferHeader*)bufferPtr);
                }
            }
        }
    }
}
