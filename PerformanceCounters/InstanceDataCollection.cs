// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;

namespace LightWeight.PerformanceCounters
{
    /// <summary>
    ///     The collection returned from  the <see cref='PerformanceCounterCategory.ReadCategory'/> method.
    ///     that contains all the counter and instance data.
    ///     The collection contains an InstanceDataCollection object for each counter.  Each InstanceDataCollection
    ///     object contains the performance data for all counters for that instance.  In other words the data is
    ///     indexed by counter name and then by instance name.
    /// </summary>    
    public class InstanceDataCollection : Dictionary<string, InstanceData>
    {
    }

    public class InstanceDataCollectionCollection : Dictionary<string, InstanceDataCollection>
    {
    }
}
