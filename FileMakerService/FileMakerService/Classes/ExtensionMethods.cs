using LanDocsCustom.Standard.LanDocsConnector.Services.BMService31;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace FileMakerService.Classes
{
    public static class ExtensionMethods
    {
        public static object ConvertDBNull(this object dbValue)
        {
            return dbValue == DBNull.Value ? null : dbValue;
        }

        public static object GetValueFromTable(this Table table,int rowIndex, string columnHeader)
        {

            int columnIndex = 0;
            foreach ( var column in table.Columns)
            {
                if (column.ColumnName == columnHeader)
                    break;
                columnIndex++;
            }
            return table.Records[rowIndex].Values[columnIndex].Item;
            
            
        }
    }
}
