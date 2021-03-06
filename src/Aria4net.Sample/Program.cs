﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Aria4net.Client;
using Aria4net.Common;
using Aria4net.Server;
using Aria4net.Server.Validation;
using Aria4net.Server.Watcher;
using NLog;
using RestSharp;

namespace Aria4net.Sample
{
    class Program
    {
        private static void Main(string[] args)
        {
            string appRoot =
                Path.GetDirectoryName(
                    Path.GetDirectoryName(
                        Path.GetDirectoryName(
                            Path.GetDirectoryName(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)))));

            var logger = LogManager.GetCurrentClassLogger();

            var config = new Aria2cConfig
                {
                    Executable = Path.Combine(appRoot, "tools\\aria2-1.16.3-win-32bit-build1\\aria2c.exe"),
                    Id = "downloader-session"

                };

            var watcher = new Aria2cWebSocketWatcher(config,
                                                     logger);

            IServer server = new Aria2cServer(
                new Aria2cProcessStarter(
                    new Aria2cFinder(config, new DefaultPathFormatter(new WindowsPathTokenizer())),
                    config,
                    logger)
                    {
                        DownloadedFilesDirPath = () => "c:\\temp"
                    },
                new DefaultValidationRunner(),
                config,
                logger,
                watcher);

            server.Start();

            IClient client = new Aria2cJsonRpcClient(config,
                                                     watcher,
                                                     logger);
            
            client.ChangeDestinationPath(@"C:\temp\torrents");

            var url1 =
                "http://download.warface.levelupgames.com.br/Warface/Installer/Instalador_Client_LevelUp_1.0.34.006.torrent";

            var gid1 = "";

            client.DownloadCompleted +=
                (sender, eventArgs) => Console.WriteLine("Download concluido {0}", eventArgs.Status.Gid);

            client.DownloadProgress += (o, e) => Console.WriteLine(
                "\r{7} Status {5} | Progress {0:N1} % | Speed {1:N2} Mb/s | Eta {2:N0} s | Downloaded {3:N2}  Mb | Remaining {6:N2} Mb | Total {4:N2} Mb",
                e.Status.Progress,
                e.Status.DownloadSpeed.ToMegaBytes(),
                e.Status.Eta,
                e.Status.CompletedLength.ToMegaBytes(),
                e.Status.TotalLength.ToMegaBytes(),
                e.Status.Status,
                (e.Status.Remaining).ToMegaBytes(),
                e.Status.Gid);

            gid1 = client.AddTorrent(url1, new[] {1});

            Console.ReadKey();

            client.Shutdown();
        }
    }
}
