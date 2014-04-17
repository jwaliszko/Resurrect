using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Management;
using EnvDTE80;
using EnvDTE90;
using Microsoft.Internal.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace Resurrect
{
    public class AttachCenter
    {
        private readonly IServiceProvider _provider;
        private readonly Debugger3 _dteDebugger;
        private ManagementEventWatcher _watcher;
        private bool _freezed;

        private OleMenuCommand _attachToProcessCommand;
        private OleMenuCommand _toggleAutoAttachCommand;

        private static AttachCenter _instance;
        private static readonly object _locker = new object();

        private AttachCenter(IServiceProvider provider, Debugger3 dteDebugger)
        {
            _provider = provider;
            _dteDebugger = dteDebugger;
            _freezed = false;
        }

        public static void Instantiate(IServiceProvider provider, Debugger3 dteDebugger)
        {
            lock (_locker)
            {
                if (_instance != null)
                    throw new ArgumentException(string.Format("{0} of Resurrect is already instantiated.", _instance.GetType().Name));
                _instance = new AttachCenter(provider, dteDebugger);
                _instance.BindCommands();
                _instance.SendPatrol();
            }
        }

        public static AttachCenter Instance
        {
            get { return _instance; }
        }

        public void Freeze()
        {
            _freezed = true;
            Refresh();
        }

        public void Unfreeze()
        {
            _freezed = false;
            Refresh();
        }        

        private void BindCommands()
        {
            // Add our command handlers for menu (commands must exist in the .vsct file).
            var mcs = _provider.GetService(typeof (IMenuCommandService)) as OleMenuCommandService;
            if (null == mcs)
                return;

            var commandId = new CommandID(GuidList.guidResurrectCmdSet, PkgCmdIDList.cmdidResurrect);
            _attachToProcessCommand = new OleMenuCommand(AttachToProcesses, commandId) { Enabled = false };
            mcs.AddCommand(_attachToProcessCommand);

            commandId = new CommandID(GuidList.guidResurrectCmdSet, PkgCmdIDList.cmdidAutoAttach);
            _toggleAutoAttachCommand = new OleMenuCommand(ToggleAutoAttachSetting, commandId) { Checked = false };
            mcs.AddCommand(_toggleAutoAttachCommand);
        }

        private void SendPatrol()
        {
            Refresh();
            Storage.Instance.SolutionActivated += (sender, args) => Refresh();
            Storage.Instance.SolutionDeactivated += (sender, args) => Refresh();

            _watcher = new ManagementEventWatcher("SELECT ProcessID FROM Win32_ProcessStartTrace");
            _watcher.EventArrived += ProcessStarted;
        }

        private void Refresh()
        {
            _attachToProcessCommand.Enabled = !_freezed && Storage.Instance.HistoricProcesses.Any();

            var processes = Storage.Instance.HistoricProcesses.Any()
                ? string.Join(", ", Storage.Instance.HistoricProcesses.Select(Path.GetFileName))
                : "(no targets yet)";
            const int length = 50;
            processes = processes.Length > length ? string.Format("{0}…", processes.Substring(0, length)) : processes;
            var engines = Storage.Instance.HistoricEngines.Any()
                ? string.Format(" // {0}", string.Join(", ", GetEnginesNames(Storage.Instance.HistoricEngines)))
                : string.Empty;

            _attachToProcessCommand.Text = string.Format("Resurrect: {0}{1}", processes, engines);
        }

        private IEnumerable<string> GetEnginesNames(IEnumerable<Guid> ids)
        {
            var names = new List<string>();
            var transport = _dteDebugger.Transports.Item("default");
            foreach (var id in ids)
            {
                var found = false;
                foreach (Engine engine in transport.Engines)
                {
                    if (Guid.Parse(engine.ID) == id)
                    {
                        names.Add(engine.Name);
                        found = true;
                        break;
                    }
                }
                if(!found)
                    names.Add("Unknown");
            }
            return names;
        }

        void ProcessStarted(object sender, EventArrivedEventArgs e)
        {
            var processId = e.NewEvent.Properties["ProcessID"].Value.ToString();
            var processName = GetMainModuleFilePath(int.Parse(processId));

            if (Storage.Instance.HistoricProcesses.Contains(processName))
                AttachToProcessSilently(processName);
        }

        // Allows to avoid "A 32 bit processes cannot access modules of a 64 bit process" thrown when accessing MainModule of System.Diagnostics.Process...
        private string GetMainModuleFilePath(int processId)
        {
            var wmiQueryString = string.Format("SELECT ExecutablePath FROM Win32_Process WHERE ProcessId = {0}", processId);
            using (var searcher = new ManagementObjectSearcher(wmiQueryString))
            {
                using (var results = searcher.Get())
                {
                    var mo = results.Cast<ManagementObject>().FirstOrDefault();
                    if (mo != null)
                        return (string)mo["ExecutablePath"];
                }
            }
            return null;
        }

        private void AttachToProcessSilently(string processName)
        {
            var runningProcess = _dteDebugger.LocalProcesses.Cast<Process3>().FirstOrDefault(x => x.Name.Equals(processName));
            if (runningProcess != null)
            {
                var engines = Storage.Instance.HistoricEngines.Select(x => string.Format("{{{0}}}", x)).ToList();
                PerformAttachOperation(new[] { runningProcess }, engines);
            }
        }

        private void AttachToProcesses(object sender, EventArgs e)
        {
            var runningProcesses = _dteDebugger.LocalProcesses.Cast<Process3>()
                .Where(process => Storage.Instance.HistoricProcesses.Any(x => x.Equals(process.Name))).ToList();
            if (!runningProcesses.Any())
            {
                ShowMessage("No historic processes found alive. Debug session cannot be resurrected.", OLEMSGICON.OLEMSGICON_INFO);
                return;
            }
            var missingProcesses = Storage.Instance.HistoricProcesses.Except(runningProcesses.Select(x => x.Name)).ToList();
            if (missingProcesses.Any())
            {
                if (DialogResult.Yes !=
                    AskQuestion(string.Format(
                        "Some of the historic processes not alive:\n{0}\n\nContinue to resurrect the debugging session without them?",
                        string.Join(",\n", missingProcesses.Select(proc => string.Format("    {0}", Path.GetFileName(proc)))))))
                {
                    return;
                }
            }
            var engines = Storage.Instance.HistoricEngines.Select(x => string.Format("{{{0}}}", x)).ToList();
                        
            PerformAttachOperation(runningProcesses, engines);                
        }

        private void ToggleAutoAttachSetting(object sender, EventArgs e)
        {            
            try
            {
                if (_toggleAutoAttachCommand.Checked)
                    _watcher.Stop();
                else
                    _watcher.Start();

                _toggleAutoAttachCommand.Checked = !_toggleAutoAttachCommand.Checked;
            }
            catch (ManagementException ex)
            {
                if("access denied".Equals(ex.Message.Trim(), StringComparison.OrdinalIgnoreCase))
                    ThrowElevationRequired();
                else
                    ShowMessage(string.Format("Unexpected problem: {0}. Request rejected.", ex.Message), OLEMSGICON.OLEMSGICON_CRITICAL);
            }
            catch (Exception ex)
            {
                ShowMessage(string.Format("Unexpected problem: {0}. Request rejected.", ex.Message), OLEMSGICON.OLEMSGICON_CRITICAL);
            }
        }

        private void PerformAttachOperation(IEnumerable<Process3> processes, IEnumerable<string> engines)
        {
            lock (_locker)
            {
                try
                {
                    var array = engines.ToArray();
                    foreach (var process in processes)
                    {
                        if (!process.IsBeingDebugged)
                            process.Attach2(array.Any() ? array : null); // if no specific engines, null indicates to detect one
                    }
                }
                catch (COMException ex)
                {
                    ThrowElevationRequired();
                }
                catch (Exception ex)
                {
                    ShowMessage(string.Format("Unexpected problem: {0}.", ex.Message), OLEMSGICON.OLEMSGICON_CRITICAL);
                }
            }
        }

        private void ThrowElevationRequired()
        {
            const int ERROR_ELEVATION_REQUIRED = unchecked((int)0x800702E4);
            Marshal.ThrowExceptionForHR(ERROR_ELEVATION_REQUIRED);
        }

        private void ShowMessage(string message, OLEMSGICON type)
        {
			var shell = (IVsUIShell)_provider.GetService(typeof(SVsUIShell));
			var clsid = Guid.Empty;
            int pnResult;
			
			shell.ShowMessageBox(
				0,
				ref clsid,
				"Resurrect",
				message,
				string.Empty,
				0,
				OLEMSGBUTTON.OLEMSGBUTTON_OK,
				OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST,
                type,
				0, // false
                out pnResult);
        }

        private int AskQuestion(string message)
        {
            var shell = (IVsUIShell) _provider.GetService(typeof (SVsUIShell));
            var clsid = Guid.Empty;
            int pnResult;

            shell.ShowMessageBox(
                0,
                ref clsid,
                "Resurrect",
                message,
                string.Empty,
                0,
                OLEMSGBUTTON.OLEMSGBUTTON_YESNO,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST,
                OLEMSGICON.OLEMSGICON_WARNING,
                0, // false
                out pnResult);
            return pnResult;
        }        
    }
}
