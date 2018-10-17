// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace LightWeight.PerformanceCounters.Interop
{
    internal partial class Interop
    {
        internal partial class Kernel32
        {
            [DllImport(Libraries.Kernel32, ExactSpelling=true, SetLastError=true)]
            internal static extern int WaitForSingleObject(SafeWaitHandle handle, int timeout);
        }
    }
}