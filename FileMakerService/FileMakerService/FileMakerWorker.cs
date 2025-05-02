using FileMakerService.Classes;
using FileMakerService.Classes.DataClasses;
using FileMakerService.Classes.Options;
using LanDocs.Constraints.Operations;
using LanDocsCustom.Standard.LanDocsConnector.Constraints;
using LanDocsCustom.Standard.LanDocsConnector.Options.Enums;
using LanDocsCustom.Standard.LanDocsConnector.Providers;
using LanDocsCustom.Standard.LanDocsConnector.Providers.Soap;
using LanDocsCustom.Standard.LanDocsConnector.Providers.WebApi;
using LanDocsCustom.Standard.LanDocsConnector.Services.BMService31;
using Microsoft.AspNetCore.Http.Features;
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
            logger.LogInformation("������ ����������: " + Assembly.GetEntryAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion);
            logger.LogInformation($"��� �������: {Dns.GetHostName()}");
            logger.LogInformation("����� �������: " +
                string.Join(", ", Dns.GetHostEntry(Dns.GetHostName()).AddressList.
                Where(x => x.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork).ToList()));
            _logger = logger;




        }

        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                _logger.LogInformation("������ ������� �������");
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
                _logger.LogDebug($"����������� � �������");
                if (!_landocs.Connect())
                {
                    throw new Exception("��� ����������� � �������");
                }
                bool rmStarted = this.ExecuteAsync(cancellationToken).IsFaulted;
                cancellationToken.ThrowIfCancellationRequested(); // ���� ��������� � ����� ����������
                if (rmStarted)
                {
                    _logger.LogWarning("��� �������� ������ ��������� � ����������");
                    throw new Exception("����� ������������ ����� ������������ �������� � ����������. ������ ����� ����������.");
                }
                _logger.LogInformation("������ ������� ������� ��������");
            }
            catch (Exception ex)
            {
                _logger.LogError($"���������� ������ �������: {ex.Message}");

            }




        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("������ ������� ������� �����������");
            tokenSource.Cancel();
        }
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            stoppingToken = tokenSource.Token;
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    long taskId = 0;
                    if (!_landocs.Connect())
                    {
                        _logger.LogError($"��� ����������� � LanDocs");
                        Thread.Sleep(_options.ServiceOptions.QueueCheckPeriod);
                        continue;
                    }
                    if (_options.ServiceOptions.WriteIdleLog)
                        _logger.LogInformation("������ ���������� ������� �� ������������ ����� ������������");
                    Queue item = GetQueueItem();
                    if (item == null)
                    {
                        if (_options.ServiceOptions.WriteIdleLog)
                            _logger.LogInformation("������� �� ������������ ����� ������������ �� �������");
                        Thread.Sleep(_options.ServiceOptions.QueueCheckPeriod);
                        continue;
                    }

                    _logger.LogInformation($"������� ������ �� ������������ ����� ������������ TaskID={item.TaskID}, VersionID={item.FileID}, FileName={item.FileName}");

                    _logger.LogInformation($"���������� ����� VersionID={item.FileID}, FileName={item.FileName}");
                    string fileData = GetStringFileArray(item.FileID);
                    _logger.LogInformation($"���� VersionID={item.FileID}, FileName={item.FileName} ������");
                    _logger.LogInformation($"��������� ������� ��� ����� VersionID={item.FileID}, FileName={item.FileName}");
                    item.Stamps = GetStamp(item.FileID);
                    if (item.Stamps == null)
                    {
                        _logger.LogInformation($"������� ��� ����� VersionID={item.FileID}, FileName={item.FileName} �� �������. ���� ������������ ����������� �� �����");
                        Thread.Sleep(_options.ServiceOptions.QueueCheckPeriod);
                        continue;
                    }
                    _logger.LogInformation($"��� ����� VersionID={item.FileID}, FileName={item.FileName} ������� {item.Stamps.Count} �������");

                    _logger.LogInformation($"������ ������������ ����� ������������ ��� ����� VersionID={item.FileID}, FileName={item.FileName}");
                    byte[] resultFile = DrawStampOnFile(item.Stamps, fileData);
                    if(resultFile==null)
                    {
                        _logger.LogInformation($"������ ������������ ����� ������������ ��� ����� VersionID={item.FileID}, FileName={item.FileName}");
                        Thread.Sleep(_options.ServiceOptions.QueueCheckPeriod); 
                        continue;
                    }


                    List<DocOperation> operations = new List<DocOperation>();
                    if(_options.ServiceOptions.RestoreDocOperations)
                    {
                        _logger.LogInformation($"���������� ����� ������������");
                        operations = GetDocOperations(item.DocumentID);
                        _logger.LogInformation($"���� ������������ ��������");
                        _logger.LogDebug($"��������� ���� ������������");
                        foreach (DocOperation operation in operations)
                        {
                            _logger.LogDebug($" ID={operation.ID}\n State={operation.State}\n SignDate={operation.SignDate}");
                        }
                    }
                    _logger.LogInformation($"������������ ����� FileName={item.FileName + "_�����"} � ��������� ID={item.DocumentID}");
                    long newFileID=AddFile(resultFile, item.DocumentID, item.FileName + "_�����");
                    _logger.LogInformation($"���� FileName={item.FileName + "_�����"} ���������� � ��������� ID={item.DocumentID}. ID ������������ �����={newFileID}");

                    if (_options.ServiceOptions.RestoreDocOperations && operations!=null && operations?.Count>0)
                    {
                        _logger.LogInformation($"�������������� ����� ������������");
                        RestoreDocOperations(operations);
                        _logger.LogInformation($"���� ������������ ������������");
                    }
                    if(_options.ServiceOptions.RestoreRights)
                    {
                        _logger.LogInformation($"�������������� ���� �� ���� ID={newFileID}");
                        RestoreRights(item.FileID,newFileID);
                        _logger.LogInformation($"����� �� ���� ID={newFileID} �������������");
                    }
                    Thread.Sleep(_options.ServiceOptions.QueueCheckPeriod);

                }
                catch (Exception ex)
                {
                    _logger.LogError($"������:{ex.Message}");                    
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

                _logger.LogDebug($"������� �� ������������ ����� ������������ �� �������");
                Thread.Sleep(_options.ServiceOptions.QueueCheckPeriod);
                return null;
            }
            _logger.LogDebug("������� ����� ������� �� ������������ ����� ������������");


            taskID = Convert.ToInt64(tbl.GetValueFromTable(0, "ID"));
            string taskParams = tbl.GetValueFromTable(0, "Params").ToString();
            if (!long.TryParse(taskParams, out long fileID))
            {
                throw new Exception("�� ������� ��������� ��������� �������");
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
                _logger.LogDebug($"��� ����� ID={fileID} ������� �� �������");
                return null;
            }
            _logger.LogDebug($"��� ����� ID={fileID} ������� ������� - {ds.Tables[0].Rows.Count}");
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
                    var stringContent = new StringContent(tmp);
                    _logger.LogDebug($"���� ������� �� ������� ������ � �������� PDF={tmp}");

                    stringContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                    HttpResponseMessage response = client.PostAsync(url, stringContent).GetAwaiter().GetResult();
                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.LogDebug($"������ ��� ����������� ������: {response.StatusCode} �������� �������. ResponseContent: {response.Content.ReadAsStringAsync().Result}");
                        response = client.PostAsync(url, stringContent).GetAwaiter().GetResult();
                    }
                    if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.OK)
                    {
                        ResponseStructure result = JsonConvert.DeserializeObject<ResponseStructure>(response.Content.ReadAsStringAsync().Result);
                        return GetFileWithStamp(client, result.TaskID);

                    }
                    else
                    {
                        _logger.LogError($"������ ��� ����������� ������: {response.StatusCode}. ResponseContent: {response.Content.ReadAsStringAsync().Result}");
                        throw new Exception($"������ ��� ����������� ������: {response.StatusCode}. ResponseContent: {response.Content.ReadAsStringAsync().Result}");
                        
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
                _logger.LogDebug($"������ ��� ����������� ������: {response.StatusCode} �������� �������. ResponseContent: {response.Content.ReadAsStringAsync().Result}");
                response = client.GetAsync(url).GetAwaiter().GetResult();
            }
            if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.OK)
            {
                ResponseStructure result = JsonConvert.DeserializeObject<ResponseStructure>(response.Content.ReadAsStringAsync().Result);
                return Convert.FromBase64String(result.ResponseContent);
            }
            else
            {
                throw new Exception($"������ ��� ����������� ������: {response.StatusCode} �������� �������. ResponseContent: {response.Content.ReadAsStringAsync().Result}");
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
                _logger.LogDebug($"��� ����� ID={item.FileID} �������� �� ������");
                return null;
            }
            item.DocumentID = long.Parse(ds?.Tables?[0].Rows?[0]["DocumentID"]?.ToString());
            item.FileName = ds?.Tables?[0].Rows?[0]["Name"]?.ToString();

            return item;
        }
        private long AddFile(byte[] arr, long docID, string fileName)
        {
            _logger.LogDebug($"��������� ���� ����� PDF");
            long fileTypeID = GetFileType();
            _logger.LogDebug($"��� ����� PDF={fileTypeID}");
            _logger.LogDebug($"�������� ������� ����� {fileName} � ��������� ID={docID}");
            long fileID = IsExistsFile(docID, fileName);
            if (fileID==0)
            {
                _logger.LogDebug($"���� {fileName} � ��������� ID={docID} �� ������. ��������� ����� ����");
                long newFileID= _landocs.AddFileToDocument(docID, fileTypeID, fileName, arr);
                _logger.LogDebug($"���� {fileName} ������ ID={newFileID}");
                return newFileID;
            }
            else
            {
                _logger.LogDebug($"���� {fileName} � ��������� ID={docID} ������ ID={fileID}. ��������� ����� ������ �����");
                long newFileID = _landocs.AddVersionToFile(fileID, fileTypeID, arr);
                _logger.LogDebug($"����� ������ ����� {fileName} ������� ID={newFileID}");
                return newFileID;
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
        private List<DocOperation> GetDocOperations(long docID)
        {
            List<DocOperation> docOperations = new List<DocOperation>();
            ConstraintComparison constraint = new ConstraintComparison(ComparisonOperation.EQUAL, "DocID", docID.ToString());
            DataSet ds = _landocs.GetObjectList("DOCOPERATION", GroupAttributes.None, new string[] { "ID", "State", "SignDate" },constraint);
            if (ds == null || ds.Tables?.Count == 0 || ds?.Tables?[0]?.Rows?.Count == 0)
                return null;
            foreach (DataRow row in ds.Tables[0].Rows)
            {
                DocOperation item = new DocOperation
                {
                    ID = long.Parse(row["ID"].ToString() ?? "0"),
                    State = int.Parse(row["State"].ToString() ?? "0"),
                    SignDate = row["SignDate"].ToString() ?? ""
                };
                docOperations.Add(item);
            }
            return docOperations;
        }

        private void RestoreDocOperations(List<DocOperation> docOpeations)
        {
            foreach (DocOperation item in docOpeations)
            {
                _landocs.UpdateObject(0, item.ID, new Dictionary<string, string> { { "State", item.State.ToString() }, { "SignDate", item.SignDate } });
            }
        }
        private void RestoreRights(long oldFileID, long newFileID)
        {
            _landocs.CallMethod("GRK_CALLING_MICROSERVICE_QUEUE", "GRK_SP_FILEMAKERSERVICE_RESTORERIGHTS", new Dictionary<string, string> { { "oldFileID", oldFileID.ToString() }, { "newFileID", newFileID.ToString() } });
        }
    }

}
