#nullable enable
using System;
using System.Diagnostics;
using System.ServiceProcess;
using System.Threading;
using System.Threading.Tasks;

namespace MQTTToSQLServer
{
    public class MqttToSqlWindowsService : ServiceBase
    {
        private CancellationTokenSource? _cts;
        private Task? _serviceTask;

        public const string ServiceNameString = "MQTTToSQLServer";

        public MqttToSqlWindowsService()
        {
            ServiceName = ServiceNameString;
            CanStop = true;
            CanShutdown = true;
            AutoLog = true;
        }

        protected override void OnStart(string[] args)
        {
            _cts = new CancellationTokenSource();
            _serviceTask = Task.Run(() => Program.StartServiceAsync(_cts.Token));
        }

        protected override void OnStop()
        {
            try
            {
                _cts?.Cancel();
                Program.StopServiceAsync().GetAwaiter().GetResult();
                _serviceTask?.Wait(TimeSpan.FromSeconds(10));
            }
            catch (Exception ex)
            {
                Trace.TraceError($"Error stopping service: {ex}");
            }
            finally
            {
                _cts?.Dispose();
            }
        }

        protected override void OnShutdown()
        {
            OnStop();
        }
    }
}
