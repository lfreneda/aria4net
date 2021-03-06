using System;
using System.Diagnostics;
using Aria4net.Common;
using Aria4net.Exceptions;
using NLog;

namespace Aria4net.Server
{
// ReSharper disable InconsistentNaming
    public class Aria2cProcessStarter : ProcessStarter
// ReSharper restore InconsistentNaming
    {
        private readonly Aria2cConfig _config;

        public Aria2cProcessStarter(IFileFinder fileFinder,
                                    Aria2cConfig config,
                                    Logger logger) : base(fileFinder, logger)
        {
            _config = config;
        }

        public Func<string> DownloadedFilesDirPath { get; set; }

        public override bool IsRunning()
        {
            Process[] pname = Process.GetProcessesByName("aria2c");
            return (0 < pname.Length);
        }

        protected override void ProcessOnExited(Process sender, EventArgs eventArgs)
        {
            try
            {
                Logger.Info("Processo executou com codigo {0}.", sender.ExitCode);
                if (0 <= sender.ExitCode)
                    throw new Aria2cException(sender.ExitCode, sender.StandardError.ReadToEnd());
            }
            catch (InvalidOperationException) // Ignore if process is not running
            {
            }
        }

        protected override void ConfigureProcess(Process process)
        {
            Logger.Info("Configurando processo.");
            process.StartInfo.WindowStyle = ProcessWindowStyle.Hidden;
        }

        protected override string GetArguments()
        {
            Logger.Info("Definindo argumentos do processo.");

            return
                string.Format(
                    "--enable-rpc --dir=\"{0}\" --quiet --listen-port={1} --rpc-listen-port={2} --follow-torrent=false --file-allocation=trunc -c --show-console-readout=false --stop-with-process={3} --max-concurrent-downloads={4} --max-overall-download-limit={5} --max-overall-upload-limit={6} --auto-save-interval=1",
                    DownloadedFilesDirPath().Trim(),
                    _config.Port,
                    _config.RpcPort,
                    Process.GetCurrentProcess().Id,
                    _config.ConcurrentDownloads,
                    _config.MaxDownloadLimit,
                    _config.MaxUploadLimit);
        }
    }
}
