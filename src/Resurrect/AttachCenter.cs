﻿using System;
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
    internal sealed class AttachCenter
    {
        private readonly IServiceProvider _provider;
        private readonly Debugger3 _dteDebugger;
        private readonly ManagementEventWatcher _watcher;
        private bool _freezed;

        private static AttachCenter _instance;
        private static readonly object _locker = new object();        

        private AttachCenter(IServiceProvider provider, Debugger3 dteDebugger)
        {
            _provider = provider;
            _dteDebugger = dteDebugger;
            _watcher = new ManagementEventWatcher("SELECT ProcessID FROM Win32_ProcessStartTrace");

            BindCommands();
        }

        public static void Instantiate(IServiceProvider provider, Debugger3 dteDebugger)
        {
            lock (_locker)
            {
                if (_instance != null)
                    throw new InvalidOperationException(string.Format("{0} of Resurrect is already instantiated.", _instance.GetType().Name));
                _instance = new AttachCenter(provider, dteDebugger);
            }
        }

        public static AttachCenter Instance
        {
            get { return _instance; }
        }

        private void BindCommands()
        {
            // Add our command handlers for menu (commands must exist in the .vsct file).
            var mcs = _provider.GetService(typeof (IMenuCommandService)) as OleMenuCommandService;
            if (null == mcs)
                return;

            var commandId = new CommandID(Constants.GuidResurrectCmdSet, Constants.CmdidResurrect);
            var attachToProcessCommand = new OleMenuCommand(AttachToProcesses, commandId) {Enabled = false};
            attachToProcessCommand.BeforeQueryStatus += (s, e) => RefreshStatus((OleMenuCommand) s);
            mcs.AddCommand(attachToProcessCommand);

            commandId = new CommandID(Constants.GuidResurrectCmdSet, Constants.CmdidAutoAttach);
            var toggleAutoAttachCommand = new MenuCommand(ToggleAutoAttachSetting, commandId) {Checked = false};
            mcs.AddCommand(toggleAutoAttachCommand);
        }

        public void SendPatrol()
        {
            Storage.Instance.SolutionActivated += SolutionActivated;
            Storage.Instance.SolutionDeactivated += SolutionDeactivated;
            _watcher.EventArrived += ProcessStarted;
        }        

        public void DismissPatrol()
        {
            Storage.Instance.SolutionActivated -= SolutionActivated;
            Storage.Instance.SolutionDeactivated -= SolutionDeactivated;
            _watcher.EventArrived -= ProcessStarted;
            _watcher.Stop();
        }

        private void SolutionActivated(object sender, EventArgs e)
        {
            PrintProcesses(Storage.Instance.HistoricProcesses, "loaded");
        }

        private void SolutionDeactivated(object sender, EventArgs e)
        {
            Log.Instance.Clear();
        }        

        public void Freeze()
        {
            // Freeze only if all historic processes are attached.
            _freezed = !Storage.Instance.HistoricProcesses.Select(x => x.ProcessName)
                .Except(Storage.Instance.SessionProcesses.Select(x => x.ProcessName))
                .Any();
        }

        public void Unfreeze()
        {
            _freezed = false;
            PrintProcesses(Storage.Instance.SessionProcesses, "saved");
        }

        void ProcessStarted(object sender, EventArrivedEventArgs e)
        {
            var processId = e.NewEvent.Properties["ProcessID"].Value.ToString();
            var processName = GetMainModuleFilePath(int.Parse(processId));

            if (Storage.Instance.HistoricProcesses.Any(x => x.ProcessName.Equals(processName)))
                AttachToProcessSilently(processName);
        }

        private void PrintProcesses(IEnumerable<AttachData> processes, string message)
        {
            foreach (var process in processes)
            {
                var engines = process.DebugEngines.Select(x => string.Format("{{{0}}}", x)).ToArray();
                var shortName = Path.GetFileName(process.ProcessName);
                var enginesNames = string.Join(", ", GetEnginesNames(engines));

                Log.Instance.AppendLine("[{0}] {1} / {2}", message, shortName, enginesNames);
            }
        }

        private void RefreshStatus(OleMenuCommand command)
        {
            command.Enabled = !_freezed && Storage.Instance.HistoricProcesses.Any();

            var processes = Storage.Instance.HistoricProcesses.Any()
                ? string.Join(", ", Storage.Instance.HistoricProcesses.Select(x => x.ShortProcessName))
                : "(no targets yet)";
            const int length = 50;
            processes = processes.Length > length
                ? string.Format("{0}...", processes.Substring(0, 50))
                : processes;
            
            command.Text = string.Format("Resurrect: {0}", processes);
        }

        private IEnumerable<string> GetEnginesNames(IEnumerable<string> ids)
        {
            return GetEnginesNames(ids.Select(Guid.Parse));
        }

        public IEnumerable<string> GetEnginesNames(IEnumerable<Guid> ids)
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

        // Allows to avoid "A 32 bit processes cannot access modules of a 64 bit process" thrown when accessing MainModule of System.Diagnostics.Process...
        private string GetMainModuleFilePath(int processId)
        {
            var wmiQueryString = string.Format("SELECT ExecutablePath FROM Win32_Process WHERE ProcessID = {0}", processId);
            using (var searcher = new ManagementObjectSearcher(wmiQueryString))
            {
                using (var results = searcher.Get())
                {
                    var mo = results.Cast<ManagementObject>().FirstOrDefault();
                    return mo != null ? (string) mo["ExecutablePath"] : null;
                }
            }
        }

        private void AttachToProcessSilently(string processName)
        {
            var runningProcess = _dteDebugger.LocalProcesses.Cast<Process3>().FirstOrDefault(x => x.Name.Equals(processName));
            if (runningProcess == null)
                return;

            PerformAttachOperation(new[] {runningProcess});
        }

        private void AttachToProcesses(object sender, EventArgs e)
        {
            var runningProcesses = _dteDebugger.LocalProcesses.Cast<Process3>()
                .Where(process => Storage.Instance.HistoricProcesses.Any(x => x.ProcessName.Equals(process.Name)))
                .ToList();
            if (!runningProcesses.Any())
            {
                ShowMessage("No historic processes found. Debug session cannot be resurrected.", OLEMSGICON.OLEMSGICON_INFO);
                return;
            }

            var missingProcesses = Storage.Instance.HistoricProcesses
                .Select(x => x.ProcessName).Except(runningProcesses.Select(x => x.Name))
                .ToList();
            if (missingProcesses.Any())
            {
                if (DialogResult.Yes !=
                    AskQuestion(string.Format(
                        "Some of the historic processes not found:\n{0}\n\nContinue to resurrect the debugging session without them?",
                        string.Join(",\n", missingProcesses.Select(proc => string.Format("    {0}", Path.GetFileName(proc)))))))
                {
                    return;
                }
            }

            PerformAttachOperation(runningProcesses);
        }

        private void ToggleAutoAttachSetting(object sender, EventArgs e)
        {            
            try
            {
                var command = (MenuCommand) sender;
                if (command.Checked)
                    _watcher.Stop();
                else
                    _watcher.Start();

                command.Checked = !command.Checked;
            }
            catch (ManagementException ex)
            {
                switch (ex.ErrorCode)
                {
                    case ManagementStatus.AccessDenied:
                        ThrowElevationRequired();
                        break;
                    default:
                        ShowMessage(ex.Message, OLEMSGICON.OLEMSGICON_CRITICAL);
                        break;
                }
            }
            catch (Exception ex)
            {
                ShowMessage(ex.Message, OLEMSGICON.OLEMSGICON_CRITICAL);
            }
        }

        private void PerformAttachOperation(IEnumerable<Process3> processes)
        {
            foreach (var process in processes)
            {
                try
                {
                    if (!process.IsBeingDebugged)
                    {
                        var engines = Storage.Instance.HistoricProcesses
                            .Single(x => x.ProcessName.Equals(process.Name))
                            .DebugEngines.Select(x => string.Format("{{{0}}}", x))
                            .ToArray();

                        process.Attach2(engines.Any() ? engines : null); // If no specific engines provided, detect appropriate one.
                    }
                }
                catch (COMException)
                {
                    // HRESULTs are COM's weakness, error codes don't scale well so we get a diagnostic for WHAT couldn't be done, not for WHY it couldn't be done.
                    // We can try to detect the simplest (not authorized) source of the failure on our own...
                    if (!SecurityGuard.HasAdminRights) 
                        ThrowElevationRequired();
                    else
                        ShowMessage(string.Format(
                            "Unable to attach to the process {0}. A debugger can be already attached (otherwise, unexpected problem has just occurred).",
                            Path.GetFileName(process.Name)), OLEMSGICON.OLEMSGICON_CRITICAL);
                }
                catch (Exception ex)
                {
                    ShowMessage(ex.Message, OLEMSGICON.OLEMSGICON_CRITICAL);
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
