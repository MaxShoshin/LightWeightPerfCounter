// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using LightWeight.PerformanceCounters.Resources;
using Microsoft.Win32;
#if !netcoreapp
using MemoryMarshal = LightWeight.PerformanceCounters.PerformanceCounterLib;
#endif

namespace LightWeight.PerformanceCounters
{
    internal class PerformanceCounterLib
    {
        internal const string PerfShimName = "netfxperf.dll";
        private const string PerfShimFullNameSuffix = @"\netfxperf.dll";
        internal const string SingleInstanceName = "systemdiagnosticsperfcounterlibsingleinstance";

        internal const string ServicePath = "SYSTEM\\CurrentControlSet\\Services";

        private const int EnglishLCID = 0x009;

        private PerformanceMonitor _performanceMonitor;
        private string _perfLcid;


        private static readonly Dictionary<string, PerformanceCounterLib> _libraryTable = new Dictionary<string, PerformanceCounterLib>();
        private Dictionary<string, PerformanceCounterCategoryType> _customCategoryTable;
        private Dictionary<string, CategoryEntry> _categoryTable;
        private Dictionary<int, string> _nameTable;
        private Dictionary<int, string>  _helpTable;
        private readonly object _categoryTableLock = new object();
        private readonly object _nameTableLock = new object();
        private readonly object _helpTableLock = new object();

        private static object s_internalSyncObject;
        private static object InternalSyncObject
        {
            get
            {
                if (s_internalSyncObject == null)
                {
                    var o = new object();
                    Interlocked.CompareExchange(ref s_internalSyncObject, o, null);
                }
                return s_internalSyncObject;
            }
        }

        internal PerformanceCounterLib(string lcid)
        {
            _perfLcid = lcid;
        }

#if !netcoreapp
        internal static T Read<T>(ReadOnlySpan<byte> span) where T : struct
            => System.Runtime.InteropServices.MemoryMarshal.Read<T>(span);

        internal static ref readonly T AsRef<T>(ReadOnlySpan<byte> span) where T : struct
            => ref System.Runtime.InteropServices.MemoryMarshal.Cast<byte, T>(span)[0];
#endif

