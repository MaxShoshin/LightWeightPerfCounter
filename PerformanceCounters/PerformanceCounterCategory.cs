// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Globalization;
using JetBrains.Annotations;
using LightWeight.PerformanceCounters.Resources;

namespace LightWeight.PerformanceCounters
{
    /// <summary>
    ///     A Performance counter category object.
    /// </summary>
    public sealed class PerformanceCounterCategory
    {
        private static PerfLib _perfLib;
        private static object SyncRoot = new object();

        private PerformanceCounterCategory([NotNull] PerfLib perfLib, [NotNull] string categoryName, [NotNull] string categoryHelp, PerformanceCounterCategoryType categoryType)
        {
            if (categoryName == null)
                throw new ArgumentNullException(nameof(categoryName));

            if (categoryName.Length == 0)
                throw new ArgumentException(SR.Format(SR.InvalidParameter, nameof(categoryName), categoryName), nameof(categoryName));

            CategoryName = categoryName;
            CategoryHelp = categoryHelp;
            CategoryType = categoryType;
        }

        private static PerfLib PerfLibInstance
        {
            get
            {
                lock (SyncRoot)
                {
                    if (_perfLib == null)
                    {
                        _perfLib = PerfLib.GetOrCreate(new CultureInfo(Constants.EnglishLCID));
                    }
                }

                return _perfLib;
            }
        }

        [NotNull]
        public static PerformanceCounterCategory Create([NotNull] string categoryName)
        {
            if (categoryName == null) throw new ArgumentNullException(nameof(categoryName));

            var help = PerfLibInstance.GetCategoryHelp(categoryName);
            PerformanceCounterCategoryType type;
            using (var categorySample = PerfLibInstance.GetCategorySample(categoryName))
            {

                // If we get MultiInstance, we can be confident it is correct.  If it is single instance, though
                // we need to check if is a custom category and if the IsMultiInstance value is set in the registry.
                // If not we return Unknown
                if (categorySample._isMultiInstance)
                {
                    type = PerformanceCounterCategoryType.MultiInstance;
                }
                else
                {
                    type = PerformanceCounterCategoryType.SingleInstance;
                }
            }

            return new PerformanceCounterCategory(PerfLibInstance, categoryName, help, type);
        }

        /// <summary>
        ///     Gets/sets the Category name
        /// </summary>
        public string CategoryName { get; }

        /// <summary>
        ///     Gets/sets the Category help
        /// </summary>
        public string CategoryHelp { get; }

        public PerformanceCounterCategoryType CategoryType { get; }


        /// <summary>
        ///     Returns true if the counter is registered for this category
        /// </summary>
        public bool CounterExists([NotNull] string counterName)
        {
            if (counterName == null) throw new ArgumentNullException(nameof(counterName));

            return _perfLib.CounterExists(CategoryName, counterName);
        }


        /// <summary>
        ///     Returns true if the counter is registered for this category on a particular machine.
        /// </summary>
        public static bool CounterExists(string counterName, string categoryName)
        {
            if (counterName == null)
                throw new ArgumentNullException(nameof(counterName));

            if (categoryName == null)
                throw new ArgumentNullException(nameof(categoryName));

            if (categoryName.Length == 0)
                throw new ArgumentException(SR.Format(SR.InvalidParameter, nameof(categoryName), categoryName), nameof(categoryName));

            return PerfLibInstance.CounterExists(categoryName, counterName);
        }

        /// <summary>
        ///     Returns true if the category is registered in the machine.
        /// </summary>
        public static bool Exists(string categoryName)
        {
            if (categoryName == null)
                throw new ArgumentNullException(nameof(categoryName));

            if (categoryName.Length == 0)
                throw new ArgumentException(SR.Format(SR.InvalidParameter, nameof(categoryName), categoryName), nameof(categoryName));

            return PerfLibInstance.CategoryExists(categoryName);
        }

