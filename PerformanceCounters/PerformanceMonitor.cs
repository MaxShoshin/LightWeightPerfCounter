using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using LightWeight.PerformanceCounters.Resources;

namespace LightWeight.PerformanceCounters
{
    internal class PerformanceMonitor
    {
        private PerformanceDataRegistryKey perfDataKey;

        internal PerformanceMonitor()
        {
            Init();
        }

        private void Init()
        {
            try
            {
                perfDataKey = PerformanceDataRegistryKey.OpenLocal();
            }
            catch (UnauthorizedAccessException)
            {
                // we need to do this for compatibility with v1.1 and v1.0.
                throw new Win32Exception(Interop.Interop.Errors.ERROR_ACCESS_DENIED);
            }
            catch (IOException e)
            {
                // we need to do this for compatibility with v1.1 and v1.0.
                throw new Win32Exception(Marshal.GetHRForException(e));
            }
        }


        // Win32 RegQueryValueEx for perf data could deadlock (for a Mutex) up to 2mins in some
        // scenarios before they detect it and exit gracefully. In the mean time, ERROR_BUSY,
        // ERROR_NOT_READY etc can be seen by other concurrent calls (which is the reason for the
        // wait loop and switch case below). We want to wait most certainly more than a 2min window.
        // The curent wait time of up to 10mins takes care of the known stress deadlock issues. In most
        // cases we wouldn't wait for more than 2mins anyways but in worst cases how much ever time
        // we wait may not be sufficient if the Win32 code keeps running into this deadlock again
        // and again. A condition very rare but possible in theory. We would get back to the user
        // in this case with InvalidOperationException after the wait time expires.
        internal byte[] GetData(string item)
        {
            var waitRetries = 17;   //2^16*10ms == approximately 10mins
            var waitSleep = 0;
            byte[] data = null;
            var error = 0;

            // no need to revert here since we'll fall off the end of the method
            while (waitRetries > 0)
            {
                try
                {
                    data = perfDataKey.GetValue(item);
                    return data;
                }
                catch (IOException e)
                {
                    error = Marshal.GetHRForException(e);
                    switch (error)
                    {
                        case Interop.Interop.Advapi32.RPCStatus.RPC_S_CALL_FAILED:
                        case Interop.Interop.Errors.ERROR_INVALID_HANDLE:
                        case Interop.Interop.Advapi32.RPCStatus.RPC_S_SERVER_UNAVAILABLE:
                            Init();
                            goto case Interop.Interop.Advapi32.WaitOptions.WAIT_TIMEOUT;

                        case Interop.Interop.Advapi32.WaitOptions.WAIT_TIMEOUT:
                        case Interop.Interop.Errors.ERROR_NOT_READY:
                        case Interop.Interop.Errors.ERROR_LOCK_FAILED:
                        case Interop.Interop.Errors.ERROR_BUSY:
                            --waitRetries;
                            if (waitSleep == 0)
                            {
                                waitSleep = 10;
                            }
                            else
                            {
                                Thread.Sleep(waitSleep);
                                waitSleep *= 2;
                            }
                            break;

                        default:
                            throw SharedUtils.CreateSafeWin32Exception(error);
                    }
                }
                catch (InvalidCastException e)
                {
                    throw new InvalidOperationException(SR.Format(SR.CounterDataCorrupt, perfDataKey.ToString()), e);
                }
            }

            throw SharedUtils.CreateSafeWin32Exception(error);
        }

    }
}