// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using LightWeight.PerformanceCounters.Resources;

namespace LightWeight.PerformanceCounters
{
    /// <summary>
    ///     Performance Counter component.
    ///     This class provides support for NT Performance counters.
    ///     It handles both the existing counters (accessible by Perf Registry Interface)
    ///     and user defined (extensible) counters.
    ///     This class is a part of a larger framework, that includes the perf dll object and
    ///     perf service.
    /// </summary>
    public sealed class PerformanceCounter : Component, ISupportInitialize
    {
        private PerformanceCounterInstanceLifetime _instanceLifetime = PerformanceCounterInstanceLifetime.Global;

        private bool _initialized;
        private string _helpMsg;
        private int _counterType = -1;

        // Cached old sample
        private CounterSample _oldSample = CounterSample.Empty;

        private object _instanceLockObject;
        private object InstanceLockObject
        {
            get
            {
                if (_instanceLockObject == null)
                {
                    var o = new object();
                    Interlocked.CompareExchange(ref _instanceLockObject, o, null);
                }
                return _instanceLockObject;
            }
        }

        /// <summary>
        ///     Creates the Performance Counter Object
        /// </summary>
        public PerformanceCounter(string categoryName, string counterName, string instanceName)
        {
            CategoryName = categoryName;
            CounterName = counterName;
            InstanceName = instanceName;
            Initialize();
            GC.SuppressFinalize(this);
        }

        internal PerformanceCounter(string categoryName, string counterName, string instanceName, bool skipInit)
        {
            CategoryName = categoryName;
            CounterName = counterName;
            InstanceName = instanceName;
            _initialized = true;
            GC.SuppressFinalize(this);
        }

        /// <summary>
        ///     Creates the Performance Counter Object, assumes that it's a single instance
        /// </summary>
        public PerformanceCounter(string categoryName, string counterName) :
            this(categoryName, counterName, "")
        {
        }

        /// <summary>
        ///     Returns the performance category name for this performance counter
        /// </summary>
        public string CategoryName { get; }

        /// <summary>
        ///     Returns the description message for this performance counter
        /// </summary>
        public string CounterHelp
        {
            get
            {
                Initialize();

                if (_helpMsg == null)
                    _helpMsg = PerformanceCounterLib.GetCounterHelp(CategoryName, CounterName);

                return _helpMsg;
            }
        }

        /// <summary>
        ///     Sets/returns the performance counter name for this performance counter
        /// </summary>
        public string CounterName { get; }

        /// <summary>
        ///     Sets/Returns the counter type for this performance counter
        /// </summary>
        public PerformanceCounterType CounterType
        {
            get
            {
                if (_counterType == -1)
                {
                    // This is the same thing that NextSample does, except that it doesn't try to get the actual counter
                    // value.  If we wanted the counter value, we would need to have an instance name. 
                    Initialize();
                    using (var categorySample = PerformanceCounterLib.GetCategorySample(CategoryName))
                    {
                        var counterSample = categorySample.GetCounterDefinitionSample(CounterName);
                        _counterType = counterSample._counterType;
                    }
                }

                return (PerformanceCounterType)_counterType;
            }
        }

        public PerformanceCounterInstanceLifetime InstanceLifetime
        {
            get { return _instanceLifetime; }
        }

        /// <summary>
        ///     Sets/returns an instance name for this performance counter
        /// </summary>
        public string InstanceName { get; }

        /// <summary>
        ///     Directly accesses the raw value of this counter.  If counter type is of a 32-bit size, it will truncate
        ///     the value given to 32 bits.  This can be significantly more performant for scenarios where
        ///     the raw value is sufficient.   Note that this only works for custom counters created using
        ///     this component,  non-custom counters will throw an exception if this property is accessed.
        /// </summary>
        public long RawValue
        {
            get
            {
                //No need to initialize or Demand, since NextSample already does.
                return NextSample().RawValue;
            }
        }

