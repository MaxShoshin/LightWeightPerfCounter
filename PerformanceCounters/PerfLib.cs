using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using JetBrains.Annotations;
using LightWeight.PerformanceCounters.Resources;
using Microsoft.Win32;

#if !netcoreapp
using MemoryMarshal = LightWeight.PerformanceCounters.Extensions;
#endif

namespace LightWeight.PerformanceCounters
{
    internal sealed class PerfLib
    {
        private const string PerfShimName = "netfxperf.dll";
        private const string PerfShimFullNameSuffix = @"\netfxperf.dll";
        private const string ServicePath = "SYSTEM\\CurrentControlSet\\Services";

        private static readonly Dictionary<CultureInfo, PerfLib> Instances = new Dictionary<CultureInfo, PerfLib>();
        private static readonly object SyncRoot = new object();

        private readonly Dictionary<string, PerformanceCounterCategoryType> _customCategoryTable = new Dictionary<string, PerformanceCounterCategoryType>();
        private readonly Dictionary<string, CategoryEntry> _categoryTable;
        private readonly Dictionary<int, string> _nameTable;
        private readonly Dictionary<int, string>  _helpTable;

        private readonly PerformanceMonitor _monitor;

        private PerfLib(
            [NotNull] PerformanceMonitor monitor,
            [NotNull] Dictionary<int, string> nameTable,
            [NotNull] Dictionary<int, string> helpTable,
            [NotNull] Dictionary<string, CategoryEntry> categoryTable)
        {
            _monitor = monitor;
            _nameTable = nameTable;
            _helpTable = helpTable;
            _categoryTable = categoryTable;
        }

        public Dictionary<int, string> NameTable => _nameTable;

        public static PerfLib GetOrCreate([NotNull] CultureInfo culture)
        {
            if (culture == null) throw new ArgumentNullException(nameof(culture));

            lock (SyncRoot)
            {
                if (!Instances.TryGetValue(culture, out var result))
                {
                    var lcid = culture.LCID.ToString("X3", CultureInfo.InvariantCulture);

                    var monitor = PerformanceMonitor.Create();
                    var nameTable = GetStringTable(lcid, false);
                    var helpTable = GetStringTable(lcid, true);
                    var categories = ReadCategories(monitor, nameTable);

                    result = new PerfLib(monitor, nameTable, helpTable, categories);

                    Instances.Add(culture, result);
                }

                return result;
            }
        }

        public bool CategoryExists([NotNull] string category)
        {
            return _categoryTable.ContainsKey(category);
        }

        [NotNull]
        public string[] GetCategories()
        {
            return _categoryTable.Keys.ToArray();
        }

