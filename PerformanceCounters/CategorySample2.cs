using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using LightWeight.PerformanceCounters.Resources;

#if !netcoreapp
using MemoryMarshal = LightWeight.PerformanceCounters.Extensions;
#endif

namespace LightWeight.PerformanceCounters
{
    internal class CategorySample2 : IDisposable
    {
        internal readonly long _systemFrequency;
        internal readonly long _timeStamp;
        internal readonly long _timeStamp100nSec;
        internal readonly long _counterFrequency;
        internal readonly long _counterTimeStamp;
        internal readonly bool _isMultiInstance;
        private readonly CategoryEntry _entry;
        private int _disposed;
        private readonly byte[] _data;

        internal CategorySample2(byte[] rawData, CategoryEntry entry)
        {
            _data = rawData;
            ReadOnlySpan<byte> data = rawData;
            _entry = entry;
            var categoryIndex = entry.NameIndex;

            ref readonly var dataBlock = ref MemoryMarshal.AsRef<Interop.Interop.Advapi32.PERF_DATA_BLOCK>(data);

            _systemFrequency = dataBlock.PerfFreq;
            _timeStamp = dataBlock.PerfTime;
            _timeStamp100nSec = dataBlock.PerfTime100nSec;
            var pos = dataBlock.HeaderLength;
            var numPerfObjects = dataBlock.NumObjectTypes;
            if (numPerfObjects == 0)
            {
                return;
            }

            //Need to find the right category, GetPerformanceData might return
            //several of them.
            var foundCategory = false;
            for (var index = 0; index < numPerfObjects; index++)
            {
                ref readonly var perfObjectType = ref MemoryMarshal.AsRef<Interop.Interop.Advapi32.PERF_OBJECT_TYPE>(data.Slice(pos));

                if (perfObjectType.ObjectNameTitleIndex == categoryIndex)
                {
                    foundCategory = true;
                    break;
                }

                pos += perfObjectType.TotalByteLength;
            }

            if (!foundCategory)
                throw new InvalidOperationException(SR.Format(SR.CantReadCategoryIndex, categoryIndex.ToString(CultureInfo.CurrentCulture)));

            ref readonly var perfObject = ref MemoryMarshal.AsRef<Interop.Interop.Advapi32.PERF_OBJECT_TYPE>(data.Slice(pos));

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

            for (var index = 0; index < samples.Length; ++index)
            {
                ref readonly var perfCounter = ref MemoryMarshal.AsRef<Interop.Interop.Advapi32.PERF_COUNTER_DEFINITION>(data.Slice(pos));
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
                for (var index = 0; index < samples.Length; ++index)
                {
                    samples[index].SetInstanceValue(0, data.Slice(pos));
                }
            }
            else
            {
                for (var i = 0; i < instanceNumber; i++)
                {
                    ref readonly var perfInstance = ref MemoryMarshal.AsRef<Interop.Interop.Advapi32.PERF_INSTANCE_DEFINITION>(data.Slice(pos));

                    pos += perfInstance.ByteLength;

                    for (var index = 0; index < samples.Length; ++index)
                        samples[index].SetInstanceValue(i, data.Slice(pos));

                    pos += MemoryMarshal.AsRef<Interop.Interop.Advapi32.PERF_COUNTER_BLOCK>(data.Slice(pos)).ByteLength;
                }
            }
        }


        internal CounterDefinitionSample GetCounterDefinitionSample(string counter)
        {
            CheckDisposed();

            for (var index = 0; index < _entry.CounterIndexes.Length; ++index)
            {
                var counterIndex = _entry.CounterIndexes[index];
                if (!_library.NameTable.TryGetValue(counterIndex, out var counterName))
                {
                    continue;
                }

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

            throw new InvalidOperationException(SR.Format(SR.CantReadCounter, counter));
        }

        internal InstanceDataCollectionCollection ReadCategory()
        {
            var data = new InstanceDataCollectionCollection();
            for (var index = 0; index < _entry.CounterIndexes.Length; ++index)
            {
                var counterIndex = _entry.CounterIndexes[index];

                _library.NameTable.TryGetValue(counterIndex, out var name);

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