using LanDocsCustom.Standard.LanDocsConnector.Constraints;
using LanDocsCustom.Standard.LanDocsConnector.Providers;
using LanDocsCustom.Standard.LanDocsConnector.Services.BMService31;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FileMakerService.Classes
{

    public class QueueState
    {
        public long CREATED;  // Состояние «Создано»
        public long READY;    // Состояние «Ожидает выполнения»
        public long PROCESS;  // Состояние «На выполнении»
        public long SUCCESS;  // Состояние «Выполнено»
        public long ERROR;    // Состояние «Ошибка выполнения»

        public long GetStateByName(string stateName)
        {
            return (long)GetType().GetField(stateName.ToUpper()).GetValue(this);
        }
    }

    public class DBVars
    {
        public QueueState QueueState;     // Состояния элементов очереди
        public long WorkerId;             // Идентификатор получателя в очереди


        public DBVars(ILanDocsProvider landocsProvider)
        {
            QueueState = new QueueState();

            if (landocsProvider == null)
            {
                throw new Exception("Передан пустой адаптер LanDocs. Переменные не могут быть загружены.");
            }
            if (!landocsProvider.Connect())
            {
                throw new Exception($"Ошибка подключения к LanDocs. Переменные не могут быть загружены.");
            }

            DataSet statesDS = landocsProvider.GetObjectList("GRK_CALLING_MICROSERVICE_STATE",
                GroupAttributes.Full, null, null);
            if (statesDS != null && statesDS.Tables.Count > 0)
            {
                foreach (DataRow row in statesDS.Tables[0].Rows)
                {
                    switch (row["Name"])
                    {
                        case "created": QueueState.CREATED = Convert.ToInt64(row["ID"]); break;
                        case "awaiting": QueueState.READY = Convert.ToInt64(row["ID"]); break;
                        case "running": QueueState.PROCESS = Convert.ToInt64(row["ID"]); break;
                        case "done": QueueState.SUCCESS = Convert.ToInt64(row["ID"]); break;
                        case "error": QueueState.ERROR = Convert.ToInt64(row["ID"]); break;
                    }
                }
            }



            WorkerId = (long)(landocsProvider.GetSingleFieldValue("GRK_CALLING_MICROSERVICE_WORKER",
                 "ID", ConstraintComparison.Equal("Name", "FileMakerService")).ConvertDBNull() ?? 0L);
            if (WorkerId == 0)
            {
                throw new Exception("Сервис ReRegStamperCrossPlatform не зарегистрирован в качестве исполнителя заданий (GRK_CALLING_MICROSERVICE_WORKER)");
            }


        }
    }

}
