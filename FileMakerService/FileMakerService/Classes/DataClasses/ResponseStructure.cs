using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FileMakerService.Classes.DataClasses
{
    public class ResponseStructure
    {
        /// <summary>
        /// ID задания
        /// </summary>
        public string TaskID { get; set; } = "";
        /// <summary>
        /// Ответ по заданию
        /// </summary>
        public string ResponseContent { get; set; } = "";
    }
}
