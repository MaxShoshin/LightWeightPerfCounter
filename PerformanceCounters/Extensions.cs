using System;

namespace LightWeight.PerformanceCounters
{
    internal static class Extensions
    {
#if !netcoreapp

        internal static T Read<T>(ReadOnlySpan<byte> span) where T : struct
            => System.Runtime.InteropServices.MemoryMarshal.Read<T>(span);

        internal static ref readonly T AsRef<T>(ReadOnlySpan<byte> span) where T : struct
            => ref System.Runtime.InteropServices.MemoryMarshal.Cast<byte, T>(span)[0];
#endif

    }
}