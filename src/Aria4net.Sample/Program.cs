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
            string appRoot = Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)))));

            var logger = LogManager.GetCurrentClassLogger();

            var config = new Aria2cConfig
                {
                    Executable = Path.Combine(appRoot,"tools\\aria2-1.16.3-win-32bit-build1\\aria2c.exe"),
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

            IClient client = new Aria2cJsonRpcClient(new RestClient(),
                                                     config,
                                                     watcher,
                                                     logger);

            var url1 =
                "ftp://download.warface.levelupgames.com.br/Warface/Installer/Instalador_Client_LevelUp_1.0.34.011.torrent";
            //var url2 = "http://download.levelupgames.com.br/Warface/Installer/Instalador_Patch_LevelUp_1.0.34.010.torrent";

            var gid1 = "";
            var gid2 = "";

            client.DownloadStarted += (sender, eventArgs) => Console.WriteLine("Download iniciado {0}", eventArgs.Status.Gid);
            client.DownloadProgress += (sender, e) => Console.WriteLine(
                "\r{7} Status {5} | Progress {0:N1} % | Speed {1:N2} Mb/s | Eta {2:N0} s | Downloaded {3:N2}  Mb | Remaining {6:N2} Mb | Total {4:N2} Mb",
                e.Status.Progress, 
                e.Status.DownloadSpeed.ToMegaBytes(), 
                e.Status.Eta,
                e.Status.CompletedLength.ToMegaBytes(), 
                e.Status.TotalLength.ToMegaBytes(), 
                e.Status.Status,
                (e.Status.Remaining).ToMegaBytes(),
                e.Status.Gid);
            
            client.DownloadCompleted += (sender, eventArgs) =>
                {
                    Console.WriteLine("Download concluido {0}", eventArgs.Status.Gid);
                    using (var process = new Process())
                    {
                        process.StartInfo.FileName =
                            eventArgs.Status.Files.FirstOrDefault(c => Path.GetExtension(c.Path) == ".exe").Path;
                        process.Start();
                    }
                };
            client.DownloadError += (sender, e) =>
                {
                    foreach (var file in e.Status.Files)
                    {
                        if(File.Exists(file.Path))
                        {
                            File.Delete(file.Path);
                        }
                    }
                };
            
            gid1 = client.AddTorrent(url1);
            //gid2 = client.AddTorrent(url2);

            Console.ReadKey();

            client.Shutdown();
        }
    }
}
