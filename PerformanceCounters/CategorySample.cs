using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using LightWeight.PerformanceCounters.Resources;

namespace LightWeight.PerformanceCounters
{
    internal class CategorySample : IDisposable
    {
        internal readonly long _systemFrequency;
        internal readonly long _timeStamp;
        internal readonly long _timeStamp100nSec;
        internal readonly long _counterFrequency;
        internal readonly long _counterTimeStamp;
        internal readonly Dictionary<int, CounterDefinitionSample> _counterTable;
        internal Dictionary<string, int> _instanceNameTable;
        internal bool _isMultiInstance;
        private CategoryEntry _entry;
        private PerformanceCounterLib _library;
        private int _disposed;
        private byte[] _data;

        internal CategorySample(byte[] rawData, CategoryEntry entry, PerformanceCounterLib library)
        {
            _data = rawData;
            ReadOnlySpan<byte> data = rawData;
            _entry = entry;
            _library = library;
            var categoryIndex = entry.NameIndex;

            ref readonly var dataBlock = ref PerformanceCounterLib.AsRef<Interop.Interop.Advapi32.PERF_DATA_BLOCK>(data);

            _systemFrequency = dataBlock.PerfFreq;
            _timeStamp = dataBlock.PerfTime;
            _timeStamp100nSec = dataBlock.PerfTime100nSec;
            var pos = dataBlock.HeaderLength;
            var numPerfObjects = dataBlock.NumObjectTypes;
            if (numPerfObjects == 0)
            {
                _counterTable = new Dictionary<int, CounterDefinitionSample>();
                _instanceNameTable = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                return;
            }

            //Need to find the right category, GetPerformanceData might return
            //several of them.
            var foundCategory = false;
            for (var index = 0; index < numPerfObjects; index++)
            {
                ref readonly var perfObjectType = ref PerformanceCounterLib.AsRef<Interop.Interop.Advapi32.PERF_OBJECT_TYPE>(data.Slice(pos));

                if (perfObjectType.ObjectNameTitleIndex == categoryIndex)
                {
                    foundCategory = true;
                    break;
                }

                pos += perfObjectType.TotalByteLength;
            }

            if (!foundCategory)
                throw new InvalidOperationException(SR.Format(SR.CantReadCategoryIndex, categoryIndex.ToString(CultureInfo.CurrentCulture)));

            ref readonly var perfObject = ref PerformanceCounterLib.AsRef<Interop.Interop.Advapi32.PERF_OBJECT_TYPE>(data.Slice(pos));

            _counterFrequency = perfObject.PerfFreq;
            _counterTimeStamp = perfObject.PerfTime;
            var counterNumber = perfObject.NumCounters;
            var instanceNumber = perfObject.NumInstances;

            if (instanceNumber == -1)
                _isMultiInstance = false;
            else
                _isMultiInstance = true;

            // Move pointer forward to end of PERF_OBJECT_TYPE
            pos += perfObject.HeaderLength;

            var samples = new CounterDefinitionSample[counterNumber];
            _counterTable = new Dictionary<int, CounterDefinitionSample>(counterNumber);
            for (var index = 0; index < samples.Length; ++index)
            {
                ref readonly var perfCounter = ref PerformanceCounterLib.AsRef<Interop.Interop.Advapi32.PERF_COUNTER_DEFINITION>(data.Slice(pos));
                samples[index] = new CounterDefinitionSample(in perfCounter, this, instanceNumber);
                pos += perfCounter.ByteLength;

                var currentSampleType = samples[index]._counterType;
                if (!PerformanceCounterLib.IsBaseCounter(currentSampleType))
                {
                    // We'll put only non-base counters in the table.
                    if (currentSampleType != Interop.Interop.Kernel32.PerformanceCounterOptions.PERF_COUNTER_NODATA)
                        _counterTable[samples[index]._nameIndex] = samples[index];
                }
                else
                {
                    // it's a base counter, try to hook it up to the main counter.
                    Debug.Assert(index > 0, "Index > 0 because base counters should never be at index 0");
                    if (index > 0)
                        samples[index - 1]._baseCounterDefinitionSample = samples[index];
                }
            }

            // now set up the InstanceNameTable.
            if (!_isMultiInstance)
            {
                _instanceNameTable = new Dictionary<string, int>(1, StringComparer.OrdinalIgnoreCase);
                _instanceNameTable[PerformanceCounterLib.SingleInstanceName] = 0;

                for (var index = 0; index < samples.Length; ++index)
                {
                    samples[index].SetInstanceValue(0, data.Slice(pos));
                }
            }
            else
            {
                string[] parentInstanceNames = null;
                _instanceNameTable = new Dictionary<string, int>(instanceNumber, StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < instanceNumber; i++)
                {
                    ref readonly var perfInstance = ref PerformanceCounterLib.AsRef<Interop.Interop.Advapi32.PERF_INSTANCE_DEFINITION>(data.Slice(pos));
                    if (perfInstance.ParentObjectTitleIndex > 0 && parentInstanceNames == null)
                        parentInstanceNames = GetInstanceNamesFromIndex(perfInstance.ParentObjectTitleIndex);

                    var instanceName = Interop.Interop.Advapi32.PERF_INSTANCE_DEFINITION.GetName(in perfInstance, data.Slice(pos)).ToString();
                    if (parentInstanceNames != null && perfInstance.ParentObjectInstance >= 0 && perfInstance.ParentObjectInstance < parentInstanceNames.Length - 1)
                        instanceName = parentInstanceNames[perfInstance.ParentObjectInstance] + "/" + instanceName;

                    //In some cases instance names are not unique (Process), same as perfmon
                    //generate a unique name.
                    var newInstanceName = instanceName;
                    var newInstanceNumber = 1;
                    while (true)
                    {
                        if (!_instanceNameTable.ContainsKey(newInstanceName))
                        {
                            _instanceNameTable[newInstanceName] = i;
                            break;
                        }

                        newInstanceName = instanceName + "#" + newInstanceNumber.ToString(CultureInfo.InvariantCulture);
                        ++newInstanceNumber;
                    }


                    pos += perfInstance.ByteLength;

                    for (var index = 0; index < samples.Length; ++index)
                        samples[index].SetInstanceValue(i, data.Slice(pos));

                    pos += PerformanceCounterLib.AsRef<Interop.Interop.Advapi32.PERF_COUNTER_BLOCK>(data.Slice(pos)).ByteLength;
                }
            }
        }

