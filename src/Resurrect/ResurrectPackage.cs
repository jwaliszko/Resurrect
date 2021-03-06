﻿using System.Runtime.InteropServices;
using EnvDTE80;
using EnvDTE90;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace Resurrect
{
    /// <summary>
    /// This is the class that implements the package exposed by this assembly.
    ///
    /// The minimum requirement for a class to be considered a valid package for Visual Studio
    /// is to implement the IVsPackage interface and register itself with the shell.
    /// This package uses the helper classes defined inside the Managed Package Framework (MPF)
    /// to do it: it derives from the Package class that provides the implementation of the 
    /// IVsPackage interface and uses the registration attributes defined in the framework to 
    /// register itself and its components with the shell.
    /// </summary>
    // This attribute tells the PkgDef creation utility (CreatePkgDef.exe) that this class is
    // a package.
    [PackageRegistration(UseManagedResourcesOnly = true)]
    // This attribute is used to register the information needed to show this package
    // in the Help/About dialog of Visual Studio.
    [InstalledProductRegistration("#110", "#112", "1.5", IconResourceID = 400)]
    // This attribute is needed to let the shell know that this package exposes some menus.
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [Guid(Constants.GuidResurrectPkgString)]
    [ProvideAutoLoad(UIContextGuids.SolutionExists)]
    public sealed class ResurrectPackage : Package
    {
        /// <summary>
        /// Default constructor of the package.
        /// Inside this method you can place any initialization code that does not require 
        /// any Visual Studio service because at this point the package object is created but 
        /// not sited yet inside Visual Studio environment. The place to do all the other 
        /// initialization is the Initialize method.
        /// </summary>
        public ResurrectPackage()
        {
        }

        /////////////////////////////////////////////////////////////////////////////
        // Overridden Package Implementation
        #region Package Members

        /// <summary>
        /// Initialization of the package; this method is called right after the package is sited, so this is the place
        /// where you can put all the initialization code that rely on services provided by VisualStudio.
        /// </summary>
        protected override void Initialize()
        {
            base.Initialize();

            var debugger = GetService(typeof (SVsShellDebugger)) as IVsDebugger;
            if (debugger == null)
                return;
            var dte = GetService(typeof (SDTE)) as DTE2;
            if (dte == null)
                return;
            var dteDebugger = dte.Debugger as Debugger3;
            if (dteDebugger == null)
                return;

            var outputLog = GetOutputPane(Constants.GuidOutputPane, "Resurrect");
            var statusBar = (IVsStatusbar) GetService(typeof (SVsStatusbar));
            Log.Instantiate(outputLog, statusBar);

            Storage.Instantiate(UserRegistryRoot, dte);
            Storage.Instance.SendPatrol();

            AttachCenter.Instantiate(this, dteDebugger);
            AttachCenter.Instance.SendPatrol();

            DebugEventsHunter.Instantiate(debugger);
            DebugEventsHunter.Instance.SendPatrol();
        }

        protected override void Dispose(bool disposing)
        {
            DebugEventsHunter.Instance.DismissPatrol();
            AttachCenter.Instance.DismissPatrol();
            Storage.Instance.DismissPatrol();
            base.Dispose(disposing);
        }
        #endregion
    }
}
