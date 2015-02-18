using System;
using System.Collections.Generic;
using System.IO;

namespace Resurrect
{
    internal class AttachData
    {
        public string ProcessName { get; set; }
        public IList<Guid> DebugEngines { get; set; }

        public string ShortProcessName
        {
            get { return Path.GetFileName(ProcessName); }
        }

        public AttachData()
        {
            DebugEngines = new List<Guid>();
        }

        public AttachData(AttachData instance)
        {
            ProcessName = instance.ProcessName;
            DebugEngines = instance.DebugEngines;
        }
    }
}
