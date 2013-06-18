﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Reactive.Concurrency;
using System.Threading;
using Aria4net.Common;
using Aria4net.Exceptions;
using Aria4net.Server;
using Aria4net.Server.Watcher;
using NLog;
using Newtonsoft.Json;
using RestSharp;
using RestSharp.Serializers;

namespace Aria4net.Client
{
    public class Aria2cJsonRpcClient : IClient
    {
        private readonly IRestClient _restClient;
        private readonly Aria2cConfig _config;
        private readonly IDictionary<string,Aria2cResult<string>> _history;
        private readonly IServerWatcher _watcher;
        private readonly Logger _logger;

        public Aria2cJsonRpcClient(IRestClient restClient,
                                   Aria2cConfig config,
                                   IDictionary<string, Aria2cResult<string>> history, 
                                   IServerWatcher watcher,
                                   Logger logger)
        {
            _restClient = restClient;
            _config = config;
            _history = history;
            _watcher = watcher;
            _logger = logger;
        }

        public virtual string AddUrl(string url)
        {
            _logger.Info("Adicionando url {0}", url);

            string newGid = string.Empty;
            Aria2cDownloadStatus status = null;
            IDisposable subject = null;

            subject = _watcher.Subscribe(() => newGid,
                                         gid => new Aria2cClientEventArgs
                                             {
                                                 Url = url,
                                                 Status = GetStatus(gid)
                                             },
                                         args =>
                                             {
                                                 var progress = GetProgress(args.Status.Gid);
                                                 args.Status.DownloadSpeed = progress.DownloadSpeed;
                                                 args.Status.CompletedLength = progress.CompletedLength;
                                                 args.Status.Status = progress.Status;
                                                 args.Status.TotalLength = progress.TotalLength;
                                                 return args;
                                             },
                                         args =>
                                             {
                                                 _logger.Info("Download da url {0} com gid {1} iniciado", args.Url,
                                                              args.Status.Gid);

                                                 if (null != DownloadStarted) DownloadStarted.Invoke(this, args);
                                             },
                                         args => DownloadProgress(this, args),
                                         args =>
                                             {
                                                 _logger.Info("Download da url {0} com gid {1} concluido.", args.Url,
                                                              args.Status.Gid);
                                                 _history.Remove(url);

                                                 if (null != subject) subject.Dispose();

                                                 if (null != DownloadCompleted)
                                                     DownloadCompleted(this, args);
                                             },
                                         args =>
                                             {
                                                 Remove(args.Status.Gid);

                                                 _logger.Error("Download da url {0} com gid {1} com erro.", args.Url,
                                                               args.Status.Gid);

                                                 if (null != DownloadError)
                                                     DownloadError(this, args);
                                             },
                                         args =>
                                             {
                                                 _history.Remove(args.Url);
                                                 _logger.Info("Download da url {0} com gid {1} parado e removido.",
                                                              args.Url, args.Status.Gid);

                                                 if (null != DownloadStoped)
                                                     DownloadStoped(this,
                                                                    new Aria2cClientEventArgs
                                                                        {
                                                                            Url = url,
                                                                            Status = status
                                                                        });
                                             },
                                         args =>
                                             {
                                                 _logger.Info("Download da url {0} com gid {1} pausado.", args.Url,
                                                              args.Status.Gid);

                                                 if (null != DownloadPaused)
                                                     DownloadPaused(this, args);
                                             });

            IRestResponse response = _restClient.Execute(CreateRequest("aria2.addUri", new List<string[]>
                {
                    new[] {url}
                }));

            var result = Newtonsoft.Json.JsonConvert.DeserializeObject<Aria2cResult<string>>(response.Content);

            if (null != result.Error) throw new Aria2cException(result.Error.Code, result.Error.Message);

            newGid = result.Result;

            _history.Add(url, result);

            return newGid;
        }

