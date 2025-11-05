using System;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Json;

namespace SqlServerMcpServer
{
    internal sealed class StderrJsonSink : ILogEventSink
    {
        private readonly JsonFormatter _formatter;

        public StderrJsonSink(bool renderMessage = false)
        {
            _formatter = new JsonFormatter(renderMessage: renderMessage);
        }

        public void Emit(LogEvent logEvent)
        {
            try
            {
                _formatter.Format(logEvent, Console.Error);
            }
            catch
            {
                // Swallow logging errors to avoid impacting runtime
            }
        }
    }
}