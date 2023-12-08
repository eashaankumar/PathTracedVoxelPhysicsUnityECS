using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs.LowLevel.Unsafe;
using UnityEngine;

// Mark this struct as a NativeContainer, usually this would be a generic struct for containers, but a counter does not need to be generic
// TODO - why does a counter not need to be generic? - explain the argument for this reasoning please.
[StructLayout(LayoutKind.Sequential)]
[NativeContainer]
unsafe public struct NativeCounter
{
    // The actual pointer to the allocated count needs to have restrictions relaxed so jobs can be schedled with this container
    [NativeDisableUnsafePtrRestriction]
    int* m_Counter;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
    AtomicSafetyHandle m_Safety;
    // The dispose sentinel tracks memory leaks. It is a managed type so it is cleared to null when scheduling a job
    // The job cannot dispose the container, and no one else can dispose it until the job has run, so it is ok to not pass it along
    // This attribute is required, without it this NativeContainer cannot be passed to a job; since that would give the job access to a managed object
    [NativeSetClassTypeToNullOnSchedule]
    DisposeSentinel m_DisposeSentinel;
#endif

    // Keep track of where the memory for this was allocated
    Allocator m_AllocatorLabel;

    public NativeCounter(Allocator label)
    {
        // This check is redundant since we always use an int that is blittable.
        // It is here as an example of how to check for type correctness for generic types.
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        if (!UnsafeUtility.IsBlittable<int>())
            throw new ArgumentException(string.Format("{0} used in NativeQueue<{0}> must be blittable", typeof(int)));
#endif
        m_AllocatorLabel = label;

        // Allocate native memory for a single integer
        m_Counter = (int*)UnsafeUtility.Malloc(UnsafeUtility.SizeOf<int>(), 4, label);

        // Create a dispose sentinel to track memory leaks. This also creates the AtomicSafetyHandle
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        DisposeSentinel.Create(out m_Safety, out m_DisposeSentinel, 0, Allocator.Persistent);
#endif
        // Initialize the count to 0 to avoid uninitialized data
        Count = 0;
    }

    public void Increment()
    {
        // Verify that the caller has write permission on this data. 
        // This is the race condition protection, without these checks the AtomicSafetyHandle is useless
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
        (*m_Counter)++;
    }

    public int Count
    {
        get
        {
            // Verify that the caller has read permission on this data. 
            // This is the race condition protection, without these checks the AtomicSafetyHandle is useless
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
            return *m_Counter;
        }
        set
        {
            // Verify that the caller has write permission on this data. This is the race condition protection, without these checks the AtomicSafetyHandle is useless
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
            *m_Counter = value;
        }
    }

    public bool IsCreated
    {
        get { return m_Counter != null; }
    }

    public void Dispose()
    {
        // Let the dispose sentinel know that the data has been freed so it does not report any memory leaks
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        DisposeSentinel.Dispose(ref m_Safety, ref m_DisposeSentinel);
#endif

        UnsafeUtility.Free(m_Counter, m_AllocatorLabel);
        m_Counter = null;
    }

    [NativeContainer]
    // This attribute is what makes it possible to use NativeCounter.Concurrent in a ParallelFor job
    [NativeContainerIsAtomicWriteOnly]
    unsafe public struct Concurrent
    {
        // Copy of the pointer from the full NativeCounter
        [NativeDisableUnsafePtrRestriction]
        int* m_Counter;

        // Copy of the AtomicSafetyHandle from the full NativeCounter. The dispose sentinel is not copied since this inner struct does not own the memory and is not responsible for freeing it.
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        AtomicSafetyHandle m_Safety;
#endif

        // This is what makes it possible to assign to NativeCounter.Concurrent from NativeCounter
        public static implicit operator NativeCounter.Concurrent(NativeCounter cnt)
        {
            NativeCounter.Concurrent concurrent;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(cnt.m_Safety);
            concurrent.m_Safety = cnt.m_Safety;
            AtomicSafetyHandle.UseSecondaryVersion(ref concurrent.m_Safety);
#endif

            concurrent.m_Counter = cnt.m_Counter;
            return concurrent;
        }

        public void Increment()
        {
            // Increment still needs to check for write permissions
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(m_Safety);
#endif
            // The actual increment is implemented with an atomic, since it can be incremented by multiple threads at the same time
            Interlocked.Increment(ref *m_Counter);
        }
    }
}

//https://coffeebraingames.wordpress.com/2021/10/24/some-dots-utilities-nativecounter-and-nativesum/

[StructLayout(LayoutKind.Sequential)]
[NativeContainer]
public unsafe struct NativeSum
{
    // The actual pointer to the allocated sum needs to have restrictions relaxed so jobs can be scheduled with this utility
    [NativeDisableUnsafePtrRestriction]
    private int* sumIntegers;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
    private AtomicSafetyHandle m_Safety;

