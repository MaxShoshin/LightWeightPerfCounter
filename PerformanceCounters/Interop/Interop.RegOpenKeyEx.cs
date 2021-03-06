// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace LightWeight.PerformanceCounters.Interop
{
    internal partial class Interop
    {
        internal partial class Advapi32
        {
            [DllImport(Libraries.Advapi32, CharSet = CharSet.Unicode, BestFitMapping = false, EntryPoint = "RegOpenKeyExW")]
            internal static extern int RegOpenKeyEx(
                SafeRegistryHandle hKey,
                string lpSubKey,
                int ulOptions,
                int samDesired,
                out SafeRegistryHandle hkResult);


            [DllImport(Libraries.Advapi32, CharSet = CharSet.Unicode, BestFitMapping = false, EntryPoint = "RegOpenKeyExW")]
            internal static extern int RegOpenKeyEx(
                IntPtr hKey,
                string lpSubKey,
                int ulOptions,
                int samDesired,
                out SafeRegistryHandle hkResult);
        }
    }
}
