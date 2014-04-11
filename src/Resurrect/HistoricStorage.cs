using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EnvDTE;
using EnvDTE80;
using Microsoft.Win32;

namespace Resurrect
{
    public class HistoricStorage
    {
        private const string KeyName = "Resurrect";
        private string KeyValue
        {
            get { return Path.GetFileName(_application.Solution.FullName); }
        }        

        private IList<string> _sessionProcesses = new List<string>();
        private IList<Guid> _sessionEngines = new List<Guid>();
        private readonly SolutionEvents _solutionEvents;
        private readonly RegistryKey _storeTarget;
        private readonly DTE2 _application;
        private static HistoricStorage _instance;        
        private static readonly object _locker = new object();

        public EventHandler<EventArgs> SolutionActivated;
        public EventHandler<EventArgs> SolutionDeactivated;

        private HistoricStorage(RegistryKey storeTarget, DTE2 application)
        {
            _storeTarget = storeTarget;
            _application = application;

            Processes = new string[0];
            Engines = new Guid[0];

            _solutionEvents = application.Events.SolutionEvents;
            _solutionEvents.Opened += SolutionOpened;
            _solutionEvents.AfterClosing += SolutionClosed; 
        }

        void SolutionOpened()
        {
            Processes = GetProcesses();
            Engines = GetEngines();
            OnSolutionOpened();
        }        

        void SolutionClosed()
        {
            Processes = new string[0];
            Engines = new Guid[0];
            OnSolutionClosed();
        }

        private void OnSolutionOpened()
        {
            if (SolutionActivated != null)
                SolutionActivated(this, null);
        }

        private void OnSolutionClosed()
        {
            if (SolutionDeactivated != null)
                SolutionDeactivated(this, null);
        }

        public static void Instantiate(RegistryKey storeTarget, DTE2 application)
        {
            lock (_locker)
            {
                if (_instance != null)
                    throw new ArgumentException(string.Format("{0} of Resurrect is already instantiated.", _instance.GetType().Name));
                _instance = new HistoricStorage(storeTarget, application);                               
            }
        }

        public static HistoricStorage Instance
        {
            get
            {
                return _instance;
            }
        }

        public IEnumerable<string> Processes { get; private set; }
        public IEnumerable<Guid> Engines { get; private set; }

        private IEnumerable<string> GetProcesses()
        {
            using (var key = _storeTarget.OpenSubKey(KeyName))
            {
                if (key != null)
                {
                    var value = key.GetValue(KeyValue) as string;
                    if (value != null)
                    {
                        var processes = value.Split(new[] {'|'}).FirstOrDefault();
                        if (!string.IsNullOrEmpty(processes))
                            return processes.Split(new[] {','});
                    }
                }
                return new string[0];
            }
        }

        private IEnumerable<Guid> GetEngines()
        {
            using (var key = _storeTarget.OpenSubKey(KeyName))
            {
                if (key != null)
                {
                    var value = key.GetValue(KeyValue) as string;
                    if (value != null)
                    {
                        var engines = value.Split(new[] {'|'}).Skip(1).FirstOrDefault();
                        if (!string.IsNullOrEmpty(engines))
                            return engines.Split(new[] {','}).Select(Guid.Parse);
                    }
                }
                return new Guid[0];
            }
        }

        private void Sanitize()
        {
            _sessionProcesses = _sessionProcesses.Where(x => !string.IsNullOrWhiteSpace(x))
                                   .Select(x => x.Trim().ToLowerInvariant())
                                   .Distinct().ToList();
            _sessionEngines = _sessionEngines.Distinct().ToList();
        }

        public void SubscribeProcess(string item)
        {
            _sessionProcesses.Add(item.Trim().ToLowerInvariant());
            Sanitize();
        }

        public void SubscribeEngine(Guid item)
        {
            _sessionEngines.Add(item);
            Sanitize();
        }

        public void Persist()
        {
            if (_sessionProcesses.Any() && _sessionEngines.Any())
            {
                using (var key = _storeTarget.OpenSubKey(KeyName, RegistryKeyPermissionCheck.ReadWriteSubTree) ??
                                 _storeTarget.CreateSubKey(KeyName, RegistryKeyPermissionCheck.ReadWriteSubTree))
                {
                    if (key == null)
                        throw new Exception("Resurrect could not store processes for further usage: registry problem.");

                    var value = string.Format("{0}|{1}", string.Join(",", _sessionProcesses), string.Join(",", _sessionEngines));
                    key.SetValue(KeyValue, value);

                    _sessionProcesses.Clear();
                    _sessionEngines.Clear();
                }

                Processes = GetProcesses();
                Engines = GetEngines();
            }
        }
    }
}
