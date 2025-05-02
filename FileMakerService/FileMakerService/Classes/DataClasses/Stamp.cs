using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FileMakerService.Classes.DataClasses
{
    public class Stamp
    {
        /// <summary>
        ////Штамп в формате base64
        /// </summary>
        public string Data { get; set; }
        /// <summary>
        //с//Номер траницы
        /// </summary>
        public int PageNumber { get; set; }
        /// <summary>
        /// Координата X
        /// </summary>
        public int X { get; set; }
        /// <summary>
        ////Координата Y
        /// </summary>
        public int Y { get; set; }
        /// <summary>
        ////Ширина штампа
        /// </summary>
        public int Width { get; set; }
        /// <summary>
        /// Высота штампа
        /// </summary>
        public int Height { get; set; }
    }
}
