using System;
using System.Buffers;
using System.IO;
using JetBrains.Annotations;
using LightWeight.PerformanceCounters.Resources;
using Microsoft.Win32.SafeHandles;

namespace LightWeight.PerformanceCounters
{
    internal class PerformanceDataRegistryKey
    {
        private const int MaxArrayPoolLength = 1024 * 1024;
        private readonly SafeRegistryHandle _keyHandle;

        private const int PerformanceData = unchecked((int)0x80000004);

        private PerformanceDataRegistryKey(SafeRegistryHandle keyHandle)
        {
            _keyHandle = keyHandle;
        }

        [NotNull]
        public static PerformanceDataRegistryKey OpenLocal()
        {
            var key = new SafeRegistryHandle(new IntPtr(PerformanceData), true);
            return new PerformanceDataRegistryKey(key);
        }

        [NotNull]
        public byte[] GetValue([NotNull] string name)
        {
            if (name == null) throw new ArgumentNullException(nameof(name));

            var size = 65000;
            var sizeInput = size;

            int ret;
            var type = 0;
            var blob = ArrayPool<byte>.Shared.Rent(size);
            while (Interop.Interop.Errors.ERROR_MORE_DATA == (ret = Interop.Interop.Advapi32.RegQueryValueEx(_keyHandle, name, null, ref type, blob, ref sizeInput)))
            {
                if (size == int.MaxValue)
                {
                    // ERROR_MORE_DATA was returned however we cannot increase the buffer size beyond Int32.MaxValue
                    Win32Error(ret, name);
                }
                else if (size > (int.MaxValue / 2))
                {
                    // at this point in the loop "size * 2" would cause an overflow
                    size = int.MaxValue;
                }
                else
                {
                    size *= 2;
                }
                sizeInput = size;
                if (blob.Length < MaxArrayPoolLength)
                {
                    ArrayPool<byte>.Shared.Return(blob);
                }

                blob = size < MaxArrayPoolLength
                           ? ArrayPool<byte>.Shared.Rent(size)
                           : new byte[size];
            }

            if (ret != 0)
            {
                Win32Error(ret, name);
            }

            return blob;
        }

        private static void Win32Error(in int errorCode, string name)
        {
            if (errorCode == Interop.Interop.Errors.ERROR_ACCESS_DENIED)
            {
                throw new UnauthorizedAccessException(SR.Format(SR.UnauthorizedAccess_RegistryKeyGeneric_Key, name));
            }

            throw new IOException(Interop.Interop.Kernel32.GetMessage(errorCode), errorCode);
        }

    }
}