        public virtual string AddTorrent(string url)
        {
            string newGid = string.Empty;
            Aria2cDownloadStatus status = null;
            var keys = new Queue<Guid>();

            keys.Enqueue(_watcher.Subscribe("aria2.onDownloadError", gid =>
                {
                    if (gid != newGid) return;

                    status = GetStatus(gid);

                    _logger.Info("Download da url {0} com gid {1} com erro. Código {2}", url, gid, status.ErrorCode);

                    Remove(gid);

                    if (null != DownloadError)
                        DownloadError(this, new Aria2cClientEventArgs
                            {
                                Status = status,
                                Url = url
                            });
                }));

            keys.Enqueue(_watcher.Subscribe("aria2.onDownloadComplete", gid =>
                {
                    if (gid != newGid) return;

                    status =  GetStatus(gid);
                    _logger.Info("Download do arquivo torrent url {0} com gid {1} concluido.", url, gid);

                    string torrentPath = status.Files.FirstOrDefault().Path;

                    _history.Remove(url);

                    Remove(gid);
                    _watcher.Unsubscribe(keys);

                    AddTorrent(GetTorrent(torrentPath),torrentPath);
                }));

            IRestResponse response = _restClient.Execute(CreateRequest("aria2.addUri", new List<string[]>
                {
                    new[] {url}
                }));

            var result = Newtonsoft.Json.JsonConvert.DeserializeObject<Aria2cResult<string>>(response.Content);

            if (null != result.Error) throw new Aria2cException(result.Error.Code, result.Error.Message);

            newGid = result.Result;
            _history.Add(url, result);

            return newGid;
        }

        public virtual string AddTorrent(byte[] torrent, string path)
        {
            string newGid = string.Empty;
            Aria2cDownloadStatus status = null;
            var keys = new Queue<Guid>();
            IDisposable subject = null;

            //keys.Enqueue(_watcher.Subscribe("aria2.onDownloadError", gid =>
            //{
            //    if (gid != newGid) return;

            //    status = GetStatus(newGid);

            //    Remove(newGid);

            //    _logger.Error( "Download da url {0} com gid {1} com erro.", path, gid);

            //    if (null != DownloadError)
            //        DownloadError(this, new Aria2cClientEventArgs
            //        {
            //            Status = status,
            //            Url = path
            //        });
            //}));

            //keys.Enqueue(_watcher.Subscribe("aria2.onDownloadPause", gid =>
            //{
            //    if (gid != newGid) return;

            //    status = GetStatus(newGid);

            //    _logger.Info("Download da url {0} com gid {1} pausado.", path, gid);

            //    if (null != DownloadPaused)
            //        DownloadPaused(this, new Aria2cClientEventArgs
            //        {
            //            Status = status,
            //            Url = path
            //        });
            //}));

            //keys.Enqueue(_watcher.Subscribe("aria2.onDownloadStop", gid =>
            //{
            //    if (gid != newGid) return;

            //    status = GetStatus(newGid);

            //    _history.Remove(path);

            //    _logger.Info("Download da url {0} com gid {1} parado e removido.", path, gid);

            //    if (null != DownloadStoped)
            //        DownloadStoped(this, new Aria2cClientEventArgs
            //        {
            //            Status = status,
            //            Url = path
            //        });
            //}));

            //keys.Enqueue(_watcher.Subscribe("aria2.onDownloadStart", gid =>
            //{
            //    if (gid != newGid) return;

            //    status = GetStatus(gid);

            //    _logger.Info("Download da url {0} com gid {1} iniciado.", path, gid);
            //    var eventArgs =
            //        new Aria2cClientEventArgs
            //        {
            //            Status = status,
            //            Url = path
            //        };

            //    if (null != DownloadStarted) DownloadStarted.Invoke(this, eventArgs);

            //    StartReportingProgress(eventArgs);
            //}));

            //keys.Enqueue(_watcher.Subscribe("aria2.onBtDownloadComplete", gid =>
            //{
            //    if (gid != newGid) return;

            //    status = GetStatus(newGid);

            //    _logger.Info("Download da url {0} com gid {1} concluido.", path, gid);

            //    _history.Remove(path);

            //    _watcher.Unsubscribe(keys);

            //    if (null != DownloadCompleted)
            //        DownloadCompleted(this, new Aria2cClientEventArgs
            //        {
            //            Status = status,
            //            Url = path
            //        });
            //}));

            

            IRestResponse response =
                _restClient.Execute(CreateRequest("aria2.addTorrent", new[] {Convert.ToBase64String(torrent)}));

            var result = Newtonsoft.Json.JsonConvert.DeserializeObject<Aria2cResult<string>>(response.Content);

            if (null != result.Error) throw new Aria2cException(result.Error.Code, result.Error.Message);

            newGid = result.Result;

            _history.Add(path, result);

            return newGid;
        }

        public string Purge()
        {
            _logger.Info("Limpando downloads completos / com erro / removidos.");
            IRestResponse response = _restClient.Execute(CreateRequest<string>("aria2.purgeDownloadResult"));

            var result = Newtonsoft.Json.JsonConvert.DeserializeObject<Aria2cResult<string>>(response.Content);

            return result.Result;
        }

