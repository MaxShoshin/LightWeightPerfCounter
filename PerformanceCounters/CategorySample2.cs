//using System;
//using System.Buffers;
//using System.Collections.Generic;
//using System.Diagnostics;
//using System.Globalization;
//using System.Threading;
//using LightWeight.PerformanceCounters.Resources;
//
//#if !netcoreapp
//using MemoryMarshal = LightWeight.PerformanceCounters.Extensions;
//#endif
//
//namespace LightWeight.PerformanceCounters
//{
//    internal class CategorySample2 : IDisposable
//    {
//        internal readonly long _systemFrequency;
//        internal readonly long _timeStamp;
//        internal readonly long _timeStamp100nSec;
//        private long _counterFrequency;
//        private long _counterTimeStamp;
//        private bool _isMultiInstance;
//        private readonly CategoryEntry _entry;
//        private int _disposed;
//        private readonly byte[] _data;
//
//        internal CategorySample2(byte[] rawData, CategoryEntry entry)
//        {
//            _data = rawData;
//            _entry = entry;
//
//            Span<byte> data = rawData;
//            ref readonly var dataBlock = ref MemoryMarshal.AsRef<Interop.Interop.Advapi32.PERF_DATA_BLOCK>(data);
//
//            _systemFrequency = dataBlock.PerfFreq;
//            _timeStamp = dataBlock.PerfTime;
//            _timeStamp100nSec = dataBlock.PerfTime100nSec;
//
//
//            // now set up the InstanceNameTable.
//            if (!_isMultiInstance)
//            {
//                for (var index = 0; index < samples.Length; ++index)
//                {
//                    samples[index].SetInstanceValue(0, data.Slice(pos));
//                }
//            }
//            else
//            {
//                for (var i = 0; i < instanceNumber; i++)
//                {
//                    ref readonly var perfInstance = ref MemoryMarshal.AsRef<Interop.Interop.Advapi32.PERF_INSTANCE_DEFINITION>(data.Slice(pos));
//
//                    pos += perfInstance.ByteLength;
//
//                    for (var index = 0; index < samples.Length; ++index)
//                        samples[index].SetInstanceValue(i, data.Slice(pos));
//
//                    pos += MemoryMarshal.AsRef<Interop.Interop.Advapi32.PERF_COUNTER_BLOCK>(data.Slice(pos)).ByteLength;
//                }
//            }
//        }
//
//        internal CounterSample GetCounterSample(int counterIndex, int instanceIndex)
//        {
//            CheckDisposed();
//
//            var pos = FindCategory();
//            pos = ReadData(pos, out var counterNumber, out var instanceNumber);
//
//            return FindSample(pos, counterNumber, instanceNumber, counterIndex, instanceIndex);
//        }
//
//
//        private CounterSample FindSample(int pos, int counterNumber, int instanceNumber, int counterIndex, int instanceIndex)
//        {
//            ReadOnlySpan<byte> data = _data;
//            for (var index = 0; index < counterNumber; ++index)
//            {
//                ref readonly var perfCounter = ref MemoryMarshal.AsRef<Interop.Interop.Advapi32.PERF_COUNTER_DEFINITION>(data.Slice(pos));
//                var sampleNameIndex = perfCounter.CounterNameTitleIndex;
//
//                pos += perfCounter.ByteLength;
//
//                if (sampleNameIndex == counterIndex)
//                {
//                    if (index + 1 < counterNumber)
//                    {
//                        ref readonly var nextPerfCounter = ref MemoryMarshal.AsRef<Interop.Interop.Advapi32.PERF_COUNTER_DEFINITION>(data.Slice(pos));
//                        var nextCounterType = nextPerfCounter.CounterType;
//
//                        if (PerformanceCounterLib.IsBaseCounter(nextCounterType))
//                        {
//                            return new CounterDefinitionSample();
//                        }
//                    }
//
//                    return new CounterDefinitionSample(in perfCounter, this, instanceNumber);
//                }
//            }
//
//            throw new InvalidOperationException(SR.Format(SR.CounterLayout));
//        }
//
//        private int ReadData(int pos, out int counterNumber, out int instanceNumber)
//        {
//            ReadOnlySpan<byte> data = _data;
//            ref readonly var perfObject = ref MemoryMarshal.AsRef<Interop.Interop.Advapi32.PERF_OBJECT_TYPE>(data.Slice(pos));
//
//            _counterFrequency = perfObject.PerfFreq;
//            _counterTimeStamp = perfObject.PerfTime;
//
//            counterNumber = perfObject.NumCounters;
//            instanceNumber = perfObject.NumInstances;
//
//            if (instanceNumber == -1)
//                _isMultiInstance = false;
//            else
//                _isMultiInstance = true;
//
//            // Move pointer forward to end of PERF_OBJECT_TYPE
//            return pos + perfObject.HeaderLength;
//
//        }
//
//        private int FindCategory()
//        {
//            ReadOnlySpan<byte> data = _data;
//
//            var categoryIndex = _entry.NameIndex;
//
//            ref readonly var dataBlock = ref MemoryMarshal.AsRef<Interop.Interop.Advapi32.PERF_DATA_BLOCK>(data);
//
//            var pos = dataBlock.HeaderLength;
//            var numPerfObjects = dataBlock.NumObjectTypes;
//            if (numPerfObjects == 0)
//            {
//                throw new InvalidOperationException(SR.Format(SR.CounterLayout));
//            }
//
//            //Need to find the right category, GetPerformanceData might return
//            //several of them.
//            var foundCategory = false;
//            for (var index = 0; index < numPerfObjects; index++)
//            {
//                ref readonly var perfObjectType = ref MemoryMarshal.AsRef<Interop.Interop.Advapi32.PERF_OBJECT_TYPE>(data.Slice(pos));
//
//                if (perfObjectType.ObjectNameTitleIndex == categoryIndex)
//                {
//                    foundCategory = true;
//                    break;
//                }
//
//                pos += perfObjectType.TotalByteLength;
//            }
//
//            if (!foundCategory)
//                throw new InvalidOperationException(SR.Format(SR.CantReadCategoryIndex, categoryIndex.ToString(CultureInfo.CurrentCulture)));
//
//
//            return pos;
//        }
//
//        public void Dispose()
//        {
//            if (Interlocked.Exchange(ref _disposed, 1) == 1)
//            {
//                return;
//            }
//
//            ArrayPool<byte>.Shared.Return(_data);
//        }
//
//        private void CheckDisposed()
//        {
//            if (_disposed == 1)
//            {
//                throw new ObjectDisposedException(SR.ObjectDisposed_CategorySampleClosed, nameof(CategorySample));
//            }
//        }
//    }
//}