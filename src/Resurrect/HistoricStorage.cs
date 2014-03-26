using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        private IList<string> _processes = new List<string>();
        private IList<Guid> _engines = new List<Guid>();
        private readonly RegistryKey _storeTarget;
        private static DTE2 _application;
        private static HistoricStorage _instance;
        private static readonly object _locker = new object();

        private HistoricStorage(RegistryKey storeTarget)
        {
            _storeTarget = storeTarget;
        }

        public static void Instantiate(RegistryKey storeTarget, DTE2 application)
        {
            lock (_locker)
            {
                if (_instance != null)
                    throw new ArgumentException("HistoricStorage of Resurrect is already instantiated.");
                _instance = new HistoricStorage(storeTarget);
                _application = application;
            }
        }

        public static HistoricStorage Instance
        {
            get
            {
                return _instance;
            }
        }

        public IEnumerable<string> GetProcesses()
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

        public IEnumerable<string> GetEngines()
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
                            return engines.Split(new[] {','});
                    }
                }
                return new string[0];
            }
        }

        private void Sanitize()
        {
            _processes = _processes.Where(x => !string.IsNullOrWhiteSpace(x))
                                   .Select(x => x.Trim().ToLowerInvariant())
                                   .Distinct()
                                   .ToList();
            _engines = _engines.Distinct().ToList();
        }

        public void SubscribeProcess(string item)
        {
            _processes.Add(item.Trim().ToLowerInvariant());
            Sanitize();
        }

        public void SubscribeEngine(Guid item)
        {
            _engines.Add(item);
            Sanitize();
        }

        public bool IsAnyStored()
        {
            using (var key = _storeTarget.OpenSubKey(KeyName))
            {
                if (key != null)
                {
                    var value = key.GetValue(KeyValue) as string;
                    return !string.IsNullOrEmpty(value);
                }
                return false;
            }
        }

        public void Persist()
        {
            if (_processes.Any() && _engines.Any())
            {
                using (var key = _storeTarget.OpenSubKey(KeyName, RegistryKeyPermissionCheck.ReadWriteSubTree) ??
                                 _storeTarget.CreateSubKey(KeyName, RegistryKeyPermissionCheck.ReadWriteSubTree))
                {
                    if (key == null)
                        throw new Exception("Resurrect could not store processes for further usage: registry problem.");

                    var value = string.Format("{0}|{1}", string.Join(",", _processes), string.Join(",", _engines));
                    key.SetValue(KeyValue, value);

                    _processes.Clear();
                    _engines.Clear();
                }
            }
        }
    }
}
