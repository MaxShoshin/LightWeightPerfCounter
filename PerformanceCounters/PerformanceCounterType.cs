// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace LightWeight.PerformanceCounters
{
    /// <summary>
    ///     Enum of friendly names to counter types (maps directory to the native types)
    /// </summary>
    public enum PerformanceCounterType
    {
        NumberOfItems32 = Interop.Interop.Kernel32.PerformanceCounterOptions.PERF_COUNTER_RAWCOUNT,
        NumberOfItems64 = Interop.Interop.Kernel32.PerformanceCounterOptions.PERF_COUNTER_LARGE_RAWCOUNT,
        NumberOfItemsHEX32 = Interop.Interop.Kernel32.PerformanceCounterOptions.PERF_COUNTER_RAWCOUNT_HEX,
        NumberOfItemsHEX64 = Interop.Interop.Kernel32.PerformanceCounterOptions.PERF_COUNTER_LARGE_RAWCOUNT_HEX,
        RateOfCountsPerSecond32 = Interop.Interop.Kernel32.PerformanceCounterOptions.PERF_COUNTER_COUNTER,
        RateOfCountsPerSecond64 = Interop.Interop.Kernel32.PerformanceCounterOptions.PERF_COUNTER_BULK_COUNT,
        CountPerTimeInterval32 = Interop.Interop.Kernel32.PerformanceCounterOptions.PERF_COUNTER_QUEUELEN_TYPE,
        CountPerTimeInterval64 = Interop.Interop.Kernel32.PerformanceCounterOptions.PERF_COUNTER_LARGE_QUEUELEN_TYPE,
        RawFraction = Interop.Interop.Kernel32.PerformanceCounterOptions.PERF_RAW_FRACTION,
        RawBase = Interop.Interop.Kernel32.PerformanceCounterOptions.PERF_RAW_BASE,

        AverageTimer32 = Interop.Interop.Kernel32.PerformanceCounterOptions.PERF_AVERAGE_TIMER,
        AverageBase = Interop.Interop.Kernel32.PerformanceCounterOptions.PERF_AVERAGE_BASE,
        AverageCount64 = Interop.Interop.Kernel32.PerformanceCounterOptions.PERF_AVERAGE_BULK,

        SampleFraction = Interop.Interop.Kernel32.PerformanceCounterOptions.PERF_SAMPLE_FRACTION,
        SampleCounter = Interop.Interop.Kernel32.PerformanceCounterOptions.PERF_SAMPLE_COUNTER,
        SampleBase = Interop.Interop.Kernel32.PerformanceCounterOptions.PERF_SAMPLE_BASE,

        CounterTimer = Interop.Interop.Kernel32.PerformanceCounterOptions.PERF_COUNTER_TIMER,
        CounterTimerInverse = Interop.Interop.Kernel32.PerformanceCounterOptions.PERF_COUNTER_TIMER_INV,
        Timer100Ns = Interop.Interop.Kernel32.PerformanceCounterOptions.PERF_100NSEC_TIMER,
        Timer100NsInverse = Interop.Interop.Kernel32.PerformanceCounterOptions.PERF_100NSEC_TIMER_INV,
        ElapsedTime = Interop.Interop.Kernel32.PerformanceCounterOptions.PERF_ELAPSED_TIME,
        CounterMultiTimer = Interop.Interop.Kernel32.PerformanceCounterOptions.PERF_COUNTER_MULTI_TIMER,
        CounterMultiTimerInverse = Interop.Interop.Kernel32.PerformanceCounterOptions.PERF_COUNTER_MULTI_TIMER_INV,
        CounterMultiTimer100Ns = Interop.Interop.Kernel32.PerformanceCounterOptions.PERF_100NSEC_MULTI_TIMER,
        CounterMultiTimer100NsInverse = Interop.Interop.Kernel32.PerformanceCounterOptions.PERF_100NSEC_MULTI_TIMER_INV,
        CounterMultiBase = Interop.Interop.Kernel32.PerformanceCounterOptions.PERF_COUNTER_MULTI_BASE,

        CounterDelta32 = Interop.Interop.Kernel32.PerformanceCounterOptions.PERF_COUNTER_DELTA,
        CounterDelta64 = Interop.Interop.Kernel32.PerformanceCounterOptions.PERF_COUNTER_LARGE_DELTA
    }
}