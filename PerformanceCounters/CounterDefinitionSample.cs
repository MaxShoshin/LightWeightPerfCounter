using System;
using LightWeight.PerformanceCounters.Resources;

#if !netcoreapp
using MemoryMarshal = LightWeight.PerformanceCounters.Extensions;
#endif

namespace LightWeight.PerformanceCounters
{
    internal class CounterDefinitionSample
    {
        internal readonly int _nameIndex;
        internal readonly int _counterType;
        internal CounterDefinitionSample _baseCounterDefinitionSample;

        private readonly int _size;
        private readonly int _offset;
        private long[] _instanceValues;
        private CategorySample _categorySample;

        internal CounterDefinitionSample(in Interop.Interop.Advapi32.PERF_COUNTER_DEFINITION perfCounter, CategorySample categorySample, int instanceNumber)
        {
            _nameIndex = perfCounter.CounterNameTitleIndex;
            _counterType = perfCounter.CounterType;
            _offset = perfCounter.CounterOffset;
            _size = perfCounter.CounterSize;
            if (instanceNumber == -1)
            {
                _instanceValues = new long[1];
            }
            else
                _instanceValues = new long[instanceNumber];

            _categorySample = categorySample;
        }

        private long ReadValue(ReadOnlySpan<byte> data)
        {
            if (_size == 4)
            {
                return MemoryMarshal.Read<uint>(data.Slice(_offset));
            }

            if (_size == 8)
            {
                return MemoryMarshal.Read<long>(data.Slice(_offset));
            }

            return -1;
        }

        internal CounterSample GetInstanceValue(string instanceName)
        {

            if (!_categorySample._instanceNameTable.ContainsKey(instanceName))
            {
                // Our native dll truncates instance names to 128 characters.  If we can't find the instance
                // with the full name, try truncating to 128 characters. 
                if (instanceName.Length > Constants.InstanceNameMaxLength)
                    instanceName = instanceName.Substring(0, Constants.InstanceNameMaxLength);

                if (!_categorySample._instanceNameTable.ContainsKey(instanceName))
                    throw new InvalidOperationException(SR.Format(SR.CantReadInstance, instanceName));
            }

            var index = _categorySample._instanceNameTable[instanceName];
            var rawValue = _instanceValues[index];
            long baseValue = 0;
            if (_baseCounterDefinitionSample != null)
            {
                var baseCategorySample = _baseCounterDefinitionSample._categorySample;
                var baseIndex = baseCategorySample._instanceNameTable[instanceName];
                baseValue = _baseCounterDefinitionSample._instanceValues[baseIndex];
            }

            return new CounterSample(rawValue,
                baseValue,
                _categorySample._counterFrequency,
                _categorySample._systemFrequency,
                _categorySample._timeStamp,
                _categorySample._timeStamp100nSec,
                (PerformanceCounterType)_counterType,
                _categorySample._counterTimeStamp);

        }

        internal InstanceDataCollection ReadInstanceData(string counterName)
        {
            var data = new InstanceDataCollection();

            var keys = new string[_categorySample._instanceNameTable.Count];
            _categorySample._instanceNameTable.Keys.CopyTo(keys, 0);
            var indexes = new int[_categorySample._instanceNameTable.Count];
            _categorySample._instanceNameTable.Values.CopyTo(indexes, 0);
            for (var index = 0; index < keys.Length; ++index)
            {
                long baseValue = 0;
                if (_baseCounterDefinitionSample != null)
                {
                    var baseCategorySample = _baseCounterDefinitionSample._categorySample;
                    var baseIndex = baseCategorySample._instanceNameTable[keys[index]];
                    baseValue = _baseCounterDefinitionSample._instanceValues[baseIndex];
                }

                var sample = new CounterSample(_instanceValues[indexes[index]],
                    baseValue,
                    _categorySample._counterFrequency,
                    _categorySample._systemFrequency,
                    _categorySample._timeStamp,
                    _categorySample._timeStamp100nSec,
                    (PerformanceCounterType)_counterType,
                    _categorySample._counterTimeStamp);

                data.Add(keys[index], new InstanceData(keys[index], sample));
            }

            return data;
        }

        internal CounterSample GetSingleValue()
        {
            var rawValue = _instanceValues[0];
            long baseValue = 0;
            if (_baseCounterDefinitionSample != null)
                baseValue = _baseCounterDefinitionSample._instanceValues[0];

            return new CounterSample(rawValue,
                baseValue,
                _categorySample._counterFrequency,
                _categorySample._systemFrequency,
                _categorySample._timeStamp,
                _categorySample._timeStamp100nSec,
                (PerformanceCounterType)_counterType,
                _categorySample._counterTimeStamp);
        }

        internal void SetInstanceValue(int index, ReadOnlySpan<byte> data)
        {
            var rawValue = ReadValue(data);
            _instanceValues[index] = rawValue;
        }
    }
}