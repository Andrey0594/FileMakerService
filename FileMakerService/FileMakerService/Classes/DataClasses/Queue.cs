using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FileMakerService.Classes.DataClasses
{
    public class Queue
    {
        /// <summary>
        /// ID задания 
        /// </summary>
        public string TaskID { get; set; } = "";
        /// <summary>
        ////ID исходного файла
        /// </summary>
        public long FileID { get; set; } = 0;
        /// <summary>
        /// Название исходного файла
        /// </summary>
        public string FileName { get; set; } = "";
        /// <summary>
        ////ID документа
        /// </summary>
        public long DocumentID { get; set; } = 0;
        /// <summary>
        ////Штампы
        /// </summary>
        public List<Stamp> Stamps { get; set; } = new List<Stamp>();
    }
}