        // Ensures that the customCategoryTable is initialized and decides whether the category passed in
        //  1) is a custom category
        //  2) is a multi instance custom category
        // The return value is whether the category is a custom category or not.
        public PerformanceCounterCategoryType GetCategoryType([NotNull] string category)
        {
            RegistryKey key = null;

            if (_customCategoryTable.TryGetValue(category, out var categoryType))
            {
                return categoryType;
            }

            try
            {
                key = Registry.LocalMachine.OpenSubKey(ServicePath + "\\" + category + "\\Performance");

                var systemDllName = key?.GetValue("Library", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;

                if (string.IsNullOrEmpty(systemDllName))
                {
                    return PerformanceCounterCategoryType.Unknown;
                }

                if (!string.Equals(systemDllName, PerfShimName, StringComparison.OrdinalIgnoreCase) && !systemDllName.EndsWith(PerfShimFullNameSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    return PerformanceCounterCategoryType.Unknown;
                }

                var isMultiInstanceObject = key.GetValue("IsMultiInstance");
                if (isMultiInstanceObject != null)
                {
                    categoryType = (PerformanceCounterCategoryType)isMultiInstanceObject;
                    if (categoryType < PerformanceCounterCategoryType.Unknown || categoryType > PerformanceCounterCategoryType.MultiInstance)
                    {
                        categoryType = PerformanceCounterCategoryType.Unknown;
                    }
                }
                else
                {
                    categoryType = PerformanceCounterCategoryType.Unknown;
                }

                var objectId = key.GetValue("First Counter");
                if (objectId is int)
                {
                    _customCategoryTable[category] = categoryType;

                    return categoryType;
                }
            }
            finally
            {
                key?.Close();
            }

            return PerformanceCounterCategoryType.Unknown;
        }

        [CanBeNull]
        public CategorySample GetCategorySample([NotNull] string category)
        {
            if (!_categoryTable.TryGetValue(category, out var entry))
            {
                return null;
            }

            var dataRef = GetPerformanceData(entry.NameIndexStr);
            if (dataRef == null)
                throw new InvalidOperationException(SR.Format(SR.CantReadCategory, category));

            var sample = new CategorySample(dataRef, entry, this);
            return sample;
        }

        [NotNull]
        internal byte[] GetPerformanceData([NotNull] string item)
        {
            return _monitor.GetData(item);
        }

        [NotNull]
        public IReadOnlyList<string> GetCounters([NotNull] string category)
        {
            if (!_categoryTable.TryGetValue(category, out var entry))
            {
                return Array.Empty<string>();
            }

            var count = entry.CounterIndexes.Length;
            var counters = new List<string>(count);
            for (int index = 0; index < count; ++index)
            {
                var counterIndex = entry.CounterIndexes[index];

                if (_nameTable.TryGetValue(counterIndex, out var name) && !string.IsNullOrEmpty(name))
                {
                    counters.Add(name);
                }
            }

            return counters;
        }

        [NotNull]
        public string GetCategoryHelp([NotNull] string category)
        {
            if (!_categoryTable.TryGetValue(category, out var entry))
            {
                return string.Empty;
            }

            _helpTable.TryGetValue(entry.HelpIndex, out var help);
            return help ?? string.Empty;
        }

        public bool CounterExists([NotNull] string category, [NotNull] string counter)
        {
            if (category == null) throw new ArgumentNullException(nameof(category));
            if (counter == null) throw new ArgumentNullException(nameof(counter));

            if (!_categoryTable.TryGetValue(category, out var entry))
            {
                return false;
            }

            for (var index = 0; index < entry.CounterIndexes.Length; ++index)
            {
                var counterIndex = entry.CounterIndexes[index];

                if (!_nameTable.TryGetValue(counterIndex, out var counterName))
                {
                    counterName = string.Empty;
                }

                if (string.Equals(counterName, counter, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        [NotNull]
        public string GetCounterHelp([NotNull] string category, [NotNull] string counter)
        {
            if (!_categoryTable.TryGetValue(category, out var entry))
            {
                return string.Empty;
            }

            var helpIndex = -1;
            for (var index = 0; index < entry.CounterIndexes.Length; ++index)
            {
                var counterIndex = entry.CounterIndexes[index];

                _nameTable.TryGetValue(counterIndex, out var counterName);
                counterName = counterName ?? string.Empty;

                if (string.Equals(counterName, counter, StringComparison.OrdinalIgnoreCase))
                {
                    helpIndex = entry.HelpIndexes[index];
                    break;
                }
            }

            if (helpIndex == -1)
                throw new InvalidOperationException(SR.Format(SR.MissingCounter, counter));

            if (!_helpTable.TryGetValue(helpIndex, out var help))
            {
                return string.Empty;
            }

            return help;
        }

        [NotNull]
        private static Dictionary<string, CategoryEntry> ReadCategories([NotNull] PerformanceMonitor monitor, [NotNull] Dictionary<int, string> nameTable)
        {
            ReadOnlySpan<byte> data = monitor.GetData("Global");

            ref readonly var dataBlock = ref MemoryMarshal.AsRef<Interop.Interop.Advapi32.PERF_DATA_BLOCK>(data);
            var pos = dataBlock.HeaderLength;

            var numPerfObjects = dataBlock.NumObjectTypes;

            // on some machines MSMQ claims to have 4 categories, even though it only has 2.
            // This causes us to walk past the end of our data, potentially crashing or reading
            // data we shouldn't.  We use dataBlock.TotalByteLength to make sure we don't go past the end
            // of the perf data.
            var tempCategoryTable = new Dictionary<string, CategoryEntry>(numPerfObjects, StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < numPerfObjects && pos < dataBlock.TotalByteLength; index++)
            {
                ref readonly var perfObject = ref MemoryMarshal.AsRef<Interop.Interop.Advapi32.PERF_OBJECT_TYPE>(data.Slice(pos));

                var newCategoryEntry = new CategoryEntry(in perfObject);
                var nextPos = pos + perfObject.TotalByteLength;
                pos += perfObject.HeaderLength;

                var index3 = 0;
                var previousCounterIndex = -1;
                //Need to filter out counters that are repeated, some providers might
                //return several adyacent copies of the same counter.
                for (var index2 = 0; index2 < newCategoryEntry.CounterIndexes.Length; ++index2)
                {
                    ref readonly var perfCounter = ref MemoryMarshal.AsRef<Interop.Interop.Advapi32.PERF_COUNTER_DEFINITION>(data.Slice(pos));
                    if (perfCounter.CounterNameTitleIndex != previousCounterIndex)
                    {
                        newCategoryEntry.CounterIndexes[index3] = perfCounter.CounterNameTitleIndex;
                        newCategoryEntry.HelpIndexes[index3] = perfCounter.CounterHelpTitleIndex;
                        previousCounterIndex = perfCounter.CounterNameTitleIndex;
                        ++index3;
                    }
                    pos += perfCounter.ByteLength;
                }

                //Lets adjust the entry counter arrays in case there were repeated copies
                if (index3 < newCategoryEntry.CounterIndexes.Length)
                {
                    var adjustedCounterIndexes = new int[index3];
                    var adjustedHelpIndexes = new int[index3];
                    Array.Copy(newCategoryEntry.CounterIndexes, adjustedCounterIndexes, index3);
                    Array.Copy(newCategoryEntry.HelpIndexes, adjustedHelpIndexes, index3);
                    newCategoryEntry.CounterIndexes = adjustedCounterIndexes;
                    newCategoryEntry.HelpIndexes = adjustedHelpIndexes;
                }

                if (nameTable.TryGetValue(newCategoryEntry.NameIndex, out var categoryName))
                {
                    tempCategoryTable[categoryName] = newCategoryEntry;
                }

                pos = nextPos;
            }

            return tempCategoryTable;
        }

        [NotNull]
        private static Dictionary<int, string> GetStringTable([NotNull] string perfLcid, bool isHelp)
        {
            var keyValue = isHelp ? "Explain " : "Counter ";
            keyValue += perfLcid;

            Dictionary<int, string> stringTable;
            RegistryKey libraryKey;

            libraryKey = Registry.PerformanceData;

            try
            {
                string[] names = null;
                var waitRetries = 14;   //((2^13)-1)*10ms == approximately 1.4mins
                var waitSleep = 0;

                // In some stress situations, querying counter values from
                // HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Perflib\009
                // often returns null/empty data back. We should build fault-tolerance logic to
                // make it more reliable because getting null back once doesn't necessarily mean
                // that the data is corrupted, most of the time we would get the data just fine
                // in subsequent tries.
                while (waitRetries > 0)
                {
                    try
                    {
                        names = (string[])libraryKey.GetValue(keyValue);

                        if ((names == null) || (names.Length == 0))
                        {
                            --waitRetries;
                            if (waitSleep == 0)
                                waitSleep = 10;
                            else
                            {
                                Thread.Sleep(waitSleep);
                                waitSleep *= 2;
                            }
                        }
                        else
                            break;
                    }
                    catch (IOException)
                    {
                        // RegistryKey throws if it can't find the value.  We want to return an empty table
                        // and throw a different exception higher up the stack.
                        names = null;
                        break;
                    }
                    catch (InvalidCastException)
                    {
                        // Unable to cast object of type 'System.Byte[]' to type 'System.String[]'.
                        // this happens when the registry data store is corrupt and the type is not even REG_MULTI_SZ
                        names = null;
                        break;
                    }
                }

                if (names == null)
                    stringTable = new Dictionary<int, string>();
                else
                {
                    stringTable = new Dictionary<int, string>(names.Length / 2);

                    for (var index = 0; index < (names.Length / 2); ++index)
                    {
                        var nameString = names[(index * 2) + 1];
                        if (nameString == null)
                            nameString = string.Empty;

                        int key;
                        if (!int.TryParse(names[index * 2], NumberStyles.Integer, CultureInfo.InvariantCulture, out key))
                        {
                            if (isHelp)
                            {
                                // Category Help Table
                                throw new InvalidOperationException(SR.Format(SR.CategoryHelpCorrupt, names[index * 2]));
                            }
                            else
                            {
                                // Counter Name Table
                                throw new InvalidOperationException(SR.Format(SR.CounterNameCorrupt, names[index * 2]));
                            }
                        }

                        stringTable[key] = nameString;
                    }
                }
            }
            finally
            {
                libraryKey.Close();
            }

            return stringTable;
        }
    }
}