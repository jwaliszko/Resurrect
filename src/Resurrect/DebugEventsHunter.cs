using System;
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
            switch (mode)
            {
                case DBGMODE.DBGMODE_Design:
                    Storage.Instance.Persist();
                    AttachCenter.Instance.Unfreeze();
                    break;
            }
            return VSConstants.S_OK;
        }

        public int Event(IDebugEngine2 engine, IDebugProcess2 process, IDebugProgram2 program,
                         IDebugThread2 thread, IDebugEvent2 debugEvent, ref Guid riidEvent, uint attributes)
        {
            if (process != null)
            {
                string processName;
                if (process.GetName((uint) enum_GETNAME_TYPE.GN_FILENAME, out processName) == VSConstants.S_OK)
                {
                    if (!processName.EndsWith("vshost.exe"))
                    {
                        if (debugEvent is IDebugProcessCreateEvent2)
                        {
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
                                    Storage.Instance.SubscribeEngine(engineId);
                            }
                        }
                    }
                }
            }
            return VSConstants.S_OK;
        }
    }
}
