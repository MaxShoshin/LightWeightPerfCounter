// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;

namespace LightWeight.PerformanceCounters.Interop
{
    internal partial class Interop
    {
        internal class PerfCounter
        {
            [DllImport(Libraries.PerfCounter, CharSet = CharSet.Unicode)]
            public static extern int FormatFromRawValue(
                uint dwCounterType,
                uint dwFormat,
                ref long pTimeBase,
                ref Kernel32.PerformanceCounterOptions.PDH_RAW_COUNTER pRawValue1,
                ref Kernel32.PerformanceCounterOptions.PDH_RAW_COUNTER pRawValue2,
                ref Kernel32.PerformanceCounterOptions.PDH_FMT_COUNTERVALUE pFmtValue
            );
        }
    }
}
