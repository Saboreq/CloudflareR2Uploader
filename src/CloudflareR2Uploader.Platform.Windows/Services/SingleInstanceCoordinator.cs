using System;
using System.IO;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CloudflareR2Uploader.Services
{
    public enum InstanceLaunchCommand { Show = 0, Background = 1 }

    public sealed class SingleInstanceCoordinator : IDisposable
    {
        private readonly Mutex _mutex;
        private readonly string _pipeName;
        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
        private bool _ownsMutex;
        private Task _listener;
        public event EventHandler ActivationRequested;
        public bool IsFirstInstance { get; private set; }

        public SingleInstanceCoordinator(string instanceQualifier = null)
        {
            string userHash = GetCurrentUserHash();
            string qualifierHash = string.IsNullOrWhiteSpace(instanceQualifier)
                ? string.Empty
                : HashUserIdentifier(instanceQualifier);
            string qualifierSuffix = qualifierHash.Length == 0
                ? string.Empty
                : string.Concat(".", qualifierHash.AsSpan(0, 12));
            bool created;
            _mutex = new Mutex(true, @"Local\CloudflareR2Uploader." + userHash + qualifierSuffix, out created);
            _ownsMutex = created;
            if (!created)
            {
                try { _ownsMutex = _mutex.WaitOne(0); }
                catch (AbandonedMutexException) { _ownsMutex = true; }
            }
            IsFirstInstance = _ownsMutex;
            _pipeName = "CloudflareR2Uploader." + userHash + qualifierSuffix;
        }

        public void StartListening()
        {
            if (!IsFirstInstance || _listener != null) return;
            _listener = ListenAsync(_cancellation.Token);
        }

        public bool SignalFirstInstance(int timeoutMilliseconds = 1500)
        {
            try
            {
                using (NamedPipeClientStream client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out, PipeOptions.Asynchronous))
                {
                    client.Connect(timeoutMilliseconds);
                    using (StreamWriter writer = new StreamWriter(client, new UTF8Encoding(false)) { AutoFlush = true }) writer.WriteLine("SHOW");
                    return true;
                }
            }
            catch (IOException) { return false; }
            catch (TimeoutException) { return false; }
        }

        private async Task ListenAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                using (NamedPipeServerStream server = new NamedPipeServerStream(_pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous))
                {
                    try
                    {
                        await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
                        using (StreamReader reader = new StreamReader(server, Encoding.UTF8, false, 256, true))
                        {
                            string message = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                            if (string.Equals(message, "SHOW", StringComparison.Ordinal))
                            {
                                EventHandler handler = ActivationRequested;
                                if (handler != null) handler(this, EventArgs.Empty);
                            }
                        }
                    }
                    catch (OperationCanceledException) { return; }
                    catch (IOException) { if (cancellationToken.IsCancellationRequested) return; }
                }
            }
        }

        public static InstanceLaunchCommand ParseArguments(string[] arguments)
        {
            if (arguments != null)
                foreach (string argument in arguments)
                    if (string.Equals(argument, "--background", StringComparison.OrdinalIgnoreCase)) return InstanceLaunchCommand.Background;
            return InstanceLaunchCommand.Show;
        }

        internal static string HashUserIdentifier(string identifier)
        {
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(identifier ?? string.Empty));
            StringBuilder result = new StringBuilder(24);
            for (int i = 0; i < 12; i++) result.Append(hash[i].ToString("x2"));
            return result.ToString();
        }

        private static string GetCurrentUserHash()
        {
            string identity;
            try { identity = WindowsIdentity.GetCurrent().User.Value; }
            catch (Exception) { identity = Environment.UserDomainName + "\\" + Environment.UserName; }
            return HashUserIdentifier(identity);
        }

        public void Dispose()
        {
            _cancellation.Cancel();
            _cancellation.Dispose();
            if (_ownsMutex) { try { _mutex.ReleaseMutex(); } catch (ApplicationException) { } }
            _mutex.Dispose();
        }
    }
}
