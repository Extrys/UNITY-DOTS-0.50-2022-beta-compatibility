using System;
using Unity.Burst;
using Unity.Jobs;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Profiling;
using System.Diagnostics;

namespace Unity.Entities
{
    /// <summary>
    /// IJobChunk is a type of [Job](https://docs.unity3d.com/ScriptReference/Unity.Jobs.IJob.html) that iterates over
    /// a set of <see cref="ArchetypeChunk"/> instances.
    /// </summary>
    /// <remarks>
    /// Create and schedule an IJobChunk Job inside the OnUpdate() function of a <see cref="SystemBase"/> implementation.
    /// The Job component system calls
    /// the Execute function once for each <see cref="ArchetypeChunk"/> found by the <see cref="EntityQuery"/> used to
    /// schedule the Job.
    ///
    /// To pass data to the Execute function beyond the parameters of the Execute() function, add public fields to the
    /// IJobChunk struct declaration and set those fields immediately before scheduling the Job. You must pass the
    /// component type information for any components that the Job reads or writes using a field of type,
    /// <seealso cref="ComponentTypeHandle{T}"/>. Get this type information by calling the appropriate
    /// <seealso cref="ComponentSystemBase.GetComponentTypeHandle{T}"/> function for the type of
    /// component.
    ///
    /// For more information see [Using IJobChunk](xref:ecs-ijobchunk).
    /// <example>
    /// <code source="../DocCodeSamples.Tests/ChunkIterationJob.cs" region="basic-ijobchunk" title="IJobChunk Example"/>
    /// </example>
    /// </remarks>
    [JobProducerType(typeof(JobChunkExtensions.JobChunkProducer<>))]
    public interface IJobChunk
    {
        // firstEntityIndex refers to the index of the first entity in the current chunk within the EntityQuery the job was scheduled with
        // For example, if the job operates on 3 chunks with 20 entities each, then the firstEntityIndices will be [0, 20, 40] respectively
        /// <summary>
        /// Implement the Execute() function to perform a unit of work on an <see cref="ArchetypeChunk"/>.
        /// </summary>
        /// <remarks>The Job component system calls the Execute function once for each <see cref="EntityArchetype"/>
        /// found by the <see cref="EntityQuery"/> used to schedule the Job.</remarks>
        /// <param name="chunk">The current chunk.</param>
        /// <param name="chunkIndex">The index of the current chunk within the list of all chunks found by the
        /// Job's <see cref="EntityQuery"/>. Note that chunks are not processed in index order, except by chance.</param>
        /// <param name="firstEntityIndex">The index of the first entity in the current chunk within the list of all
        /// entities in all the chunks found by the Job's <see cref="EntityQuery"/>.</param>
        void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex);
    }

    /// <summary>
    /// Extensions for scheduling and running IJobChunk Jobs.
    /// </summary>
    public static class JobChunkExtensions
    {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        [NativeContainer]
        internal struct EntitySafetyHandle
        {
            internal AtomicSafetyHandle m_Safety;
        }
#endif
        internal struct JobChunkWrapper<T> where T : struct
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
#pragma warning disable 414
            [ReadOnly] internal EntitySafetyHandle safety;
#pragma warning restore
#endif
            internal T JobData;

            [NativeDisableContainerSafetyRestriction]
            [DeallocateOnJobCompletion]
            internal NativeArray<byte> PrefilterData;

            internal int IsParallel;
        }

        /// <summary>
        /// This method is only to be called by automatically generated setup code.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        public static void EarlyJobInit<T>()
            where T : struct, IJobChunk
        {
            JobChunkProducer<T>.Initialize();
        }

        [Conditional("ENABLE_UNITY_COLLECTIONS_CHECKS")]
        private static void CheckReflectionDataCorrect(IntPtr reflectionData)
        {
            if (reflectionData == IntPtr.Zero)
                throw new InvalidOperationException("Reflection data was not set up by an Initialize() call");
        }

        /// <summary>
        /// Adds an IJobChunk instance to the Job scheduler queue for parallel execution.
        /// Note: This method is being replaced with use of ScheduleParallel to make non-sequential execution explicit.
        /// </summary>
        /// <param name="jobData">An IJobChunk instance.</param>
        /// <param name="query">The query selecting chunks with the necessary components.</param>
        /// <param name="dependsOn">The handle identifying already scheduled Jobs that could constrain this Job.
        /// A Job that writes to a component must run before other Jobs that read or write that component. Jobs that
        /// only read the same components can run in parallel.</param>
        /// <typeparam name="T">The specific IJobChunk implementation type.</typeparam>
        /// <returns>A handle that combines the current Job with previous dependencies identified by the `dependsOn`
        /// parameter.</returns>
        public static unsafe JobHandle Schedule<T>(this T jobData, EntityQuery query, JobHandle dependsOn = default(JobHandle))
            where T : struct, IJobChunk
        {
            return ScheduleInternal(ref jobData, query, dependsOn, ScheduleMode.Parallel, true);
        }

        /// <summary>
        /// Adds an IJobChunk instance to the Job scheduler queue for parallel execution.
        /// Note: This method is being replaced with use of ScheduleParallel to make non-sequential execution explicit.
        /// </summary>
        /// <param name="jobData">An IJobChunk instance. In this variant, the jobData is passed by reference,
        /// which may be necessary for unusually large job structs.</param>
        /// <param name="query">The query selecting chunks with the necessary components.</param>
        /// <param name="dependsOn">The handle identifying already scheduled Jobs that could constrain this Job.
        /// A Job that writes to a component must run before other Jobs that read or write that component. Jobs that
        /// only read the same components can run in parallel.</param>
        /// <typeparam name="T">The specific IJobChunk implementation type.</typeparam>
        /// <returns>A handle that combines the current Job with previous dependencies identified by the `dependsOn`
        /// parameter.</returns>
        public static unsafe JobHandle ScheduleByRef<T>(this ref T jobData, EntityQuery query, JobHandle dependsOn = default(JobHandle))
            where T : struct, IJobChunk
        {
            return ScheduleInternal(ref jobData, query, dependsOn, ScheduleMode.Parallel, true);
        }

        /// <summary>
        /// Adds an IJobChunk instance to the Job scheduler queue for sequential (non-parallel) execution.
        /// </summary>
        /// <param name="jobData">An IJobChunk instance.</param>
        /// <param name="query">The query selecting chunks with the necessary components.</param>
        /// <param name="dependsOn">The handle identifying already scheduled Jobs that could constrain this Job.
        /// A Job that writes to a component must run before other Jobs that read or write that component. Jobs that
        /// only read the same components can run in parallel.</param>
        /// <typeparam name="T">The specific IJobChunk implementation type.</typeparam>
        /// <returns>A handle that combines the current Job with previous dependencies identified by the `dependsOn`
        /// parameter.</returns>
        public static unsafe JobHandle ScheduleSingle<T>(this T jobData, EntityQuery query, JobHandle dependsOn = default(JobHandle))
            where T : struct, IJobChunk
        {
            return ScheduleInternal(ref jobData, query, dependsOn, ScheduleMode.Parallel, false);
        }

        /// <summary>
        /// Adds an IJobChunk instance to the Job scheduler queue for sequential (non-parallel) execution.
        /// </summary>
        /// <param name="jobData">An IJobChunk instance. In this variant, the jobData is passed by reference,
        /// which may be necessary for unusually large job structs.</param>
        /// <param name="query">The query selecting chunks with the necessary components.</param>
        /// <param name="dependsOn">The handle identifying already scheduled Jobs that could constrain this Job.
        /// A Job that writes to a component must run before other Jobs that read or write that component. Jobs that
        /// only read the same components can run in parallel.</param>
        /// <typeparam name="T">The specific IJobChunk implementation type.</typeparam>
        /// <returns>A handle that combines the current Job with previous dependencies identified by the `dependsOn`
        /// parameter.</returns>
        public static unsafe JobHandle ScheduleSingleByRef<T>(this ref T jobData, EntityQuery query, JobHandle dependsOn = default(JobHandle))
            where T : struct, IJobChunk
        {
            return ScheduleInternal(ref jobData, query, dependsOn, ScheduleMode.Parallel, false);
        }

        /// <summary>
        /// Adds an IJobChunk instance to the Job scheduler queue for parallel execution.
        /// </summary>
        /// <param name="jobData">An IJobChunk instance.</param>
        /// <param name="query">The query selecting chunks with the necessary components.</param>
        /// <param name="dependsOn">The handle identifying already scheduled Jobs that could constrain this Job.
        /// A Job that writes to a component must run before other Jobs that read or write that component. Jobs that
        /// only read the same components can run in parallel.</param>
        /// <typeparam name="T">The specific IJobChunk implementation type.</typeparam>
        /// <returns>A handle that combines the current Job with previous dependencies identified by the `dependsOn`
        /// parameter.</returns>
        public static unsafe JobHandle ScheduleParallel<T>(this T jobData, EntityQuery query, JobHandle dependsOn = default(JobHandle))
            where T : struct, IJobChunk
        {
            return ScheduleInternal(ref jobData, query, dependsOn, ScheduleMode.Parallel, true);
        }

        /// <summary>
        /// Adds an IJobChunk instance to the Job scheduler queue for parallel execution.
        /// </summary>
        /// <param name="jobData">An IJobChunk instance. In this variant, the jobData is passed by reference,
        /// which may be necessary for unusually large job structs.</param>
        /// <param name="query">The query selecting chunks with the necessary components.</param>
        /// <param name="dependsOn">The handle identifying already scheduled Jobs that could constrain this Job.
        /// A Job that writes to a component must run before other Jobs that read or write that component. Jobs that
        /// only read the same components can run in parallel.</param>
        /// <typeparam name="T">The specific IJobChunk implementation type.</typeparam>
        /// <returns>A handle that combines the current Job with previous dependencies identified by the `dependsOn`
        /// parameter.</returns>
        public static unsafe JobHandle ScheduleParallelByRef<T>(this ref T jobData, EntityQuery query, JobHandle dependsOn = default(JobHandle))
            where T : struct, IJobChunk
        {
            return ScheduleInternal(ref jobData, query, dependsOn, ScheduleMode.Parallel, true);
        }

        /// <summary>
        /// Runs the Job immediately on the current thread.
        /// </summary>
        /// <param name="jobData">An IJobChunk instance.</param>
        /// <param name="query">The query selecting chunks with the necessary components.</param>
        /// <typeparam name="T">The specific IJobChunk implementation type.</typeparam>
        public static unsafe void Run<T>(this T jobData, EntityQuery query)
            where T : struct, IJobChunk
        {
            ScheduleInternal(ref jobData, query, default(JobHandle), ScheduleMode.Run, false);
        }

        /// <summary>
        /// Runs the Job immediately on the current thread.
        /// </summary>
        /// <param name="jobData">An IJobChunk instance. In this variant, the jobData is passed by reference,
        /// which may be necessary for unusually large job structs.</param>
        /// <param name="query">The query selecting chunks with the necessary components.</param>
        /// <typeparam name="T">The specific IJobChunk implementation type.</typeparam>
        public static unsafe void RunByRef<T>(this ref T jobData, EntityQuery query)
            where T : struct, IJobChunk
        {
            ScheduleInternal(ref jobData, query, default(JobHandle), ScheduleMode.Run, false);
        }

        /// <summary>
        /// Runs the job using an ArchetypeChunkIterator instead of the jobs API.
        /// </summary>
        /// <param name="jobData">The job to execute.</param>
        /// <param name="chunkIterator">The ArchetypeChunkIterator of the EntityQuery to run over.</param>
        /// <typeparam name="T">The specific IJobChunk implementation type.</typeparam>
        public static unsafe void RunWithoutJobs<T>(this ref T jobData, ref ArchetypeChunkIterator chunkIterator)
            where T : struct, IJobChunk
        {
            var chunkCount = 0;

            while (chunkIterator.MoveNext())
            {
                var archetypeChunk = chunkIterator.CurrentArchetypeChunk;
                jobData.Execute(archetypeChunk, chunkCount, chunkIterator.CurrentChunkFirstEntityIndex);

                chunkCount++;
            }
        }

        internal static unsafe JobHandle ScheduleInternal<T>(ref T jobData, EntityQuery query, JobHandle dependsOn, ScheduleMode mode, bool isParallel = true)
            where T : struct, IJobChunk
        {
            var unfilteredChunkCount = query.CalculateChunkCountWithoutFiltering();
            var impl = query._GetImpl();

            var prefilterHandle = ChunkIterationUtility.PreparePrefilteredChunkListsAsync(unfilteredChunkCount,

                impl->_QueryData->MatchingArchetypes, impl->_Filter, dependsOn, mode,
                out NativeArray<byte> prefilterData,
                out void* deferredCountData,
                out var useFiltering);

            JobChunkWrapper<T> jobChunkWrapper = new JobChunkWrapper<T>
            {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
                // All IJobChunk jobs have a EntityManager safety handle to ensure that BeforeStructuralChange throws an error if
                // jobs without any other safety handles are still running (haven't been synced).
                safety = new EntitySafetyHandle { m_Safety = impl->_Access->DependencyManager->Safety.GetEntityManagerSafetyHandle() },
#endif

                JobData = jobData,
                PrefilterData = prefilterData,
                IsParallel = isParallel ? 1 : 0
            };

            var reflectionData = JobChunkProducer<T>.reflectionData.Data;
            CheckReflectionDataCorrect(reflectionData);

            var scheduleParams = new JobsUtility.JobScheduleParameters(
                UnsafeUtility.AddressOf(ref jobChunkWrapper),
                reflectionData,
                prefilterHandle,
                mode);

#if UNITY_DOTSRUNTIME
            try
            {
                if (!isParallel)
                {
                    return JobsUtility.Schedule(ref scheduleParams);
                }
                else
                {
                    if (useFiltering)
                        return JobsUtility.ScheduleParallelForDeferArraySize(ref scheduleParams, 1, deferredCountData, null);
                    else
                        return JobsUtility.ScheduleParallelFor(ref scheduleParams, unfilteredChunkCount, 1);
                }
            }
            catch (InvalidOperationException e)
            {
                prefilterHandle.Complete();
                prefilterData.Dispose();
                throw e;
            }
#else
            // We can't use try {} catch {} with 2020.2 as we will be burst compiling the schedule code.
            // Burst doesn't support exception handling.
            bool executedManaged = false;
            JobHandle result = default;
            FinalizeScheduleChecked(isParallel, unfilteredChunkCount, prefilterHandle, prefilterData, deferredCountData, useFiltering, ref scheduleParams, ref executedManaged, ref result);

            if (executedManaged)
                return result;

            return FinalizeScheduleNoExceptions(isParallel, unfilteredChunkCount, deferredCountData, useFiltering, ref scheduleParams);
#endif
        }