        internal string[] GetInstanceNamesFromIndex(int categoryIndex)
        {
            CheckDisposed();

            ReadOnlySpan<byte> data = _library.GetPerformanceData(categoryIndex.ToString(CultureInfo.InvariantCulture));

            ref readonly var dataBlock = ref PerformanceCounterLib.AsRef<Interop.Interop.Advapi32.PERF_DATA_BLOCK>(data);
            var pos = dataBlock.HeaderLength;
            var numPerfObjects = dataBlock.NumObjectTypes;

            var foundCategory = false;
            for (var index = 0; index < numPerfObjects; index++)
            {
                ref readonly var type = ref PerformanceCounterLib.AsRef<Interop.Interop.Advapi32.PERF_OBJECT_TYPE>(data.Slice(pos));

                if (type.ObjectNameTitleIndex == categoryIndex)
                {
                    foundCategory = true;
                    break;
                }

                pos += type.TotalByteLength;
            }

            if (!foundCategory)
                return Array.Empty<string>();

            ref readonly var perfObject = ref PerformanceCounterLib.AsRef<Interop.Interop.Advapi32.PERF_OBJECT_TYPE>(data.Slice(pos));

            var counterNumber = perfObject.NumCounters;
            var instanceNumber = perfObject.NumInstances;
            pos += perfObject.HeaderLength;

            if (instanceNumber == -1)
                return Array.Empty<string>();

            var samples = new CounterDefinitionSample[counterNumber];
            for (var index = 0; index < samples.Length; ++index)
            {
                pos += PerformanceCounterLib.AsRef<Interop.Interop.Advapi32.PERF_COUNTER_DEFINITION>(data.Slice(pos)).ByteLength;
            }

            var instanceNames = new string[instanceNumber];
            for (var i = 0; i < instanceNumber; i++)
            {
                ref readonly var perfInstance = ref PerformanceCounterLib.AsRef<Interop.Interop.Advapi32.PERF_INSTANCE_DEFINITION>(data.Slice(pos));
                instanceNames[i] = Interop.Interop.Advapi32.PERF_INSTANCE_DEFINITION.GetName(in perfInstance, data.Slice(pos)).ToString();
                pos += perfInstance.ByteLength;

                pos += PerformanceCounterLib.AsRef<Interop.Interop.Advapi32.PERF_COUNTER_BLOCK>(data.Slice(pos)).ByteLength;
            }

            return instanceNames;
        }

        internal CounterDefinitionSample GetCounterDefinitionSample(string counter)
        {
            CheckDisposed();

            for (var index = 0; index < _entry.CounterIndexes.Length; ++index)
            {
                var counterIndex = _entry.CounterIndexes[index];
                var counterName = (string)_library.NameTable[counterIndex];
                if (counterName != null)
                {
                    if (string.Equals(counterName, counter, StringComparison.OrdinalIgnoreCase))
                    {
                        var sample = _counterTable[counterIndex];
                        if (sample == null)
                        {
                            //This is a base counter and has not been added to the table
                            foreach (var multiSample in _counterTable.Values)
                            {
                                if (multiSample._baseCounterDefinitionSample != null &&
                                    multiSample._baseCounterDefinitionSample._nameIndex == counterIndex)
                                    return multiSample._baseCounterDefinitionSample;
                            }

                            throw new InvalidOperationException(SR.Format(SR.CounterLayout));
                        }
                        return sample;
                    }
                }
            }

            throw new InvalidOperationException(SR.Format(SR.CantReadCounter, counter));
        }

        internal InstanceDataCollectionCollection ReadCategory()
        {
            var data = new InstanceDataCollectionCollection();
            for (var index = 0; index < _entry.CounterIndexes.Length; ++index)
            {
                var counterIndex = _entry.CounterIndexes[index];

                var name = _library.NameTable[counterIndex];
                if (!string.IsNullOrEmpty(name))
                {
                    var sample = _counterTable[counterIndex];
                    if (sample != null)
                        //If the current index refers to a counter base,
                        //the sample will be null
                        data.Add(name, sample.ReadInstanceData(name));
                }
            }

            return data;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
            {
                return;
            }

            ArrayPool<byte>.Shared.Return(_data);
        }

        private void CheckDisposed()
        {
            if (_disposed == 1)
            {
                throw new ObjectDisposedException(SR.ObjectDisposed_CategorySampleClosed, nameof(CategorySample));
            }
        }
    }
}