        /// <summary>
        ///     Returns the instance names for a given category
        /// </summary>
        /// <internalonly/>
        internal static string[] GetCounterInstances(string categoryName)
        {
            using (var categorySample = PerfLibInstance.GetCategorySample(categoryName))
            {
                if (categorySample._instanceNameTable.Count == 0)
                {
                    return Array.Empty<string>();
                }

                var instanceNames = new string[categorySample._instanceNameTable.Count];
                categorySample._instanceNameTable.Keys.CopyTo(instanceNames, 0);

                if (instanceNames.Length == 1 && instanceNames[0] == Constants.SingleInstanceName)
                {
                    return Array.Empty<string>();
                }

                return instanceNames;
            }
        }

        /// <summary>
        ///     Returns an array of counters in this category.  The counter must have only one instance.
        /// </summary>
        public IReadOnlyList<PerformanceCounter> GetCounters()
        {
            if (GetInstanceNames().Length != 0) throw new ArgumentException(SR.Format(SR.InstanceNameRequired));

            return GetCounters("");
        }

        /// <summary>
        ///     Returns an array of counters in this category for the given instance.
        /// </summary>
        public IReadOnlyList<PerformanceCounter> GetCounters([NotNull] string instanceName)
        {
            if (instanceName == null) throw new ArgumentNullException(nameof(instanceName));
            if (instanceName.Length != 0 && !InstanceExists(instanceName))
                throw new InvalidOperationException(SR.Format(SR.MissingInstance, instanceName, CategoryName));

            var counterNames = PerfLibInstance.GetCounters(CategoryName);
            var counters = new PerformanceCounter[counterNames.Count];
            for (var index = 0; index < counters.Length; index++)
            {
                counters[index] = PerformanceCounter.Create(CategoryName, counterNames[index], instanceName);
            }

            return counters;
        }


        /// <summary>
        ///     Returns an array of performance counter categories for a particular machine.
        /// </summary>
        public static PerformanceCounterCategory[] GetCategories()
        {
            var categoryNames = PerfLibInstance.GetCategories();
            var categories = new PerformanceCounterCategory[categoryNames.Length];
            for (var index = 0; index < categories.Length; index++)
            {
                categories[index] = Create(categoryNames[index]);
            }

            return categories;
        }

        /// <summary>
        ///     Returns an array of instances for this category
        /// </summary>
        public string[] GetInstanceNames()
        {
            return GetCounterInstances(CategoryName);
        }

        /// <summary>
        ///     Returns true if the instance already exists for this category.
        /// </summary>
        public bool InstanceExists([NotNull] string instanceName)
        {
            if (instanceName == null) throw new ArgumentNullException(nameof(instanceName));

            using (var categorySample = PerfLibInstance.GetCategorySample(CategoryName))
            {
                return categorySample._instanceNameTable.ContainsKey(instanceName);
            }
        }


        /// <summary>
        ///     Returns true if the instance already exists for this category and machine specified.
        /// </summary>
        public static bool InstanceExists([NotNull] string instanceName, [NotNull] string categoryName)
        {
            if (instanceName == null) throw new ArgumentNullException(nameof(instanceName));
            if (categoryName == null) throw new ArgumentNullException(nameof(categoryName));
            if (categoryName.Length == 0) throw new ArgumentException(SR.Format(SR.InvalidParameter, nameof(categoryName), categoryName), nameof(categoryName));

            var category = PerformanceCounterCategory.Create(categoryName);
            return category.InstanceExists(instanceName);
        }

        /// <summary>
        ///     Reads all the counter and instance data of this performance category.  Note that reading the entire category
        ///     at once can be as efficient as reading a single counter because of the way the system provides the data.
        /// </summary>
        public InstanceDataCollectionCollection ReadCategory()
        {
            if (CategoryName == null)
                throw new InvalidOperationException(SR.Format(SR.CategoryNameNotSet));

            using (var categorySample = PerfLibInstance.GetCategorySample(CategoryName))
            {
                return categorySample.ReadCategory();
            }
        }
    }

    [Flags]
    internal enum PerformanceCounterCategoryOptions
    {
        EnableReuse = 0x1,
        UseUniqueSharedMemory = 0x2
    }
}