#if !UNITY_DOTSRUNTIME
        // Burst does not support exception handling.
        [BurstDiscard]
        private static unsafe void FinalizeScheduleChecked(bool isParallel, int unfilteredChunkCount, JobHandle prefilterHandle, NativeArray<byte> prefilterData, void* deferredCountData, bool useFiltering, ref JobsUtility.JobScheduleParameters scheduleParams, ref bool executed, ref JobHandle result)
        {
            executed = true;

            try
            {
                result = FinalizeScheduleNoExceptions(isParallel, unfilteredChunkCount, deferredCountData, useFiltering, ref scheduleParams);
            }
            catch (InvalidOperationException e)
            {
                prefilterHandle.Complete();
                prefilterData.Dispose();
                throw e;
            }
        }

        private static unsafe JobHandle FinalizeScheduleNoExceptions(bool isParallel, int unfilteredChunkCount, void* deferredCountData, bool useFiltering, ref JobsUtility.JobScheduleParameters scheduleParams)
        {
            if (!isParallel)
            {
                return JobsUtility.Schedule(ref scheduleParams);
            }
            else
            {
                if (useFiltering)
                    return JobsUtility.ScheduleParallelForDeferArraySize(ref scheduleParams, 1, deferredCountData, null);
                else
                    return JobsUtility.ScheduleParallelFor(ref scheduleParams, unfilteredChunkCount, 1);
            }
        }
