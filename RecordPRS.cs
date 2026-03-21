using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TMPLAB1
{
    /// <summary>
    /// Класс записи: 
    /// флаг удаление, 
    /// указатель на изделие/узел,
    /// указатель на деталь,
    /// кратность вхождения детали,
    /// указатель на следующую запись
    /// </summary>
    public class RecordPRS : IRecord
    {
        public byte FlagDelete { get; set; }
        public int p_Product { get; set; }
        public int p_Detail { get; set; }
        public ushort MultiOccurrence { get; set; }
        public int p_Next { get; set; }

        public bool IsDeleted => FlagDelete == 0xFF;
    }
}
