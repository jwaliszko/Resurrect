﻿using System;
using System.ComponentModel.Design;
using System.IO;
using System.Linq;
using System.Threading;
using EnvDTE80;
using Microsoft.Internal.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Task = System.Threading.Tasks.Task;

namespace Resurrect
{
    public class AttachCenter
    {        
        private readonly IServiceProvider _provider;
        private readonly Debugger2 _dteDebugger;
        private bool _freezed;
        private OleMenuCommand _command;

        private static AttachCenter _instance;
        private static readonly object _locker = new object();

        private AttachCenter(IServiceProvider provider, Debugger2 dteDebugger)
        {
            _provider = provider;
            _dteDebugger = dteDebugger;
            _freezed = false;
        }

        public static void Instantiate(IServiceProvider provider, Debugger2 dteDebugger)
        {
            lock (_locker)
            {
                if (_instance != null)
                    throw new ArgumentException("AttachCenter of Resurrect is already instantiated.");
                _instance = new AttachCenter(provider, dteDebugger);
                _instance.BindCommand();
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

        private void BindCommand()
        {
            // Add our command handlers for menu (commands must exist in the .vsct file)
            var mcs = _provider.GetService(typeof(IMenuCommandService)) as OleMenuCommandService;
            if (null != mcs)
            {
                // Create the command for the menu item.
                var commandId = new CommandID(GuidList.guidResurrectCmdSet, PkgCmdIDList.cmdidResurrect);
                _command = new OleMenuCommand(AttachToProcesses, commandId);
                mcs.AddCommand(_command);
            }            
        }

        private void SendPatrol()
        {
            // Run background checking.
            Task.Factory.StartNew(Patrol);
        }        

        private void Patrol()
        {
            const int delay = 500;
            while (true)    // Patrol for safety reasons and in case of multiple VS instances running, which can change debug history.
            {
                if (_freezed)
                {
                    _command.Enabled = false;
                }
                else
                {
                    _command.Enabled = HistoricStorage.Instance.IsAnyStored();
                    _command.Text = string.Format("Resurrect {0}",
                                                  string.Join(", ",
                                                              HistoricStorage.Instance.GetProcesses()
                                                                             .Select(Path.GetFileName)));
                }
                Thread.Sleep(delay);
            }
        }

        /// <summary>
        /// This function is the callback used to execute a command when the a menu item is clicked.
        /// See the Initialize method to see how the menu item is associated to this function using
        /// the OleMenuCommandService service and the MenuCommand class.
        /// </summary>
        private void AttachToProcesses(object sender, EventArgs e)
        {
            if (!_freezed && HistoricStorage.Instance.IsAnyStored())
            {
                var stored = HistoricStorage.Instance.GetProcesses();
                var processes =
                    _dteDebugger.LocalProcesses.Cast<Process2>()
                                .Where(
                                    process =>
                                    stored.Any(x => x.Equals(process.Name, StringComparison.OrdinalIgnoreCase)))
                                .ToList();
                if (processes.Count > 0)
                {
                    var unavailable =
                        stored.Except(processes.Select(x => x.Name), StringComparer.CurrentCultureIgnoreCase).ToList();
                    if (unavailable.Any())
                    {
                        if (DialogResult.OK !=
                            AskQuestion(
                                string.Format(
                                    "Some of historic processes not running: {0}. Continue to resurrect rest of them ?",
                                    string.Join(",", unavailable.Select(Path.GetFileName)))))
                        {
                            return;
                        }
                    }

                    var transport = _dteDebugger.Transports.Item("default");
                    var engines = new[] { transport.Engines.Item("managed/native") };
                    foreach (var process in processes)
                    {
                        process.Attach2(engines);
                    }
                }
                else
                {
                    ShowMessage("No historic processes found running. Sorry, too dead to resurrect anything.");
                }
            }
        }

        private void ShowMessage(string message)
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
				OLEMSGICON.OLEMSGICON_INFO,
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
                OLEMSGBUTTON.OLEMSGBUTTON_OKCANCEL,
                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST,
                OLEMSGICON.OLEMSGICON_WARNING,
                0, // false
                out pnResult);
            return pnResult;
        }
    }
}