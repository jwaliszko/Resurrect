using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
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
        private IList<AttachData> _historicProcesses;
        private IList<AttachData> _sessionProcesses;

        private static Storage _instance;        
        private static readonly object _locker = new object();

        private Storage(RegistryKey storeTarget, DTE2 application)
        {
            _storeTarget = storeTarget;
            _application = application;
            _solutionEvents = application.Events.SolutionEvents;
            _historicProcesses = new List<AttachData>();
            _sessionProcesses = new List<AttachData>();
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

        public IEnumerable<AttachData> HistoricProcesses
        {
            get { return _historicProcesses; }
        }

        public IEnumerable<AttachData> SessionProcesses
        {
            get { return _sessionProcesses; }
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
            _historicProcesses = GetProcesses();
        }

        void SolutionClosed()
        {
            _historicProcesses.Clear();
        }

        private IList<AttachData> GetProcesses()
        {
            using (var key = _storeTarget.OpenSubKey(KeyName))
            {
                var result = new List<AttachData>();

                if (key == null)
                    return result;

                var value = key.GetValue(KeyValue) as string;
                if (value == null)
                    return result;

                var sets = value.Split(new[] {';'}); // proc1|eng1;proc2|eng1,eng2;
                foreach (var set in sets)
                {
                    var items = set.Split(new[] {'|'});
                    if (items.Any())
                    {
                        var processes = items.First().Split(new[] {','}); // backward compatibility proc1,proc2|eng1
                        var engines = items.Last().Split(new[] {','});
                        foreach (var process in processes)
                        {
                            result.Add(new AttachData {ProcessName = process, DebugEngines = engines.Select(Guid.Parse).ToList()});
                        }
                    }
                }
                return result;
            }
        }

        public void SubscribeProcess(string process)
        {
            lock (_locker)
            {
                if (!_sessionProcesses.Any(x => x.ProcessName.Equals(process)))
                    _sessionProcesses.Add(new AttachData {ProcessName = process});
            }
        }

        public void SubscribeEngine(string process, Guid engine)
        {
            lock (_locker)
            {
                var data = _sessionProcesses.Single(x => x.ProcessName.Equals(process));
                if(!data.DebugEngines.Contains(engine))
                    data.DebugEngines.Add(engine);
            }
        }

        public void Persist()
        {
            if (!_sessionProcesses.Any()) 
                return;
             
            using (var key = _storeTarget.OpenSubKey(KeyName, RegistryKeyPermissionCheck.ReadWriteSubTree) ??
                             _storeTarget.CreateSubKey(KeyName, RegistryKeyPermissionCheck.ReadWriteSubTree))
            {
                if (key == null)
                    throw new InvalidOperationException("Resurrect could not store processes for further usage - registry problem.");

                var value = new StringBuilder();
                foreach (var sessionProcess in _sessionProcesses)
                {
                    value.Append(string.Format("{0}|{1};", sessionProcess.ProcessName, string.Join(",", sessionProcess.DebugEngines)));
                }
                value.Length--; // remove last ';'
                key.SetValue(KeyValue, value);

                _historicProcesses.Clear();
                foreach (var sessionProcess in _sessionProcesses)
                {
                    _historicProcesses.Add(new AttachData(sessionProcess));
                }
                _sessionProcesses.Clear();
            }
        }
    }
}