        public string Shutdown()
        {
            _logger.Info("Solicitando shutdown do servidor.");
            IRestResponse response = _restClient.Execute(CreateRequest<string>("aria2.forceShutdown"));

            var result = Newtonsoft.Json.JsonConvert.DeserializeObject<Aria2cResult<string>>(response.Content);

            if (null != result.Error) throw new Aria2cException(result.Error.Code, result.Error.Message);

            return result.Result;    
        }

        public virtual Aria2cDownloadStatus GetStatus(string gid)
        {
            _logger.Info("Recuperando status de {0}.", gid);
            IRestResponse response = _restClient.Execute(CreateRequest("aria2.tellStatus", new[] { gid }));

            var result = Newtonsoft.Json.JsonConvert.DeserializeObject<Aria2cResult<Aria2cDownloadStatus>>(response.Content);

            if(null != result.Error) throw new Aria2cException(result.Error.Code, result.Error.Message);

            return result.Result;
        }

        public virtual Aria2cDownloadStatus GetProgress(string gid)
        {
            _logger.Info("Recuperando progresso de {0}.", gid);

            IRestResponse response = _restClient.Execute(CreateRequest("aria2.tellStatus",new List<object>
                    {
                        gid,
                        new []
                            {
                                "status",
                                "completedLength",
                                "totalLength",
                                "downloadSpeed"
                            }
                    } ));

            var result = Newtonsoft.Json.JsonConvert.DeserializeObject<Aria2cResult<Aria2cDownloadStatus>>(response.Content);

            if (null != result.Error) throw new Aria2cException(result.Error.Code, result.Error.Message);
           

            return result.Result;
        }

        public virtual string Pause(string gid)
        {
            _logger.Info("Pausando {0}.", gid);
            IRestResponse response = _restClient.Execute(CreateRequest("aria2.forcePause",new[]{gid}));

            var result = Newtonsoft.Json.JsonConvert.DeserializeObject<Aria2cResult<string>>(response.Content);

            if (null != result.Error) throw new Aria2cException(result.Error.Code, result.Error.Message);

            return result.Result;
        }

        public virtual string Resume(string gid)
        {
            _logger.Info("Reiniciando {0}.", gid);
            IRestResponse response = _restClient.Execute(CreateRequest("aria2.unpause", new[] { gid }));

            var result = Newtonsoft.Json.JsonConvert.DeserializeObject<Aria2cResult<string>>(response.Content);

            if (null != result.Error) throw new Aria2cException(result.Error.Code, result.Error.Message);

            return result.Result;
        }

        public virtual string Stop(string gid)
        {
            _logger.Info("Parando e removendo {0}.", gid);
            IRestResponse response = _restClient.Execute(CreateRequest("aria2.forceRemove", new[] { gid }));

            var result = Newtonsoft.Json.JsonConvert.DeserializeObject<Aria2cResult<string>>(response.Content);

            if (null != result.Error) throw new Aria2cException(result.Error.Code, result.Error.Message);

            return result.Result;
        }

        public virtual string Remove(string gid)
        {
            _logger.Info("Excluindo dados de {0}.", gid);
            IRestResponse response = _restClient.Execute(CreateRequest("aria2.removeDownloadResult", new[] { gid }));

            var result = Newtonsoft.Json.JsonConvert.DeserializeObject<Aria2cResult<string>>(response.Content);

            if (null != result.Error) throw new Aria2cException(result.Error.Code, result.Error.Message);

            return result.Result;
        }

        private byte[] GetTorrent(string path)
        {
            return File.ReadAllBytes(path);
        }

        protected virtual IRestRequest CreateRequest<TParameters>(string method, TParameters parameters = default(TParameters))
        {
            var request = new RestRequest(_config.JsonrpcUrl)
                {
                    RequestFormat = DataFormat.Json
                };

            if (null != parameters)
                request.AddBody(new
                    {
                        jsonrpc = _config.JsonrpcVersion,
                        id = _config.Id,
                        method,
                        @params = parameters
                    });
            else
                request.AddBody(new
                    {
                        jsonrpc = _config.JsonrpcVersion,
                        id = _config.Id,
                        method,
                    });

            request.Method = Method.POST;

            return request;
        }

        public event EventHandler<Aria2cClientEventArgs> DownloadCompleted;
        public event EventHandler<Aria2cClientEventArgs> DownloadPaused;
        public event EventHandler<Aria2cClientEventArgs> DownloadError;
        public event EventHandler<Aria2cClientEventArgs> DownloadStoped;
        public event EventHandler<Aria2cClientEventArgs> DownloadStarted;
        public event EventHandler<Aria2cClientEventArgs> DownloadProgress;
    }
}