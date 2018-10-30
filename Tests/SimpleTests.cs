using System;
using System.Linq;
using LightWeight.PerformanceCounters;
using Xunit;
using Xunit.Abstractions;

namespace Tests
{
    public class SimpleTests
    {
        private ITestOutputHelper _output;

        public SimpleTests(ITestOutputHelper output)
        {
            _output = output ?? throw new ArgumentNullException(nameof(output));
        }

        [Fact]
        public void Read_Processor_Usage()
        {
            var counter = PerformanceCounter.Create("Processor", "% Processor Time", "_Total");

            for (int i = 0; i < 100; i++)
            {
                _output.WriteLine(counter.NextValue().ToString());
            }
        }

        [Fact]
        public void List_Processor_Counters()
        {
            var processor = PerformanceCounterCategory.GetCategories().First(category => category.CategoryName == "Processor");
            var instanceNames = processor.GetInstanceNames();

            foreach (var instanceName in instanceNames)
            {
                _output.WriteLine(instanceName);
            }

            _output.WriteLine("");

            var counters = processor.GetCounters(instanceNames.First()).OrderBy(item => item.CounterName);

            foreach (var counter in counters)
            {
                _output.WriteLine("{0}   __  {1}", counter.CounterName, counter.InstanceName);
            }
        }


        [Fact]
        public void List_All_Categories()
        {
            var categories = PerformanceCounterCategory.GetCategories()
                .OrderBy(item => item.CategoryName);
            foreach (var category in categories)
            {
                _output.WriteLine("{0} {1}", category.CategoryName, category.CategoryType);
            }
        }
    }
}