using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FileMakerService.Classes.Options
{
    [Serializable]
    public class ServiceOptions
    {
        /// <summary>
        ///Период проверки наличия элементов в очереди
        /// </summary>
        public int QueueCheckPeriodSec { get; set; }
        

        /// <summary>
        /// URL к сервису PdfTool
        /// </summary>
        public string PdfTool { get; set; }

        
        /// <summary>
        ////Необходимость восстанавливать лист согласования
        /// </summary>
        public bool RestoreDocOperations { get; set; }

        /// <summary>
        ////Необходимость восстанавливать права(актуально для документов с уровнем доступа ДСП)
        /// </summary>
        public bool RestoreRights { get; set; }

        /// <summary>
        ////Необходимость писать в лог информации об отсутствии заданий
        /// </summary>
        public bool WriteIdleLog { get; set; }

        // internals
        internal int QueueCheckPeriod { get { return QueueCheckPeriodSec < 60 ? QueueCheckPeriodSec * 1000 : 60000; } }


    }
}
