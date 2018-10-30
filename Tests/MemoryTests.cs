using System;
using System.Linq;
using System.Threading;
using JetBrains.dotMemoryUnit;
using LightWeight.PerformanceCounters;
using Xunit;
using Xunit.Abstractions;

namespace Tests
{
    [DotMemoryUnit(CollectAllocations = true, FailIfRunWithoutSupport = true)]
    public class MemoryTests
    {
        private readonly ITestOutputHelper _output;
        private readonly PerformanceCounter _counter;

        public MemoryTests(ITestOutputHelper output)
        {
            if (output != null)
            {
                _output = output;
                DotMemoryUnitTestOutput.SetOutputMethod(item => _output.WriteLine(item));
            }

            _counter = PerformanceCounter.Create("Processor", "% Processor Time", "_Total");
        }

        [Fact]
        public void Memory_Traffic_On_Single_Read()
        {
            RunExperiment();
            RunExperiment();

            var start = dotMemory.Check();

            RunExperiment();

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

        public void RunExperiment()
        {
            _counter.NextValue();

            Thread.Sleep(100);

            _counter.NextValue();
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