    // The dispose sentinel tracks memory leaks. It is a managed type so it is cleared to null when scheduling a job
    // The job cannot dispose the container, and no one else can dispose it until the job has run, so it is ok to not pass it along
    // This attribute is required, without it this NativeContainer cannot be passed to a job; since that would give the job access to a managed object
    [NativeSetClassTypeToNullOnSchedule]
    private DisposeSentinel m_DisposeSentinel;
#endif

    // Keep track of where the memory for this was allocated
    private readonly Allocator m_AllocatorLabel;

    public const int INTS_PER_CACHE_LINE = JobsUtility.CacheLineSize / sizeof(int);

    public NativeSum(Allocator label)
    {
        // This check is redundant since we always use an int that is blittable.
        // It is here as an example of how to check for type correctness for generic types.
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        if (!UnsafeUtility.IsBlittable<int>())
        {
            throw new ArgumentException(
                string.Format("{0} used in NativeQueue<{0}> must be blittable", typeof(int)));
        }
#endif
        this.m_AllocatorLabel = label;

        // Allocate native memory for a single integer
        this.sumIntegers = (int*)UnsafeUtility.Malloc(
            UnsafeUtility.SizeOf<int>() * INTS_PER_CACHE_LINE * JobsUtility.MaxJobThreadCount, 4, label);

        // Create a dispose sentinel to track memory leaks. This also creates the AtomicSafetyHandle
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        DisposeSentinel.Create(out this.m_Safety, out this.m_DisposeSentinel, 0, label);
#endif

        Clear();
    }

    public void Clear()
    {
        // Clear uninitialized data
        // Verify that the caller has write permission on this data. 
        // This is the race condition protection, without these checks the AtomicSafetyHandle is useless
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        AtomicSafetyHandle.CheckWriteAndThrow(this.m_Safety);
#endif

        for (int i = 0; i < JobsUtility.MaxJobThreadCount; ++i)
        {
            this.sumIntegers[INTS_PER_CACHE_LINE * i] = 0;
        }
    }

    public void Add(int amount)
    {
        // Verify that the caller has write permission on this data. 
        // This is the race condition protection, without these checks the AtomicSafetyHandle is useless
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        AtomicSafetyHandle.CheckWriteAndThrow(this.m_Safety);
#endif
        (*this.sumIntegers) += amount;
    }

    public int Total
    {
        get
        {
            // Verify that the caller has read permission on this data. 
            // This is the race condition protection, without these checks the AtomicSafetyHandle is useless
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(this.m_Safety);
#endif
            int total = 0;
            for (int i = 0; i < JobsUtility.MaxJobThreadCount; ++i)
            {
                total += this.sumIntegers[INTS_PER_CACHE_LINE * i];
            }

            return total;
        }
    }

    public bool IsCreated
    {
        get
        {
            return this.sumIntegers != null;
        }
    }

    public void Dispose()
    {
        // Let the dispose sentinel know that the data has been freed so it does not report any memory leaks
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        DisposeSentinel.Dispose(ref this.m_Safety, ref this.m_DisposeSentinel);
#endif

        UnsafeUtility.Free(this.sumIntegers, this.m_AllocatorLabel);
        this.sumIntegers = null;
    }

    [NativeContainer]
    // This attribute is what makes it possible to use NativeSum.ParallelWriter in a ParallelFor job
    [NativeContainerIsAtomicWriteOnly]
    public struct ParallelWriter
    {
        // Copy of the pointer from the main NativeSum
        [NativeDisableUnsafePtrRestriction]
        private int* dataPointer;

        // Copy of the AtomicSafetyHandle from the full NativeCounter. The dispose sentinel is not copied since this inner struct does not own the memory and is not responsible for freeing it.
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        private AtomicSafetyHandle m_Safety;
#endif

        // The current worker thread index; it must use this exact name since it is injected
        [NativeSetThreadIndex]
        int m_ThreadIndex;

        // This is what makes it possible to assign to NativeCounter.Concurrent from NativeCounter
        public static implicit operator ParallelWriter(NativeSum sum)
        {
            ParallelWriter parallelWriter;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(sum.m_Safety);
            parallelWriter.m_Safety = sum.m_Safety;
            AtomicSafetyHandle.UseSecondaryVersion(ref parallelWriter.m_Safety);
#endif

            parallelWriter.dataPointer = sum.sumIntegers;
            parallelWriter.m_ThreadIndex = 0;

            return parallelWriter;
        }

        public void Add(int amount)
        {
            // Increment still needs to check for write permissions
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndThrow(this.m_Safety);
#endif

            // No need for atomics any more since we are just incrementing the local count
            this.dataPointer[INTS_PER_CACHE_LINE * this.m_ThreadIndex] += amount;
        }
    }
}