        /// <summary>
        /// </summary>
        public void BeginInit()
        {
            Close();
        }

        /// <summary>
        ///     Frees all the resources allocated by this counter
        /// </summary>
        public void Close()
        {
            _helpMsg = null;
            _oldSample = CounterSample.Empty;
            _initialized = false;
            _counterType = -1;
        }

        /// <internalonly/>
        /// <summary>
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            // safe to call while finalizing or disposing
            if (disposing)
            {
                //Dispose managed and unmanaged resources
                Close();
            }

            base.Dispose(disposing);
        }

        /// <summary>
        /// </summary>
        public void EndInit()
        {
            Initialize();
        }

        private void Initialize()
        {
            // Keep this method small so the JIT will inline it.
            if (!_initialized && !DesignMode)
            {
                InitializeImpl();
            }
        }

        /// <summary>
        ///     Intializes required resources
        /// </summary>
        private void InitializeImpl()
        {
            var tookLock = false;
            try
            {
                Monitor.Enter(InstanceLockObject, ref tookLock);

                if (!_initialized)
                {
                    if (CategoryName == string.Empty)
                        throw new InvalidOperationException(SR.Format(SR.CategoryNameMissing));
                    if (CounterName == string.Empty)
                        throw new InvalidOperationException(SR.Format(SR.CounterNameMissing));

                    if (!PerformanceCounterLib.CounterExists(CategoryName, CounterName))
                        throw new InvalidOperationException(SR.Format(SR.CounterExists, CategoryName, CounterName));

                    var categoryType = PerformanceCounterLib.GetCategoryType(CategoryName);
                    if (categoryType == PerformanceCounterCategoryType.MultiInstance)
                    {
                        if (string.IsNullOrEmpty(InstanceName))
                            throw new InvalidOperationException(SR.Format(SR.MultiInstanceOnly, CategoryName));
                    }
                    else if (categoryType == PerformanceCounterCategoryType.SingleInstance)
                    {
                        if (!string.IsNullOrEmpty(InstanceName))
                            throw new InvalidOperationException(SR.Format(SR.SingleInstanceOnly, CategoryName));
                    }

                    if (_instanceLifetime != PerformanceCounterInstanceLifetime.Global)
                        throw new InvalidOperationException(SR.Format(SR.InstanceLifetimeProcessonReadOnly));

                    _initialized = true;
                }
            }
            finally
            {
                if (tookLock)
                    Monitor.Exit(InstanceLockObject);
            }

        }

        // Will cause an update, raw value
        /// <summary>
        ///     Obtains a counter sample and returns the raw value for it.
        /// </summary>
        public CounterSample NextSample()
        {
            Initialize();


            using (var categorySample = PerformanceCounterLib.GetCategorySample(CategoryName))
            {
                var counterSample = categorySample.GetCounterDefinitionSample(CounterName);
                _counterType = counterSample._counterType;
                if (!categorySample._isMultiInstance)
                {
                    if (!string.IsNullOrEmpty(InstanceName))
                        throw new InvalidOperationException(SR.Format(SR.InstanceNameProhibited, InstanceName));

                    return counterSample.GetSingleValue();
                }

                if (string.IsNullOrEmpty(InstanceName))
                    throw new InvalidOperationException(SR.Format(SR.InstanceNameRequired));

                return counterSample.GetInstanceValue(InstanceName);
            }
        }

        /// <summary>
        ///     Obtains a counter sample and returns the calculated value for it.
        ///     NOTE: For counters whose calculated value depend upon 2 counter reads,
        ///           the very first read will return 0.0.
        /// </summary>
        public float NextValue()
        {
            //No need to initialize or Demand, since NextSample already does.
            var newSample = NextSample();
            var retVal = 0.0f;

            retVal = CounterSample.Calculate(_oldSample, newSample);
            _oldSample = newSample;

            return retVal;
        }

    }
}
