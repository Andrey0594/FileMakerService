using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FileMakerService.Classes.DataClasses
{
    public class DocOperation
    {
        /// <summary>
        /// ID записи листа согласования
        /// </summary>
        public long ID { get; set; }

        /// <summary>
        /// Состояние записи листа согласования
        /// </summary>
        public int State { get; set; }
        /// <summary>
        /// Дата выполнения операции из листа согласования
        /// </summary>
        public string SignDate { get; set; }
    }
}
