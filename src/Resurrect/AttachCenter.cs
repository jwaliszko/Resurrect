using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Management;
using EnvDTE80;
using EnvDTE90;
using Microsoft.Internal.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

namespace Resurrect
{
    public class AttachCenter
    {        
        private readonly IServiceProvider _provider;
        private readonly Debugger3 _dteDebugger;
        private ManagementEventWatcher _watcher;
        private bool _freezed;
        private OleMenuCommand _command;

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
                _instance.BindCommand(enabled: false);
                _instance.SendPatrol();
            }
        }

        public static AttachCenter Instance
        {
            get
            {
                return _instance;
            }
        }

        public void Freeze()
        {
            _freezed = true;
            _command.Enabled = false;
        }

        public void Unfreeze()
        {
            _freezed = false;
            _command.Enabled = true;
        }

        private void BindCommand(bool enabled)
        {
            // Add our command handlers for menu (commands must exist in the .vsct file).
            var mcs = _provider.GetService(typeof(IMenuCommandService)) as OleMenuCommandService;
            if (null != mcs)
            {
                // Create the command for the menu item.
                var commandId = new CommandID(GuidList.guidResurrectCmdSet, PkgCmdIDList.cmdidResurrect);
                _command = new OleMenuCommand(AttachToProcesses, commandId) {Enabled = enabled};
                mcs.AddCommand(_command);
            }            
        }

        private void SendPatrol()
        {
            HistoricStorage.Instance.SolutionActivated += (sender, args) => Refresh();
            HistoricStorage.Instance.SolutionDeactivated += (sender, args) => Refresh();

            // Run background checking.
            Task.Factory.StartNew(VisualPatrol);
            // Task.Factory.StartNew(SystemPatrol); //ToDo: uncomment when UI support will be done
        }

        private void VisualPatrol()
        {
            const int delay = 500;
            while (true)    // Patrol for safety reasons and in case of multiple VS instances running, which can change debug history.
            {
                Refresh();
                Thread.Sleep(delay);
            }
        }

        private void Refresh()
        {
            _command.Enabled = !_freezed && HistoricStorage.Instance.Processes.Any();
            _command.Text = string.Format("Resurrect {0}", string.Join(", ", HistoricStorage.Instance.Processes.Select(Path.GetFileName)));
        }

        private void SystemPatrol()
        {
            _watcher = new ManagementEventWatcher("SELECT ProcessName FROM Win32_ProcessStartTrace");
            _watcher.EventArrived += ProcessStarted;
            _watcher.Start();
        }

        void ProcessStarted(object sender, EventArrivedEventArgs e)
        {
            var historicProcesses = HistoricStorage.Instance.Processes.ToList();
            if (!historicProcesses.Any())
                return;

            var processName = e.NewEvent.Properties["ProcessName"].Value.ToString();
            if (historicProcesses.Select(Path.GetFileName).Contains(processName, StringComparer.OrdinalIgnoreCase))            
                AttachToProcesses(this, null);
        }

        /// <summary>
        /// This function is the callback used to execute a command when the a menu item is clicked.
        /// See the Initialize method to see how the menu item is associated to this function using
        /// the OleMenuCommandService service and the MenuCommand class.
        /// </summary>
        private void AttachToProcesses(object sender, EventArgs e)
        {
            if (_freezed) return;

            var historicProcesses = HistoricStorage.Instance.Processes.ToList();
            if (!historicProcesses.Any()) return;

            var processes = _dteDebugger.LocalProcesses.Cast<Process3>()
                .Where(process => historicProcesses.Any(x => x.Equals(process.Name, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (!processes.Any())
            {
                ShowMessage("No historic processes found alive. Debug session cannot be resurrected.", OLEMSGICON.OLEMSGICON_INFO);
                return;
            }

            var unavailable = historicProcesses.Except(processes.Select(x => x.Name), StringComparer.OrdinalIgnoreCase).ToList();
            if (unavailable.Any())
            {
                if (DialogResult.OK !=
                    AskQuestion(string.Format(
                        "Some of the historic processes not alive:\n{0}\n\nContinue to resurrect the debugging session without them?",
                        string.Join(",\n", unavailable.Select(proc => string.Format("    {0}", Path.GetFileName(proc)))))))
                {
                    return;
                }
            }

            var engines = new List<Engine>();
            var transport = _dteDebugger.Transports.Item("default");
            var historicEngines = HistoricStorage.Instance.Engines.ToList();
            foreach (Engine engine in transport.Engines)
            {
                foreach (var id in historicEngines)
                {
                    if (Guid.Parse(engine.ID) == id)
                    {
                        engines.Add(engine);
                        break;
                    }
                }
            }

            PerformAttachOperation(processes, engines, transport);
        }

        private void PerformAttachOperation(IList<Process3> processes, IList<Engine> engines, Transport transport)
        {
            try
            {
                foreach (var process in processes)
                {
                    process.Attach2(engines.Any() ? engines.ToArray() : new[] {transport.Engines.Item("Managed/Native")});
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
