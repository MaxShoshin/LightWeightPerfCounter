using System;
using System.Threading;

namespace Tests
{
    public class Program
    {
        public static void Main()
        {
            var tests = new MemoryTests(null);
            tests.RunExperiment();

            Console.WriteLine("Attach profiler an press any key.");
            Console.ReadKey();

            tests.RunExperiment();

            Thread.Sleep(20000);
        }
    }
}