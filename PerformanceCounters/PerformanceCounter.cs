// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using JetBrains.Annotations;
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
    public sealed class PerformanceCounter
    {
        private bool _initialized;
        private string _helpMsg;
        private int _counterType = -1;

        private CounterSample _oldSample = CounterSample.Empty;

        private readonly object _instanceLockObject = new object();
        private readonly PerfLib _perfLib = PerfLib.GetOrCreate(new CultureInfo(Constants.EnglishLCID));

        /// <summary>
        ///     Creates the Performance Counter Object
        /// </summary>
        private PerformanceCounter(string categoryName, string counterName, string instanceName)
        {
            CategoryName = categoryName;
            CounterName = counterName;
            InstanceName = instanceName;
        }

        [NotNull]
        public static PerformanceCounter Create([NotNull] string categoryName, [NotNull] string counterName, [NotNull] string instanceName = "")
        {
            if (categoryName == null) throw new ArgumentNullException(nameof(categoryName));
            if (counterName == null) throw new ArgumentNullException(nameof(counterName));
            if (instanceName == null) throw new ArgumentNullException(nameof(instanceName));

            var perfCounter = new PerformanceCounter(categoryName, counterName, instanceName);

            perfCounter.Initialize();

            return perfCounter;
        }

        /// <summary>
        ///    Returns the performance category name for this performance counter
        /// </summary>
        public string CategoryName { get; }

        /// <summary>
        ///    Returns the description message for this performance counter
        /// </summary>
        public string CounterHelp => _helpMsg;

        /// <summary>
        ///    Returns the performance counter name for this performance counter
        /// </summary>
        public string CounterName { get; }

        /// <summary>
        ///    Returns the counter type for this performance counter
        /// </summary>
        public PerformanceCounterType CounterType => (PerformanceCounterType)_counterType;

        /// <summary>
        ///     Returns an instance name for this performance counter
        /// </summary>
        public string InstanceName { get; }

        /// <summary>
        ///     Directly accesses the raw value of this counter.  If counter type is of a 32-bit size, it will truncate
        ///     the value given to 32 bits.  This can be significantly more performant for scenarios where
        ///     the raw value is sufficient.   Note that this only works for custom counters created using
        ///     this component,  non-custom counters will throw an exception if this property is accessed.
        /// </summary>
        public long RawValue => NextSample().RawValue;


        /// <summary>
        ///     Intializes required resources
        /// </summary>
        private void Initialize()
        {
            if (_initialized)
            {
                return;
            }

            lock (_instanceLockObject)
            {
                if (_initialized)
                {
                    return;
                }

                if (CategoryName == string.Empty)
                    throw new InvalidOperationException(SR.Format(SR.CategoryNameMissing));
                if (CounterName == string.Empty)
                    throw new InvalidOperationException(SR.Format(SR.CounterNameMissing));

                if (!_perfLib.CategoryExists(CategoryName))
                    throw new InvalidOperationException(SR.Format(SR.MissingCategory));

                if (!_perfLib.CounterExists(CategoryName, CounterName))
                    throw new InvalidOperationException(SR.Format(SR.CounterExists, CategoryName, CounterName));

                var categoryType = _perfLib.GetCategoryType(CategoryName);
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

                _helpMsg = _perfLib.GetCounterHelp(CategoryName, CounterName);

                // To read _counterType
                NextSample();

                _initialized = true;
            }
        }

        // Will cause an update, raw value
        /// <summary>
        ///     Obtains a counter sample and returns the raw value for it.
        /// </summary>
        public CounterSample NextSample()
        {
            var data = _perfLib.GetPerformanceData(CategoryName);

            ArrayPool<byte>.Shared.Return(data);
            using (var categorySample = _perfLib.GetCategorySample(CategoryName))
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
            var newSample = NextSample();
            var retVal = 0.0f;

            retVal = CounterSample.Calculate(_oldSample, newSample);
            _oldSample = newSample;

            return retVal;
        }

    }
}
