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
using System.Security.Cryptography;
using System.Threading.Tasks;
using static System.Formats.Asn1.AsnWriter;

namespace FileMakerService
{
    public class FileMakerWorker : BackgroundService
    {
        /// <summary>
        /// ������
        /// </summary>
        private readonly ILogger<FileMakerWorker> _logger;
        /// <summary>
        ///����� ����� �������
        /// </summary>
        private readonly IHostApplicationLifetime _hostApplicationLifetime;
        CancellationTokenSource tokenSource = new CancellationTokenSource();

        /// <summary>
        /// ��������� �������
        /// </summary>
        protected MainOptions _options = null;
        /// <summary>
        /// ��������� ��������
        /// </summary>
        protected ILanDocsProvider _landocs = null;
        /// <summary>
        /// ��������� ����������
        /// </summary>
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
                long taskID = 0;
                try
                {
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
                    taskID = long.Parse(item.TaskID);

                    _logger.LogInformation($"������� ������ �� ������������ ����� ������������ TaskID={item.TaskID}, VersionID={item.FileID}, FileName={item.FileName}");

                    _logger.LogInformation($"���������� ����� VersionID={item.FileID}, FileName={item.FileName}");
                    string fileData = GetStringFileArray(item.FileID);
                    _logger.LogInformation($"���� VersionID={item.FileID}, FileName={item.FileName} ������");
                    _logger.LogInformation($"��������� ������� ��� ����� VersionID={item.FileID}, FileName={item.FileName}");
                    item.Stamps = GetStamp(item.FileID);
                    if (item.Stamps == null)
                    {
                        _logger.LogInformation($"������� ��� ����� VersionID={item.FileID}, FileName={item.FileName} �� �������. ���� ������������ ����������� �� �����");
                        ChangeTaskState(taskID, "SUCCESS", "EndDatetime", $"������� ��� ����� VersionID={item.FileID}, FileName={item.FileName} �� �������. ���� ������������ ����������� �� �����");
                        Thread.Sleep(_options.ServiceOptions.QueueCheckPeriod);
                        continue;
                    }
                    _logger.LogInformation($"��� ����� VersionID={item.FileID}, FileName={item.FileName} ������� {item.Stamps.Count} �������");

                    _logger.LogInformation($"������ ������������ ����� ������������ ��� ����� VersionID={item.FileID}, FileName={item.FileName}");
                    byte[] resultFile = DrawStampOnFile(item.Stamps, fileData);
                    if(resultFile==null)
                    {
                        _logger.LogError($"������ ������������ ����� ������������ ��� ����� VersionID={item.FileID}, FileName={item.FileName}");
                        throw new Exception($"������ ������������ ����� ������������ ��� ����� VersionID={item.FileID}, FileName={item.FileName}");                       
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
                    ChangeTaskState(taskID, "SUCCESS", "EndDatetime", $"��� ����� ID={item.FileID} ����������� ���� ������������ ID={newFileID}");
                    
                    Thread.Sleep(_options.ServiceOptions.QueueCheckPeriod);

                }
                catch (Exception ex)
                {
                    _logger.LogError($"������:{ex.Message}");
                    ChangeTaskState(taskID, "ERROR", "EndDatetime", "������ ������������ ����� ������������: "+ex.Message);
                    Thread.Sleep(_options.ServiceOptions.QueueCheckPeriod);
                    continue;
                }
            }
            tokenSource.Cancel();
            _hostApplicationLifetime.StopApplication();
        }