#endif

        internal struct JobChunkProducer<T>
            where T : struct, IJobChunk
        {
            internal static readonly SharedStatic<IntPtr> reflectionData = SharedStatic<IntPtr>.GetOrCreate<JobChunkProducer<T>>();

            internal static void Initialize()
            {
                reflectionData.Data = JobsUtility.CreateJobReflectionData(typeof(JobChunkWrapper<T>), typeof(T), (ExecuteJobFunction)Execute);
            }

            internal delegate void ExecuteJobFunction(ref JobChunkWrapper<T> jobWrapper, System.IntPtr additionalPtr, System.IntPtr bufferRangePatchData, ref JobRanges ranges, int jobIndex);

            public static void Execute(ref JobChunkWrapper<T> jobWrapper, System.IntPtr additionalPtr, System.IntPtr bufferRangePatchData, ref JobRanges ranges, int jobIndex)
            {
                ExecuteInternal(ref jobWrapper, bufferRangePatchData, ref ranges, jobIndex);
            }

            internal unsafe static void ExecuteInternal(ref JobChunkWrapper<T> jobWrapper, System.IntPtr bufferRangePatchData, ref JobRanges ranges, int jobIndex)
            {
                ChunkIterationUtility.UnpackPrefilterData(jobWrapper.PrefilterData, out var filteredChunks, out var entityIndices, out var chunkCount);

                bool isParallel = jobWrapper.IsParallel == 1;
                while (true)
                {
                    int beginChunkIndex = 0;
                    int endChunkIndex = chunkCount;

                    // If we are running the job in parallel, steal some work.
                    if (isParallel)
                    {
                        // If we have no range to steal, exit the loop.
                        if (!JobsUtility.GetWorkStealingRange(ref ranges, jobIndex, out beginChunkIndex, out endChunkIndex))
                            break;
                    }

                    // Do the actual user work.
                    for (int chunkIndex = beginChunkIndex; chunkIndex < endChunkIndex; ++chunkIndex)
                    {
                        var chunk = filteredChunks[chunkIndex];
                        var entityOffset = entityIndices[chunkIndex];

#if ENABLE_UNITY_COLLECTIONS_CHECKS
                        if(isParallel)
                        {
                            JobsUtility.PatchBufferMinMaxRanges(bufferRangePatchData, UnsafeUtility.AddressOf(ref jobWrapper), entityOffset, chunk.Count);
                        }
#endif
                        jobWrapper.JobData.Execute(chunk, chunkIndex, entityOffset);
                    }

                    // If we are not running in parallel, our job is done.
                    if (!isParallel)
                        break;
                }
            }
        }
    }
}
