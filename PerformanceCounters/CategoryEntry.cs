namespace LightWeight.PerformanceCounters
{
    internal class CategoryEntry
    {
        internal int NameIndex;
        internal int HelpIndex;
        internal int[] CounterIndexes;
        internal int[] HelpIndexes;

        internal CategoryEntry(in Interop.Interop.Advapi32.PERF_OBJECT_TYPE perfObject)
        {
            NameIndex = perfObject.ObjectNameTitleIndex;
            HelpIndex = perfObject.ObjectHelpTitleIndex;
            CounterIndexes = new int[perfObject.NumCounters];
            HelpIndexes = new int[perfObject.NumCounters];
        }
    }
}