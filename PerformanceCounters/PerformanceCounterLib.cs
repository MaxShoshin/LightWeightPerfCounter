// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using LightWeight.PerformanceCounters.Resources;
using Microsoft.Win32;


namespace LightWeight.PerformanceCounters
{
    internal class PerformanceCounterLib
    {
        internal static bool IsBaseCounter(int type)
        {
            return (type == Interop.Interop.Kernel32.PerformanceCounterOptions.PERF_AVERAGE_BASE ||
                    type == Interop.Interop.Kernel32.PerformanceCounterOptions.PERF_COUNTER_MULTI_BASE ||
                    type == Interop.Interop.Kernel32.PerformanceCounterOptions.PERF_RAW_BASE ||
                    type == Interop.Interop.Kernel32.PerformanceCounterOptions.PERF_LARGE_RAW_BASE ||
                    type == Interop.Interop.Kernel32.PerformanceCounterOptions.PERF_SAMPLE_BASE);
        }

    }
}
