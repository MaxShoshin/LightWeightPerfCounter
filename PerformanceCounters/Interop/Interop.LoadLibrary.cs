// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Runtime.InteropServices;

namespace LightWeight.PerformanceCounters.Interop
{
    internal partial class Interop
    {
        internal partial class Kernel32
        {
            [DllImport(Libraries.Kernel32, CharSet = CharSet.Unicode, SetLastError = true)]
            public static extern IntPtr LoadLibrary(string libFilename);
        }
    }
}

