using System;
using System.Collections.Concurrent;
using System.Text;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace Resurrect
{
    internal sealed class Log
    {
        private readonly ConcurrentQueue<string> _messages;
        private readonly Package _provider;
        private IVsOutputWindowPane _outputLog;

        private static Log _instance;
        private static readonly object _locker = new object();

        private Log(Package provider)
        {
            _messages = new ConcurrentQueue<string>();
            _provider = provider;
            InitializeOutputLog();
        }

        public static void Instantiate(Package provider)
        {
            lock (_locker)
            {
                if (_instance != null)
                    throw new InvalidOperationException(string.Format("{0} of Resurrect is already instantiated.", _instance.GetType().Name));
                _instance = new Log(provider);
            }
        }

        public static Log Instance
        {
            get { return _instance; }
        }

        private void InitializeOutputLog()
        {
            if (_outputLog != null)
                return;
            _outputLog = _provider.GetOutputPane(Constants.GuidOutputPane, "Resurrect");
        }

        public void AppendLine(string format, params object[] args)
        {
            var sb = new StringBuilder();
            sb.AppendFormat(format, args);
            sb.AppendLine();            
            Append(sb.ToString());
        }

        public void Append(string format, params object[] args)
        {
            DoAppend(String.Format(format, args));
        }

        public void Append(string message)
        {
            DoAppend(message);
        }
        
        public void Clear()
        {
            DoClear();
        }

        private void DoClear()
        {
            InitializeOutputLog();

            while (!_messages.IsEmpty)
            {
                string dummy;
                _messages.TryDequeue(out dummy);
            }

            if (_outputLog != null)
                _outputLog.Clear();
        }

        private void DoAppend(string message)
        {
            _messages.Enqueue(message);
            ThreadHelper.Generic.BeginInvoke(ProcessMessageQueue);
        }

        private void ProcessMessageQueue()
        {
            InitializeOutputLog();
            while (!_messages.IsEmpty)
            {
                string message;
                _messages.TryDequeue(out message);
                if (String.IsNullOrEmpty(message))
                    continue;

                if (_outputLog != null)
                    _outputLog.OutputString(message);
            }
        }
    }
}