        private Dictionary<string, CategoryEntry> CategoryTable
        {
            get
            {
                if (_categoryTable == null)
                {
                    lock (_categoryTableLock)
                    {
                        if (_categoryTable == null)
                        {
                            ReadOnlySpan<byte> data = GetPerformanceData("Global");

                            ref readonly var dataBlock = ref AsRef<Interop.Interop.Advapi32.PERF_DATA_BLOCK>(data);
                            var pos = dataBlock.HeaderLength;

                            var numPerfObjects = dataBlock.NumObjectTypes;

                            // on some machines MSMQ claims to have 4 categories, even though it only has 2.
                            // This causes us to walk past the end of our data, potentially crashing or reading
                            // data we shouldn't.  We use dataBlock.TotalByteLength to make sure we don't go past the end
                            // of the perf data.
                            var tempCategoryTable = new Dictionary<string, CategoryEntry>(numPerfObjects, StringComparer.OrdinalIgnoreCase);
                            for (var index = 0; index < numPerfObjects && pos < dataBlock.TotalByteLength; index++)
                            {
                                ref readonly var perfObject = ref AsRef<Interop.Interop.Advapi32.PERF_OBJECT_TYPE>(data.Slice(pos));

                                var newCategoryEntry = new CategoryEntry(in perfObject);
                                var nextPos = pos + perfObject.TotalByteLength;
                                pos += perfObject.HeaderLength;

                                var index3 = 0;
                                var previousCounterIndex = -1;
                                //Need to filter out counters that are repeated, some providers might
                                //return several adyacent copies of the same counter.
                                for (var index2 = 0; index2 < newCategoryEntry.CounterIndexes.Length; ++index2)
                                {
                                    ref readonly var perfCounter = ref AsRef<Interop.Interop.Advapi32.PERF_COUNTER_DEFINITION>(data.Slice(pos));
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

                                if (NameTable.TryGetValue(newCategoryEntry.NameIndex, out var categoryName))
                                {
                                    tempCategoryTable[categoryName] = newCategoryEntry;
                                }

                                pos = nextPos;
                            }

                            _categoryTable = tempCategoryTable;
                        }
                    }
                }

                return _categoryTable;
            }
        }

        internal Dictionary<int, string> HelpTable
        {
            get
            {
                if (_helpTable == null)
                {
                    lock (_helpTableLock)
                    {
                        if (_helpTable == null)
                            _helpTable = GetStringTable(true);
                    }
                }

                return _helpTable;
            }
        }

        internal Dictionary<int, string> NameTable
        {
            get
            {
                if (_nameTable == null)
                {
                    lock (_nameTableLock)
                    {
                        if (_nameTable == null)
                            _nameTable = GetStringTable(false);
                    }
                }

                return _nameTable;
            }
        }

        internal static bool CategoryExists(string category)
        {
            var library = GetPerformanceCounterLib(new CultureInfo(EnglishLCID));
            if (library.CategoryExists2(category))
                return true;

            if (CultureInfo.CurrentCulture.Parent.LCID != EnglishLCID)
            {
                var culture = CultureInfo.CurrentCulture;
                while (culture != CultureInfo.InvariantCulture)
                {
                    library = GetPerformanceCounterLib(culture);
                    if (library.CategoryExists2(category))
                        return true;
                    culture = culture.Parent;
                }
            }

            return false;
        }

        internal bool CategoryExists2(string category)
        {
            return CategoryTable.ContainsKey(category);
        }

        internal void CloseTables()
        {
            _nameTable = null;
            _helpTable = null;
            _categoryTable = null;
            _customCategoryTable = null;
        }

        internal void Close()
        {
            if (_performanceMonitor != null)
            {
                _performanceMonitor = null;
            }

            CloseTables();
        }

        internal static bool CounterExists(string category, string counter)
        {
            var library = GetPerformanceCounterLib(new CultureInfo(EnglishLCID));
            var categoryExists = false;
            var counterExists = library.CounterExists(category, counter, ref categoryExists);

            if (!categoryExists && CultureInfo.CurrentCulture.Parent.LCID != EnglishLCID)
            {
                var culture = CultureInfo.CurrentCulture;
                while (culture != CultureInfo.InvariantCulture)
                {
                    library = GetPerformanceCounterLib(culture);
                    counterExists = library.CounterExists(category, counter, ref categoryExists);
                    if (counterExists)
                        break;

                    culture = culture.Parent;
                }
            }

            if (!categoryExists)
            {
                // Consider adding diagnostic logic here, may be we can dump the nameTable...
                throw new InvalidOperationException(SR.Format(SR.MissingCategory));
            }

            return counterExists;
        }

        private bool CounterExists(string category, string counter, ref bool categoryExists)
        {
            categoryExists = false;
            if (!CategoryTable.ContainsKey(category))
                return false;
            categoryExists = true;

            var entry = (CategoryEntry)CategoryTable[category];
            for (var index = 0; index < entry.CounterIndexes.Length; ++index)
            {
                var counterIndex = entry.CounterIndexes[index];
                var counterName = (string)NameTable[counterIndex];
                if (counterName == null)
                    counterName = string.Empty;

                if (string.Equals(counterName, counter, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        // Ensures that the customCategoryTable is initialized and decides whether the category passed in
        //  1) is a custom category
        //  2) is a multi instance custom category
        // The return value is whether the category is a custom category or not.
        internal bool FindCustomCategory(string category, out PerformanceCounterCategoryType categoryType)
        {
            RegistryKey key = null;
            RegistryKey baseKey = null;
            categoryType = PerformanceCounterCategoryType.Unknown;

            var table =
                _customCategoryTable ??
                Interlocked.CompareExchange(ref _customCategoryTable, new Dictionary<string, PerformanceCounterCategoryType>(StringComparer.OrdinalIgnoreCase), null) ??
                _customCategoryTable;

            if (table.ContainsKey(category))
            {
                categoryType = (PerformanceCounterCategoryType)table[category];
                return true;
            }

            try
            {
                var keyPath = ServicePath + "\\" + category + "\\Performance";
                key = Registry.LocalMachine.OpenSubKey(keyPath);

                if (key != null)
                {
                    var systemDllName = key.GetValue("Library", null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                    if (systemDllName != null && systemDllName is string
                                              && (string.Equals((string)systemDllName, PerfShimName, StringComparison.OrdinalIgnoreCase)
                                                  || ((string)systemDllName).EndsWith(PerfShimFullNameSuffix, StringComparison.OrdinalIgnoreCase)))
                    {

                        var isMultiInstanceObject = key.GetValue("IsMultiInstance");
                        if (isMultiInstanceObject != null)
                        {
                            categoryType = (PerformanceCounterCategoryType)isMultiInstanceObject;
                            if (categoryType < PerformanceCounterCategoryType.Unknown || categoryType > PerformanceCounterCategoryType.MultiInstance)
                                categoryType = PerformanceCounterCategoryType.Unknown;
                        }
                        else
                            categoryType = PerformanceCounterCategoryType.Unknown;

                        var objectID = key.GetValue("First Counter");
                        if (objectID != null)
                        {
                            var firstID = (int)objectID;
                            lock (table)
                            {
                                table[category] = categoryType;
                            }
                            return true;
                        }
                    }
                }
            }
            finally
            {
                if (key != null)
                    key.Close();
                if (baseKey != null)
                    baseKey.Close();
            }

            return false;
        }

        internal static string[] GetCategories()
        {
            PerformanceCounterLib library;
            var culture = CultureInfo.CurrentCulture;
            while (culture != CultureInfo.InvariantCulture)
            {
                library = GetPerformanceCounterLib(culture);
                var categories = library.GetCategories2();
                if (categories.Length != 0)
                    return categories;
                culture = culture.Parent;
            }

            library = GetPerformanceCounterLib(new CultureInfo(EnglishLCID));
            return library.GetCategories2();
        }

        internal string[] GetCategories2()
        {
            var keys = CategoryTable.Keys;
            var categories = new string[keys.Count];
            keys.CopyTo(categories, 0);
            return categories;
        }

        internal static string GetCategoryHelp(string category)
        {
            PerformanceCounterLib library;
            string help;

            //First check the current culture for the category. This will allow
            //PerformanceCounterCategory.CategoryHelp to return localized strings.
            if (CultureInfo.CurrentCulture.Parent.LCID != EnglishLCID)
            {
                var culture = CultureInfo.CurrentCulture;

                while (culture != CultureInfo.InvariantCulture)
                {
                    library = GetPerformanceCounterLib(culture);
                    help = library.GetCategoryHelp2(category);
                    if (help != null)
                        return help;
                    culture = culture.Parent;
                }
            }

            //We did not find the category walking up the culture hierarchy. Try looking
            // for the category in the default culture English.
            library = GetPerformanceCounterLib(new CultureInfo(EnglishLCID));
            help = library.GetCategoryHelp2(category);

            if (help == null)
                throw new InvalidOperationException(SR.Format(SR.MissingCategory));

            return help;
        }

        private string GetCategoryHelp2(string category)
        {
            var entry = (CategoryEntry)CategoryTable[category];
            if (entry == null)
                return null;

            return (string)HelpTable[entry.HelpIndex];
        }

        internal static CategorySample GetCategorySample(string category)
        {
            var library = GetPerformanceCounterLib(new CultureInfo(EnglishLCID));
            var sample = library.GetCategorySample2(category);
            if (sample == null && CultureInfo.CurrentCulture.Parent.LCID != EnglishLCID)
            {
                var culture = CultureInfo.CurrentCulture;
                while (culture != CultureInfo.InvariantCulture)
                {
                    library = GetPerformanceCounterLib(culture);
                    sample = library.GetCategorySample2(category);
                    if (sample != null)
                        return sample;
                    culture = culture.Parent;
                }
            }
            if (sample == null)
                throw new InvalidOperationException(SR.Format(SR.MissingCategory));

            return sample;
        }

        private CategorySample GetCategorySample2(string category)
        {
            var entry = (CategoryEntry)CategoryTable[category];
            if (entry == null)
                return null;

            CategorySample sample = null;
            var dataRef = GetPerformanceData(entry.NameIndex.ToString(CultureInfo.InvariantCulture));
            if (dataRef == null)
                throw new InvalidOperationException(SR.Format(SR.CantReadCategory, category));

            sample = new CategorySample(dataRef, entry, this);
            return sample;
        }

        internal static string[] GetCounters(string category)
        {
            var library = GetPerformanceCounterLib(new CultureInfo(EnglishLCID));
            var categoryExists = false;
            var counters = library.GetCounters(category, ref categoryExists);

            if (!categoryExists && CultureInfo.CurrentCulture.Parent.LCID != EnglishLCID)
            {
                var culture = CultureInfo.CurrentCulture;
                while (culture != CultureInfo.InvariantCulture)
                {
                    library = GetPerformanceCounterLib(culture);
                    counters = library.GetCounters(category, ref categoryExists);
                    if (categoryExists)
                        return counters;

                    culture = culture.Parent;
                }
            }

            if (!categoryExists)
                throw new InvalidOperationException(SR.Format(SR.MissingCategory));

            return counters;
        }

        private string[] GetCounters(string category, ref bool categoryExists)
        {
            categoryExists = false;
            var entry = (CategoryEntry)CategoryTable[category];
            if (entry == null)
                return null;
            categoryExists = true;

            var index2 = 0;
            var counters = new string[entry.CounterIndexes.Length];
            for (var index = 0; index < counters.Length; ++index)
            {
                var counterIndex = entry.CounterIndexes[index];
                var counterName = (string)NameTable[counterIndex];
                if (counterName != null && counterName != string.Empty)
                {
                    counters[index2] = counterName;
                    ++index2;
                }
            }

            //Lets adjust the array in case there were null entries
            if (index2 < counters.Length)
            {
                var adjustedCounters = new string[index2];
                Array.Copy(counters, adjustedCounters, index2);
                counters = adjustedCounters;
            }

            return counters;
        }

        internal static string GetCounterHelp(string category, string counter)
        {
            PerformanceCounterLib library;
            var categoryExists = false;
            string help;

            //First check the current culture for the counter. This will allow
            //PerformanceCounter.CounterHelp to return localized strings.
            if (CultureInfo.CurrentCulture.Parent.LCID != EnglishLCID)
            {
                var culture = CultureInfo.CurrentCulture;
                while (culture != CultureInfo.InvariantCulture)
                {
                    library = GetPerformanceCounterLib(culture);
                    help = library.GetCounterHelp(category, counter, ref categoryExists);
                    if (categoryExists)
                        return help;
                    culture = culture.Parent;
                }
            }

            //We did not find the counter walking up the culture hierarchy. Try looking
            // for the counter in the default culture English.
            library = GetPerformanceCounterLib(new CultureInfo(EnglishLCID));
            help = library.GetCounterHelp(category, counter, ref categoryExists);

            if (!categoryExists)
                throw new InvalidOperationException(SR.Format(SR.MissingCategoryDetail, category));

            return help;
        }

        private string GetCounterHelp(string category, string counter, ref bool categoryExists)
        {
            categoryExists = false;
            var entry = (CategoryEntry)CategoryTable[category];
            if (entry == null)
                return null;
            categoryExists = true;

            var helpIndex = -1;
            for (var index = 0; index < entry.CounterIndexes.Length; ++index)
            {
                var counterIndex = entry.CounterIndexes[index];
                var counterName = (string)NameTable[counterIndex];
                if (counterName == null)
                    counterName = string.Empty;

                if (string.Equals(counterName, counter, StringComparison.OrdinalIgnoreCase))
                {
                    helpIndex = entry.HelpIndexes[index];
                    break;
                }
            }

            if (helpIndex == -1)
                throw new InvalidOperationException(SR.Format(SR.MissingCounter, counter));

            var help = (string)HelpTable[helpIndex];
            if (help == null)
                return string.Empty;
            return help;
        }

        private static PerformanceCounterLib GetPerformanceCounterLib(CultureInfo culture)
        {
            var lcidString = culture.LCID.ToString("X3", CultureInfo.InvariantCulture);

            //race with CloseAllLibraries
            lock (InternalSyncObject)
            {
                var libraryKey = lcidString;
                if (_libraryTable.TryGetValue(libraryKey, out var result))
                {
                    return result;
                }

                var library = new PerformanceCounterLib(lcidString);
                _libraryTable[libraryKey] = library;
                return library;
            }
        }

        internal byte[] GetPerformanceData(string item)
        {
            if (_performanceMonitor == null)
            {
                lock (InternalSyncObject)
                {
                    if (_performanceMonitor == null)
                        _performanceMonitor = new PerformanceMonitor();
                }
            }

            return _performanceMonitor.GetData(item);
        }

        private Dictionary<int, string> GetStringTable(bool isHelp)
        {
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
                        if (!isHelp)
                            names = (string[])libraryKey.GetValue("Counter " + _perfLcid);
                        else
                            names = (string[])libraryKey.GetValue("Explain " + _perfLcid);

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

        internal static bool IsCustomCategory(string category)
        {
            var library = GetPerformanceCounterLib(new CultureInfo(EnglishLCID));
            if (library.IsCustomCategory2(category))
                return true;

            if (CultureInfo.CurrentCulture.Parent.LCID != EnglishLCID)
            {
                var culture = CultureInfo.CurrentCulture;
                while (culture != CultureInfo.InvariantCulture)
                {
                    library = GetPerformanceCounterLib(culture);
                    if (library.IsCustomCategory2(category))
                        return true;
                    culture = culture.Parent;
                }
            }

            return false;
        }

        internal static bool IsBaseCounter(int type)
        {
            return (type == Interop.Interop.Kernel32.PerformanceCounterOptions.PERF_AVERAGE_BASE ||
                    type == Interop.Interop.Kernel32.PerformanceCounterOptions.PERF_COUNTER_MULTI_BASE ||
                    type == Interop.Interop.Kernel32.PerformanceCounterOptions.PERF_RAW_BASE ||
                    type == Interop.Interop.Kernel32.PerformanceCounterOptions.PERF_LARGE_RAW_BASE ||
                    type == Interop.Interop.Kernel32.PerformanceCounterOptions.PERF_SAMPLE_BASE);
        }

        private bool IsCustomCategory2(string category)
        {
            PerformanceCounterCategoryType categoryType;

            return FindCustomCategory(category, out categoryType);
        }

        internal static PerformanceCounterCategoryType GetCategoryType(string category)
        {
            var categoryType = PerformanceCounterCategoryType.Unknown;

            var library = GetPerformanceCounterLib(new CultureInfo(EnglishLCID));
            if (!library.FindCustomCategory(category, out categoryType))
            {
                if (CultureInfo.CurrentCulture.Parent.LCID != EnglishLCID)
                {
                    var culture = CultureInfo.CurrentCulture;
                    while (culture != CultureInfo.InvariantCulture)
                    {
                        library = GetPerformanceCounterLib(culture);
                        if (library.FindCustomCategory(category, out categoryType))
                            return categoryType;
                        culture = culture.Parent;
                    }
                }
            }
            return categoryType;
        }
    }
}
