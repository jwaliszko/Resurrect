using System;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Debugger.Interop;
using Microsoft.VisualStudio.Shell.Interop;

namespace Resurrect
{
    internal sealed class DebugEventsHunter : IVsDebuggerEvents, IDebugEventCallback2
    {
        private readonly IVsDebugger _debugger;
        private uint _cookie;

        private static DebugEventsHunter _instance;
        private static readonly object _locker = new object();

        public DebugEventsHunter(IVsDebugger debugger)
        {
            _debugger = debugger;
        }

        public static void Instantiate(IVsDebugger debugger)
        {
            lock (_locker)
            {
                if (_instance != null)
                    throw new InvalidOperationException(string.Format("{0} of Resurrect is already instantiated.", _instance.GetType().Name));
                _instance = new DebugEventsHunter(debugger);
            }
        }

        public static DebugEventsHunter Instance
        {
            get { return _instance; }
        }

        public void SendPatrol()
        {
            _debugger.AdviseDebuggerEvents(this, out _cookie);
            _debugger.AdviseDebugEventCallback(this);
        }

        public void DismissPatrol()
        {
            _debugger.UnadviseDebuggerEvents(_cookie);
            _debugger.UnadviseDebugEventCallback(this);
        }

        public int OnModeChange(DBGMODE mode)
        {
            Log.Instance.Clear();
            switch (mode)
            {
                case DBGMODE.DBGMODE_Design:
                    AttachCenter.Instance.Unfreeze();
                    Storage.Instance.Persist();
                    break;
            }
            return VSConstants.S_OK;
        }

        public int Event(IDebugEngine2 engine, IDebugProcess2 process, IDebugProgram2 program,
                         IDebugThread2 thread, IDebugEvent2 debugEvent, ref Guid riidEvent, uint attributes)
        {
            if (process == null)
                return VSConstants.S_OK;
            string processName;
            if (process.GetName((uint) enum_GETNAME_TYPE.GN_FILENAME, out processName) != VSConstants.S_OK)
                return VSConstants.S_OK;
            if (processName.EndsWith("vshost.exe"))
                return VSConstants.S_OK;            

            if (debugEvent is IDebugProcessCreateEvent2)
            {
                Log.Instance.SetStatus("[attaching...] {0}", Path.GetFileName(processName));
                Storage.Instance.SubscribeProcess(processName);
                AttachCenter.Instance.Freeze();
            }
            if (debugEvent is IDebugLoadCompleteEvent2)
            {
                if (program != null)
                {
                    string engineName;
                    Guid engineId;                    
                    if (program.GetEngineInfo(out engineName, out engineId) == VSConstants.S_OK)
                    {
                        var fields = new PROCESS_INFO[1];
                        if (process.GetInfo((uint)enum_PROCESS_INFO_FIELDS.PIF_PROCESS_ID, fields) != VSConstants.S_OK)
                            return VSConstants.S_OK;
                        var processId = fields[0].ProcessId.dwProcessId;
                        Log.Instance.AppendLine("[attached] {0} ({1}) / {2}", Path.GetFileName(processName), processId, AttachCenter.Instance.GetEnginesNames(new[] {engineId}).Single());
                        Storage.Instance.SubscribeEngine(processName, engineId);
                    }
                }
            }
            return VSConstants.S_OK;
        }
    }
}
