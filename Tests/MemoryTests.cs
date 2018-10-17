using System;
using System.Linq;
using JetBrains.dotMemoryUnit;
using LightWeight.PerformanceCounters;
using Xunit;
using Xunit.Abstractions;

namespace Tests
{
    [DotMemoryUnit(CollectAllocations = true, FailIfRunWithoutSupport = true)]
    public class MemoryTests
    {
        private ITestOutputHelper _output;

        public MemoryTests(ITestOutputHelper output)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));

            _output = output;
            DotMemoryUnitTestOutput.SetOutputMethod(item => _output.WriteLine(item));
        }

        [Fact]
        public void Memory_Traffic_On_Single_Read()
        {
            var counter = new PerformanceCounter("Processor", "% Processor Time", "_Total");

            counter.NextValue();

            var start = dotMemory.Check();

            counter.NextValue();

            dotMemory.Check(memory =>
            {
                var traffic = memory.GetTrafficFrom(start);

                WriteMemoryTraffic("Total", traffic.AllocatedMemory, traffic.CollectedMemory);
                _output.WriteLine("");

                var typeInfo = traffic.GroupByType().OrderByDescending(item => item.AllocatedMemoryInfo.SizeInBytes);

                foreach (var typeTrafficInfo in typeInfo)
                {
                    WriteMemoryTraffic(typeTrafficInfo.Type.Name, typeTrafficInfo.AllocatedMemoryInfo, typeTrafficInfo.CollectedMemoryInfo);
                }
            });
        }

        private void WriteMemoryTraffic(string name, MemoryInfo allocatedMemory, MemoryInfo collectedMemory)
        {
            _output.WriteLine("{4}: Allocated: {0} objects ({1} bytes). Collected: {2} objects ({3} bytes)",
                allocatedMemory.ObjectsCount,
                allocatedMemory.SizeInBytes,
                collectedMemory.ObjectsCount,
                collectedMemory.SizeInBytes,
                name);
        }
    }
}