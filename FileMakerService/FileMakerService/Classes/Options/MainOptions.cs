using LanDocsCustom.Standard.LanDocsConnector.Options.Authentication;
using LanDocsCustom.Standard.LanDocsConnector.Options.Enums;
using LanDocsCustom.Standard.LanDocsConnector.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FileMakerService.Classes.Options
{
    [Serializable]
    public class MainOptions
    {

        /// <summary>
        /// Параметры подключения к LanDocs
        /// </summary>
        public LanDocsConnectionOptions LanDocsConnectionOptions;

        /// <summary>
        /// Параметры сервиса
        /// </summary>
        public ServiceOptions ServiceOptions;

        /// <summary>
        /// Инициализация параметров по умолчанию
        /// </summary>
        public static MainOptions LoadDefaultOptions()
        {
            MainOptions options = new MainOptions
            {
                LanDocsConnectionOptions = new LanDocsConnectionOptions
                {
                    // Типы подключения по умолчанию
                    ConnectionType = LanDocsConnectionType.WebAPI,

                    // Параметры подключения к BMService31
                    SoapConnectionOptions = new SoapConnectionOptions
                    {
                        BMServiceUrl = "http://172.19.90.183:5072/BMService31.asmx",
                        UtilsUrl = "http://172.19.90.183:5072/Utils.asmx",
                        TimeoutSec = 900,
                        ClearConnectionAlways = false,
                        UtilsMinimalFileSizeMB = 5,
                        LanDocsAuthentication = new SoapAuthentication
                        {
                            AuthenticationType = SoapAuthenticationType.SQL,
                            Login = "scheduler",
                            Password = "sql",
                            SsoAuthenticationKey =
            "86-4E-B6-92-12-DE-03-49-B4-7A-E5-D8-2A-0F-FC-D6-89-D8-E3-E4-BD-E7-62-40-91-14-28-96-32-98-07-81"
                        },
                        HttpAuthentication = new HttpAuthentication
                        {
                            AuthenticationType = HttpAuthenticationType.Anonymous,
                            Login = "",
                            Password = "",
                            Domain = "",
                        }
                    },

                    // Параметры подключения к WebApi
                    WebApiConnectionOptions = new WebApiConnectionOptions
                    {
                        //BaseUrl = "http://172.19.90.183:81/landocs.webapi", // вариант для Windows
                        //RelativeUrl = "api/v1/", // вариант для Windows
                        BaseUrl = "http://172.19.90.183:81", // вариант для Linux
                        RelativeUrl = "landocs.webapi/api/v1/", // вариант для Linux
                        TimeoutSec = 900,
                        ClearConnectionAlways = false,
                        LanDocsAuthentication = new WebApiAuthenticaton
                        {
                            AuthenticationType = WebApiAuthenticationType.Basic,
                            Login = "scheduler",
                            Password = "sql",
                            Domain = ""
                        }
                    }
                }
            };

            // Параметры сервиса

            options.ServiceOptions = new ServiceOptions();            
            options.ServiceOptions.PdfTool = "http://localhost:7010";
            options.ServiceOptions.RestoreDocOperations = true;
            options.ServiceOptions.RestoreRights = true;
            options.ServiceOptions.QueueCheckPeriodSec = 1;

            OptionsLoader<MainOptions>.SaveOptionsToJson(options);
            return options;
        }



    }
}
