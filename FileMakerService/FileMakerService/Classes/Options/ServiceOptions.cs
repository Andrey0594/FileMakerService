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
        ///Восстанавливать лист согласования после добавления файла визуализации
        /// </summary>
        public bool RestoreDocOperation { get; set; }

        /// <summary>
        /// URL к сервису PdfTool
        /// </summary>
        public string PdfTool { get; set; }

        /// <summary>
        ///Папка для сохранения временных файлов
        /// </summary>
        public string TempFolder { get; set; }

        // internals
        internal int QueueCheckPeriod { get { return QueueCheckPeriodSec < 60 ? QueueCheckPeriodSec * 1000 : 60000; } }


    }
}
