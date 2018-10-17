// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using LightWeight.PerformanceCounters.Resources;

namespace LightWeight.PerformanceCounters
{
    /// <summary>
    ///     A Performance counter category object.
    /// </summary>
    public sealed class PerformanceCounterCategory
    {
        private string _categoryName;
        private string _categoryHelp;

        /// <summary>
        ///     Creates a PerformanceCounterCategory object for given category.
        ///     Uses the given machine name.
        /// </summary>
        internal PerformanceCounterCategory(string categoryName)
        {
            if (categoryName == null)
                throw new ArgumentNullException(nameof(categoryName));

            if (categoryName.Length == 0)
                throw new ArgumentException(SR.Format(SR.InvalidParameter, nameof(categoryName), categoryName), nameof(categoryName));

            _categoryName = categoryName;
        }

        /// <summary>
        ///     Gets/sets the Category name
        /// </summary>
        public string CategoryName
        {
            get
            {
                return _categoryName;
            }

        }

        /// <summary>
        ///     Gets/sets the Category help
        /// </summary>
        public string CategoryHelp
        {
            get
            {
                if (_categoryName == null)
                    throw new InvalidOperationException(SR.Format(SR.CategoryNameNotSet));

                if (_categoryHelp == null)
                    _categoryHelp = PerformanceCounterLib.GetCategoryHelp(_categoryName);

                return _categoryHelp;
            }
        }

        public PerformanceCounterCategoryType CategoryType
        {
            get
            {
                using (var categorySample = PerformanceCounterLib.GetCategorySample(_categoryName))
                {

                    // If we get MultiInstance, we can be confident it is correct.  If it is single instance, though
                    // we need to check if is a custom category and if the IsMultiInstance value is set in the registry.
                    // If not we return Unknown
                    if (categorySample._isMultiInstance)
                        return PerformanceCounterCategoryType.MultiInstance;
                    if (PerformanceCounterLib.IsCustomCategory(_categoryName))
                        return PerformanceCounterLib.GetCategoryType(_categoryName);
                    return PerformanceCounterCategoryType.SingleInstance;
                }
            }
        }


        /// <summary>
        ///     Returns true if the counter is registered for this category
        /// </summary>
        public bool CounterExists(string counterName)
        {
            if (counterName == null)
                throw new ArgumentNullException(nameof(counterName));

            if (_categoryName == null)
                throw new InvalidOperationException(SR.Format(SR.CategoryNameNotSet));

            return PerformanceCounterLib.CounterExists(_categoryName, counterName);
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

            return PerformanceCounterLib.CounterExists(categoryName, counterName);
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

            if (PerformanceCounterLib.IsCustomCategory(categoryName))
                return true;

            return PerformanceCounterLib.CategoryExists(categoryName);
        }

        /// <summary>
        ///     Returns the instance names for a given category
        /// </summary>
        /// <internalonly/>
        internal static string[] GetCounterInstances(string categoryName)
        {
            using (var categorySample = PerformanceCounterLib.GetCategorySample(categoryName))
            {
                if (categorySample._instanceNameTable.Count == 0)
                    return Array.Empty<string>();

                var instanceNames = new string[categorySample._instanceNameTable.Count];
                categorySample._instanceNameTable.Keys.CopyTo(instanceNames, 0);
                if (instanceNames.Length == 1 && instanceNames[0] == PerformanceCounterLib.SingleInstanceName)
                    return Array.Empty<string>();

                return instanceNames;
            }
        }

        /// <summary>
        ///     Returns an array of counters in this category.  The counter must have only one instance.
        /// </summary>
        public PerformanceCounter[] GetCounters()
        {
            if (GetInstanceNames().Length != 0)
                throw new ArgumentException(SR.Format(SR.InstanceNameRequired));
            return GetCounters("");
        }

        /// <summary>
        ///     Returns an array of counters in this category for the given instance.
        /// </summary>
        public PerformanceCounter[] GetCounters(string instanceName)
        {
            if (instanceName == null)
                throw new ArgumentNullException(nameof(instanceName));

            if (_categoryName == null)
                throw new InvalidOperationException(SR.Format(SR.CategoryNameNotSet));

            if (instanceName.Length != 0 && !InstanceExists(instanceName))
                throw new InvalidOperationException(SR.Format(SR.MissingInstance, instanceName, _categoryName));

            var counterNames = PerformanceCounterLib.GetCounters(_categoryName);
            var counters = new PerformanceCounter[counterNames.Length];
            for (var index = 0; index < counters.Length; index++)
                counters[index] = new PerformanceCounter(_categoryName, counterNames[index], instanceName, true);

            return counters;
        }


        /// <summary>
        ///     Returns an array of performance counter categories for a particular machine.
        /// </summary>
        public static PerformanceCounterCategory[] GetCategories()
        {
            var categoryNames = PerformanceCounterLib.GetCategories();
            var categories = new PerformanceCounterCategory[categoryNames.Length];
            for (var index = 0; index < categories.Length; index++)
                categories[index] = new PerformanceCounterCategory(categoryNames[index]);

            return categories;
        }

        /// <summary>
        ///     Returns an array of instances for this category
        /// </summary>
        public string[] GetInstanceNames()
        {
            if (_categoryName == null)
                throw new InvalidOperationException(SR.Format(SR.CategoryNameNotSet));

            return GetCounterInstances(_categoryName);
        }

        /// <summary>
        ///     Returns true if the instance already exists for this category.
        /// </summary>
        public bool InstanceExists(string instanceName)
        {
            if (instanceName == null)
                throw new ArgumentNullException(nameof(instanceName));

            if (_categoryName == null)
                throw new InvalidOperationException(SR.Format(SR.CategoryNameNotSet));

            using (var categorySample = PerformanceCounterLib.GetCategorySample(_categoryName))
            {
                return categorySample._instanceNameTable.ContainsKey(instanceName);
            }
        }


        /// <summary>
        ///     Returns true if the instance already exists for this category and machine specified.
        /// </summary>
        public static bool InstanceExists(string instanceName, string categoryName)
        {
            if (instanceName == null)
                throw new ArgumentNullException(nameof(instanceName));

            if (categoryName == null)
                throw new ArgumentNullException(nameof(categoryName));

            if (categoryName.Length == 0)
                throw new ArgumentException(SR.Format(SR.InvalidParameter, nameof(categoryName), categoryName), nameof(categoryName));

            var category = new PerformanceCounterCategory(categoryName);
            return category.InstanceExists(instanceName);
        }

        /// <summary>
        ///     Reads all the counter and instance data of this performance category.  Note that reading the entire category
        ///     at once can be as efficient as reading a single counter because of the way the system provides the data.
        /// </summary>
        public InstanceDataCollectionCollection ReadCategory()
        {
            if (_categoryName == null)
                throw new InvalidOperationException(SR.Format(SR.CategoryNameNotSet));

            using (var categorySample = PerformanceCounterLib.GetCategorySample(_categoryName))
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
