﻿using System;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Debugger.Interop;
using Microsoft.VisualStudio.Shell.Interop;

namespace Resurrect
{
    public class DebugEventsHunter : IVsDebuggerEvents, IDebugEventCallback2
    {
        private readonly IVsDebugger _debugger;
        private uint _cookie;

        public DebugEventsHunter(IVsDebugger debugger)
        {
            _debugger = debugger;            
        }

        public void Listen()
        {
            _debugger.AdviseDebuggerEvents(this, out _cookie);
            _debugger.AdviseDebugEventCallback(this);
        }

        public int Event(IDebugEngine2 engine, IDebugProcess2 process, IDebugProgram2 program,
                         IDebugThread2 thread, IDebugEvent2 debugEvent, ref Guid riidEvent, uint attributes)
        {
            if (process != null)
            {
                string name;
                process.GetName((uint) enum_GETNAME_TYPE.GN_FILENAME, out name);

                if (debugEvent is IDebugProcessCreateEvent2)
                {
                    AttachCenter.Instance.Freeze();                 
                }
                if (debugEvent is IDebugProcessDestroyEvent2)
                {
                    HistoricStorage.Instance.Subscribe(name);
                }

            }
            return VSConstants.S_OK;
        }

        public int OnModeChange(DBGMODE mode)
        {
            if (mode == DBGMODE.DBGMODE_Design)
            {                
                HistoricStorage.Instance.Persist();
                AttachCenter.Instance.Unfreeze();
            }
            return VSConstants.S_OK;
        }
    }
}