        /// <summary>
        /// ��������� ���������� ������� �� ������������ ����� ������������ (� �����������)
        /// </summary>
        /// <returns>������� ������� �� ������������ ����� ������������</returns>        
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
                ChangeTaskState(taskID, "ERROR", "EndDatetime", $"������: �� ������� ��������� ��������� �������");
                throw new Exception("�� ������� ��������� ��������� �������");
            }
            Queue item = new Queue();
            item.TaskID = taskID.ToString();
            item.FileID = fileID;
            return GetFileInfo(item);
        }

        /// <summary>
        /// ��������� ������� ������������� �� �����
        /// </summary>
        /// <param name="fileID">ID �����, �� �������� ����� ������������� ���� ������������</param>
        /// <returns>������ ������� � �����������</returns>
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

        /// <summary>
        /// ������������ ����� ������������
        /// </summary>
        /// <param name="stamps">������</param>
        /// <param name="fileContent">Base64 ���������� �����</param>
        /// <returns>������ ���� ��������������� ����� ������������</returns>
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


        /// <summary>
        /// ������ ����������� ����� ������������
        /// </summary>
        /// <param name="client">Http ������ ������� PdfTool</param>
        /// <param name="taskID">TaskID ��������������� ����� ������������</param>
        /// <returns>������ ���� ��������������� ����� ������������</returns>        
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


        /// <summary>
        /// ��������� base64 ����������� ����� �� landocs 
        /// </summary>
        /// <param name="fileID">ID ����� �� landocs</param>
        /// <returns>base64 ���������� ����� �� landocs </returns>
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
        /// <summary>
        /// ������������ ����� ������������ � ��������� � landocs
        /// </summary>
        /// <param name="arr">������ ���� �������������� �����</param>
        /// <param name="docID">ID ���������, � �������� ���������� ���������� ����</param>
        /// <param name="fileName">�������� �������������� �����</param>
        /// <returns>ID �������������� �����</returns>
        private long AddFile(byte[] arr, long docID, string fileName)
        {
            _logger.LogDebug($"��������� ���� ����� PDF");
            long fileTypeID = GetPDFFileTypeID();
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
        /// <summary>
        /// �������� ������� ����� � ���������
        /// </summary>
        /// <param name="docID">ID ���������</param>
        /// <param name="fileName">�������� �����</param>
        /// <returns>0 ���� ��� �����, ID ����� ���� ���� ������</returns>
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
        /// <summary>
        /// ��������� ID  ���� ����� PDF
        /// </summary>
        /// <returns>ID ���� ����� PDF</returns>
        private long GetPDFFileTypeID()
        {
            //���� ���������, ��� ��� ��� ����� PDF �������.
            //��� ������������� ����������� �����
            return 1521;
        }
        
        /// <summary>
        ////��������� ����� ������������ �� ���������
        /// </summary>
        /// <param name="docID">ID ���������</param>
        /// <returns>���� ������������ ���������</returns>
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

        /// <summary>
        /// �������������� ����� ������������
        /// </summary>
        /// <param name="docOpeations">���� ������������ �� ���������</param>
        private void RestoreDocOperations(List<DocOperation> docOpeations)
        {
            foreach (DocOperation item in docOpeations)
            {
                _landocs.UpdateObject(0, item.ID, new Dictionary<string, string> { { "State", item.State.ToString() }, { "SignDate", item.SignDate } });
            }
        }
       /// <summary>
       /// �������������� ���� �� ���� (��������� ��� ���)
       /// </summary>
       /// <param name="oldFileID">ID ����� ��������� ��� ����� ������������</param>
       /// <param name="newFileID">ID ����� ������������</param>
        private void RestoreRights(long oldFileID, long newFileID)
        {
            _landocs.CallMethod("GRK_CALLING_MICROSERVICE_QUEUE", "GRK_SP_FILEMAKERSERVICE_RESTORERIGHTS", new Dictionary<string, string> { { "oldFileID", oldFileID.ToString() }, { "newFileID", newFileID.ToString() } });
        }

        private void ChangeTaskState(long taskId, string stateName, string dateTimeFieldName, string resultMessage = "")
        {
            if (taskId == 0) return;

            try
            {
                _logger.LogDebug($"������� ��������� ������� � ID = {taskId} � {stateName}");
                _landocs.UpdateObjectWithType(0, "GRK_CALLING_MICROSERVICE_QUEUE",
                new string[] {
                                "STATE",
                                dateTimeFieldName,//"EndDatetime",
                                "Result"
                },
                new string[] {
                                _dbVars.QueueState.GetStateByName(stateName).ToString(),
                                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                                resultMessage
                },
                new string[] { "ID" },
                new string[] { taskId.ToString() });
                _logger.LogDebug($"������� � ID = {taskId} ���������� � {stateName}");
            }
            catch
            {
                // ���������� ������, �� ������ �������� ������� ���������
                // ���� �� ���������� - �� � �����, ��� � ���������...
                _logger.LogError($"������ ����� ��������� ������� � ID = {taskId}");
            }
        }
    }

}
