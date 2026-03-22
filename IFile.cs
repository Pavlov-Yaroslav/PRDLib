using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TMPLAB1
{
    public interface IFile
    {
        public void Create();
        public void Open();
        public string Input(string argument);
        public string Delete(string name);
        public void Print(string name);
        public void Restore(string name);
        public void Truncate();
        public bool IsOpen { get; set; }
        public string CurrentFileName { get; set; }
        public IFileHeader FileHeader { get; set; }
        public IRecord Record { get; set; }
    }
}
