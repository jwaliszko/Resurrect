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
        private readonly IVsOutputWindowPane _outputLog;
        private readonly IVsStatusbar _statusBar;

        private static Log _instance;
        private static readonly object _locker = new object();

        private Log(IVsOutputWindowPane outputLog, IVsStatusbar statusBar)
        {
            _messages = new ConcurrentQueue<string>();
            _outputLog = outputLog;
            _statusBar = statusBar;
        }

        public static void Instantiate(IVsOutputWindowPane outputLog, IVsStatusbar statusBar)
        {
            lock (_locker)
            {
                if (_instance != null)
                    throw new InvalidOperationException(string.Format("{0} of Resurrect is already instantiated.", _instance.GetType().Name));
                _instance = new Log(outputLog, statusBar);
            }
        }

        public static Log Instance
        {
            get { return _instance; }
        }

        public void SetStatus(string format, params object[] args)
        {
            int frozen;
            _statusBar.IsFrozen(out frozen);

            if (frozen == 0)
                _statusBar.SetText(string.Format(format, args));
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
