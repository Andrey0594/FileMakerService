using FileMakerService.Classes;
using FileMakerService.Classes.Options;
using LanDocs.Constraints.Operations;
using LanDocsCustom.Standard.LanDocsConnector.Constraints;
using LanDocsCustom.Standard.LanDocsConnector.Options.Enums;
using LanDocsCustom.Standard.LanDocsConnector.Providers;
using LanDocsCustom.Standard.LanDocsConnector.Providers.Soap;
using LanDocsCustom.Standard.LanDocsConnector.Providers.WebApi;
using LanDocsCustom.Standard.LanDocsConnector.Services.BMService31;
using Newtonsoft.Json;
using NLog;
using NLog.Extensions.Logging;
using System;
using System.Data;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Mime;
using System.Reflection;
using System.Threading.Tasks;
using static System.Formats.Asn1.AsnWriter;

namespace FileMakerService
{
    public class FileMakerWorker : BackgroundService
    {
        private readonly ILogger<FileMakerWorker> _logger;
        private readonly IHostApplicationLifetime _hostApplicationLifetime;
        CancellationTokenSource tokenSource = new CancellationTokenSource();


        protected MainOptions _options = null;
        protected ILanDocsProvider _landocs = null;
        protected DBVars _dbVars = null;


        public FileMakerWorker(ILogger<FileMakerWorker> logger, IHostApplicationLifetime hostApplicationLifetime)
        {
            _hostApplicationLifetime = hostApplicationLifetime;
            logger.LogInformation("Версия приложения: " + Assembly.GetEntryAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion);
            logger.LogInformation($"Имя сервера: {Dns.GetHostName()}");
            logger.LogInformation("Адрес сервера: " +
                string.Join(", ", Dns.GetHostEntry(Dns.GetHostName()).AddressList.
                Where(x => x.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork).ToList()));
            _logger = logger;




        }

        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("Запуск потоков сервиса");
                _options = OptionsLoader<MainOptions>.LoadOptionsFromJson() ?? MainOptions.LoadDefaultOptions();
                ILoggerFactory loggerFactory = LoggerFactory.Create(builder => builder.AddNLog());
                if (_options.LanDocsConnectionOptions.ConnectionType == LanDocsConnectionType.WebAPI)
                {
                    _landocs = new LanDocsWebApiProvider(_options.LanDocsConnectionOptions.WebApiConnectionOptions, _logger);
                }
                else
                {
                    _landocs = new LanDocsSoapProvider(_options.LanDocsConnectionOptions.SoapConnectionOptions, _logger);
                }
                _dbVars = new DBVars(_landocs);
                _logger.LogDebug($"Подключение к серверу");
                if (!_landocs.Connect())
                {
                    throw new Exception("Нет подключения к серверу");
                }
                bool rmStarted = this.ExecuteAsync(cancellationToken).IsFaulted;
                cancellationToken.ThrowIfCancellationRequested(); // если запустили и сразу остановили
                if (rmStarted)
                {
                    _logger.LogWarning("Все основные потоки отключены в настройках");
                    throw new Exception("Поток формирования файла визуализации отключен в настройках. Сервис будет остановлен.");
                }
                _logger.LogInformation("Потоки сервиса успешно запущены");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Глобальная ошибка сервиса: {ex.Message}");

            }




        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Потоки сервиса успешно остановлены");

        }




        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            stoppingToken = tokenSource.Token;
            int i = 0;

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    long taskId = 0;
                    if (!_landocs.Connect())
                    {
                        _logger.LogError($"Нет подключения к LanDocs");
                        Thread.Sleep(_options.ServiceOptions.QueueCheckPeriod);
                        continue;
                    }
                    Queue item = GetQueueItem();
                    if (item == null)
                        continue;
                    string fileData = GetStringFileArray(item.FileID);
                    item.Stamps = GetStamp(item.FileID);
                    if (item.Stamps == null)
                    {
                        continue;
                    }
                    byte[] resultFile = DrawStampOnFile(item.Stamps, fileData);
                    AddFile(resultFile, item.DocumentID, item.FileName + "_штамп");



                }
                catch (Exception)
                {

                    throw;
                }







            }
            tokenSource.Cancel();
            _hostApplicationLifetime.StopApplication();
        }

        private Queue GetQueueItem()
        {
            long taskID = 0;
            CallResult queue = _landocs.CallMethod("GRK_CALLING_MICROSERVICE_QUEUE",
                            "grk_sp_calling_microservice_queue_search",
                            new string[] { "pWorkerID" }, new string[] { _dbVars.WorkerId.ToString() });
            Table tbl = queue == null ? null : (Table)queue.Item;
            Thread.Sleep(_options.ServiceOptions.QueueCheckPeriod);
            if (tbl == null || tbl.Records == null || tbl.Records.Length == 0)
            {

                _logger.LogDebug($"Заданий на формирование файла визуализации не найдено");
                Thread.Sleep(_options.ServiceOptions.QueueCheckPeriod);
                return null;
            }
            _logger.LogDebug("Найдено новое задание на формирование файла визуализации");


            taskID = Convert.ToInt64(tbl.GetValueFromTable(0, "ID"));
            string taskParams = tbl.GetValueFromTable(0, "Params").ToString();
            if (!long.TryParse(taskParams, out long fileID))
            {
                throw new Exception("Не удалось прочитать параметры задания");
            }
            Queue item = new Queue();
            item.TaskID = taskID.ToString();
            item.FileID = fileID;
            return GetFileInfo(item);
        }

        private List<Stamp> GetStamp(long fileID)
        {
            ConstraintComparison constraint = new ConstraintComparison(ComparisonOperation.EQUAL, "VERSION", fileID.ToString());

            DataSet ds = _landocs.GetObjectList("STAMP", GroupAttributes.None, new string[] { "XCoord", "YCoord", "VERSION", "PageNumber", "Picture", "Width", "Height" }, constraint);
            if (ds == null || ds.Tables?.Count == 0 || ds.Tables[0]?.Rows?.Count == 0)
            {
                _logger.LogInformation($"Для файла ID={fileID} штампов не найдено");
                return null;
            }
            _logger.LogInformation($"Для файла ID={fileID} найдено штампов - {ds.Tables[0].Rows.Count}");
            List<Stamp> result = new List<Stamp>();
            foreach (DataRow item in ds.Tables[0].Rows)
            {
                Stamp stamp = new Stamp();
                if (item["Picture"] is byte[])
                    stamp.Data = Convert.ToBase64String((byte[])item["Picture"]);
                else
                    stamp.Data = item["Picture"].ToString();
                stamp.X = int.Parse(item["XCoord"].ToString());
                stamp.Y = int.Parse(item["YCoord"].ToString());
                stamp.Width = int.Parse(item["Width"].ToString());
                stamp.Height = int.Parse(item["Height"].ToString());
                stamp.PageNumber = int.Parse(item["PageNumber"].ToString());
                result.Add(stamp);
            }
            return result;
        }

        private byte[] DrawStampOnFile(List<Stamp> stamps, string fileContent)
        {
            using (var handler = new HttpClientHandler())
            {
                using (HttpClient client = new HttpClient(handler))
                {
                    string url = _options.ServiceOptions.PdfTool + $"/v1/pdf/insert";
                    var data = new
                    {
                        base64Content = fileContent,
                        insertionData = stamps.Select(t => new
                        {
                            contentType = "Image",
                            base64Content = t.Data,
                            insertionParams = new
                            {
                                pagesInsertionParams = new[]
                                {
                                    new{
                                        pageNumber = t.PageNumber,
                                        scale = 1,
                                        coordinates = new
                                        {
                                            x = t.X,
                                            y = t.Y,
                                        },
                                        size = new
                                        {
                                            width=t.Width,
                                            height=t.Height
                                        }
                                    }
                                }
                            }
                        }
                    ).ToArray()
                    };
                    string tmp = JsonConvert.SerializeObject(data);
                    File.WriteAllText("f:\\Сервисы интеграции\\FileMakerService\\FileMakerService\\FileMakerService\\bin\\Debug\\net6.0\\1.json", tmp);
                    var stringContent = new StringContent(tmp);

                    stringContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                    HttpResponseMessage response = client.PostAsync(url, stringContent).GetAwaiter().GetResult();
                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.LogError($"Ошибка при простановке штампа: {response.StatusCode} Повторяю попытку. ResponseContent: {response.Content.ReadAsStringAsync().Result}");
                        response = client.PostAsync(url, stringContent).GetAwaiter().GetResult();
                    }
                    if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.OK)
                    {
                        ResponseStructure result = JsonConvert.DeserializeObject<ResponseStructure>(response.Content.ReadAsStringAsync().Result);
                        return GetFileWithStamp(client, result.TaskID);
                    }
                }
            }
            return null;
        }

        private byte[] GetFileWithStamp(HttpClient client, string taskID)
        {
            string url = _options.ServiceOptions.PdfTool + $"/v1/pdf/result?taskId={taskID}";
            HttpResponseMessage response = client.GetAsync(url).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"Ошибка при простановке штампа: {response.StatusCode} Повторяю попытку. ResponseContent: {response.Content.ReadAsStringAsync().Result}");
                response = client.GetAsync(url).GetAwaiter().GetResult();
            }
            if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.OK)
            {
                ResponseStructure result = JsonConvert.DeserializeObject<ResponseStructure>(response.Content.ReadAsStringAsync().Result);
                return Convert.FromBase64String(result.ResponseContent);
            }

            return null;
        }

        private string GetStringFileArray(long fileID)
        {
            return Convert.ToBase64String(_landocs.GetFile(fileID));
        }



        private Queue GetFileInfo(Queue item)
        {
            ConstraintComparison constraint = new ConstraintComparison(ComparisonOperation.EQUAL, "ID", item.FileID.ToString());
            DataSet ds = _landocs.GetObjectList("DOCUMENTFILE", GroupAttributes.None, new string[] { "DocumentID", "Name" }, constraint);
            if (ds == null || ds.Tables?.Count == 0 || ds?.Tables?[0]?.Rows?.Count == 0)
            {
                _logger.LogInformation($"Для файла ID={item.FileID} документ не найден");
                return null;
            }
            item.DocumentID = long.Parse(ds?.Tables?[0].Rows?[0]["DocumentID"]?.ToString());
            item.FileName = ds?.Tables?[0].Rows?[0]["Name"]?.ToString();

            return item;
        }




        private void AddFile(byte[] arr, long docID, string fileName)
        {
            long fileID = IsExistsFile(docID, fileName);
            long fileTypeID = GetFileType();

            if (fileID==0)
            {
                _landocs.AddFileToDocument(docID, fileTypeID, fileName, arr);
            }
            else
            {
                _landocs.AddVersionToFile(fileID, fileTypeID, arr);
            }
        }

        private long IsExistsFile(long docID, string fileName)
        {
            ConstraintComparison docConstraint = new ConstraintComparison(ComparisonOperation.EQUAL, "DocumentID", docID.ToString());
            ConstraintComparison fileNameConstraint = new ConstraintComparison(ComparisonOperation.EQUAL, "Name", fileName);
            ConstraintGroup groupConstraint = new ConstraintGroup(GroupOperation.AND, docConstraint, fileNameConstraint);
            DataSet ds = _landocs.GetObjectList("DOCUMENTFILE", GroupAttributes.None, new string[] { "ID", "DocumentID", "Name" }, groupConstraint);
            if (ds == null || ds.Tables?.Count == 0 || ds?.Tables?[0]?.Rows?.Count == 0)
                return 0;
            return long.Parse(ds?.Tables?[0]?.Rows?[0]?["ID"]?.ToString()??"0");
        }

        private long GetFileType()
        {
            return 1521;
        }

    }

}
