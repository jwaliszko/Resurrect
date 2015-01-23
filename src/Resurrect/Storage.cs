using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EnvDTE;
using EnvDTE80;
using Microsoft.Win32;

namespace Resurrect
{
    internal sealed class Storage
    {
        private const string KeyName = "Resurrect";
        private string KeyValue
        {
            get { return Path.GetFileName(_application.Solution.FullName); }
        }

        private readonly SolutionEvents _solutionEvents;
        private readonly RegistryKey _storeTarget;
        private readonly DTE2 _application;
        private List<string> _historicProcesses;
        private List<Guid> _historicEngines;
        private List<string> _sessionProcesses;
        private List<Guid> _sessionEngines;
        private static Storage _instance;        
        private static readonly object _locker = new object();

        private Storage(RegistryKey storeTarget, DTE2 application)
        {
            _storeTarget = storeTarget;
            _application = application;
            _solutionEvents = application.Events.SolutionEvents;
            _historicProcesses = _sessionProcesses = new List<string>();
            _historicEngines = _sessionEngines = new List<Guid>();
        }

        public static void Instantiate(RegistryKey storeTarget, DTE2 application)
        {
            lock (_locker)
            {
                if (_instance != null)
                    throw new InvalidOperationException(string.Format("{0} of Resurrect is already instantiated.", _instance.GetType().Name));
                _instance = new Storage(storeTarget, application);
            }
        }

        public static Storage Instance
        {
            get { return _instance; }
        }

        public IEnumerable<string> HistoricProcesses
        {
            get { return _historicProcesses; }
        }

        public IEnumerable<Guid> HistoricEngines
        {
            get { return _historicEngines; }
        }

        public IEnumerable<string> SessionProcesses
        {
            get { return _sessionProcesses; }
        }

        public IEnumerable<Guid> SessionEngines
        {
            get { return _sessionEngines; }
        }

        public void SendPatrol()
        {
            _solutionEvents.Opened += SolutionOpened;
            _solutionEvents.AfterClosing += SolutionClosed;
        }

        public void DismissPatrol()
        {
            _solutionEvents.Opened -= SolutionOpened;
            _solutionEvents.AfterClosing -= SolutionClosed;
        }

        void SolutionOpened()
        {
            _historicProcesses = GetProcesses().ToList();
            _historicEngines = GetEngines().ToList();
        }        

        void SolutionClosed()
        {
            _historicProcesses.Clear();
            _historicEngines.Clear();
        }

        private IEnumerable<string> GetProcesses()
        {
            using (var key = _storeTarget.OpenSubKey(KeyName))
            {
                if (key == null) 
                    return new string[0];

                var value = key.GetValue(KeyValue) as string;
                if (value == null) 
                    return new string[0];
                
                var processes = value.Split(new[] {'|'}).FirstOrDefault();
                return !string.IsNullOrEmpty(processes) ? processes.Split(new[] {','}) : new string[0];
            }
        }

        private IEnumerable<Guid> GetEngines()
        {
            using (var key = _storeTarget.OpenSubKey(KeyName))
            {
                if (key == null) 
                    return new Guid[0];
                
                var value = key.GetValue(KeyValue) as string;
                if (value == null) 
                    return new Guid[0];
                
                var engines = value.Split(new[] {'|'}).Skip(1).FirstOrDefault();
                return !string.IsNullOrEmpty(engines) ? engines.Split(new[] {','}).Select(Guid.Parse) : new Guid[0];
            }
        }

        private void Sanitize()
        {
            _sessionProcesses = _sessionProcesses.Distinct().ToList();
            _sessionEngines = _sessionEngines.Distinct().ToList();
        }

        public void SubscribeProcess(string item)
        {
            _sessionProcesses.Add(item);
            Sanitize();
        }

        public void SubscribeEngine(Guid item)
        {
            _sessionEngines.Add(item);
            Sanitize();
        }

        public void Persist()
        {
            if (!_sessionProcesses.Any() || !_sessionEngines.Any()) return;
            
            using (var key = _storeTarget.OpenSubKey(KeyName, RegistryKeyPermissionCheck.ReadWriteSubTree) ??
                             _storeTarget.CreateSubKey(KeyName, RegistryKeyPermissionCheck.ReadWriteSubTree))
            {
                if (key == null)
                    throw new InvalidOperationException("Resurrect could not store processes for further usage - registry problem.");

                var value = string.Format("{0}|{1}", string.Join(",", _sessionProcesses), string.Join(",", _sessionEngines));
                key.SetValue(KeyValue, value);

                _historicProcesses = _sessionProcesses.ToList();
                _historicEngines = _sessionEngines.ToList();

                _sessionProcesses.Clear();
                _sessionEngines.Clear();
            }
        }
    }
}
