using System.Text;

/// <summary>
/// Тип компонента.
/// </summary>
enum Type { PRODUCT, ASSEMBLY, DETAIL, UNKNOWN };

namespace TMPLAB1
{
    /// <summary>
    /// Класс для работы с PRD-файлом (список компонентов).
    /// Поддерживает создание, чтение, удаление, восстановление и сжатие записей.
    /// </summary>
    public class PRD : IFile
    {
        // Смещение до поля p_Next в записи PRD: flag(1) + p_FirstComp(4)
        const int PRD_NEXT_OFFSET = 5;

        // Смещение до поля p_Next в записи PRS: flag(1) + p_Product(4) + p_Detail(4) + multiOcc(2)
        const int PRS_NEXT_OFFSET = 11;

        public byte[] NameSpec { get; set; } = new byte[16];
        public bool IsOpen { get; set; }
        public string CurrentFileName { get; set; }

        private string _currentDirectory;

        public HeaderPRD Header { get; set; } = new HeaderPRD();

        public IFileHeader FileHeader
        {
            get => Header;
            set => Header = (HeaderPRD)value;
        }

        public RecordPRD Record { get; set; } = new RecordPRD();

        IRecord IFile.Record
        {
            get => Record;
            set => Record = (RecordPRD)value;
        }

        /// <summary>
        /// Инициализация объекта PRD без создания файла.
        /// </summary>
        public PRD(string fileName)
        {
            CurrentFileName = fileName;
            _currentDirectory = Path.GetDirectoryName(fileName) ?? string.Empty;
        }

        /// <summary>
        /// Инициализация объекта PRD с заданной длиной записи.
        /// </summary>
        public PRD(string fileName, string recLen)
        {
            CurrentFileName = fileName;
            _currentDirectory = Path.GetDirectoryName(fileName) ?? string.Empty;
            Header.RecordLen = ushort.Parse(recLen);
            Header.p_FirstRecord = -1;
            Header.p_FreeSpace = 0;
        }

        /// <summary>
        /// Читает запись PRD (флаг, указатели и имя) из текущей позиции потока.
        /// </summary>
        private (RecordPRD, string) ReadRecord(BinaryReader br)
        {
            RecordPRD read = new RecordPRD(
                br.ReadByte(),
                br.ReadInt32(),
                br.ReadInt32(),
                br.ReadBytes(Header.RecordLen)
            );

            string recordName = Encoding.UTF8.GetString(read.Name).TrimEnd('\0');
            return (read, recordName);
        }

        /// <summary>
        /// Создает новый PRD-файл и связанный PRS-файл.
        /// </summary>
        public void Create()
        {
            string pureName = Path.GetFileNameWithoutExtension(CurrentFileName);
            string prsName = pureName + ".prs";

            Console.WriteLine(prsName);

            if (File.Exists(CurrentFileName))
            {
                Console.WriteLine($"Файл {CurrentFileName} уже существует!");

                try
                {
                    using (BinaryReader br = new BinaryReader(File.OpenRead(CurrentFileName)))
                    {
                        byte[] signature = br.ReadBytes(2);

                        if (signature.Length < 2)
                        {
                            throw new Exception("Ошибка: Сигнатура отсутствует");
                        }

                        string signatureStr = Encoding.ASCII.GetString(signature);

                        if (signatureStr != "PS")
                        {
                            throw new Exception($"Ошибка: Неверная сигнатура файла. Ожидание 'PS', получено '{signatureStr}'");
                        }
                    }
                }
                catch (Exception ex)
                {
                    throw new Exception($"Ошибка при чтении файла: {ex.Message}");
                }

                while (true)
                {
                    Console.Write("Хотите пересоздать файл с данным именем? (y/n): ");
                    char res = Console.ReadKey(true).KeyChar;

                    if ((res == 'n') || (res == 'N')) return;
                    if ((res == 'y') || (res == 'Y')) break;
                }
            }

            Header.NameSpec = Encoding.ASCII.GetBytes(prsName.PadRight(16));

            using (BinaryWriter bw = new BinaryWriter(File.Create(CurrentFileName)))
            {
                bw.Write(Header.Signature);
                bw.Write(Header.RecordLen);
                bw.Write(Header.p_FirstRecord);
                bw.Write(Header.p_FreeSpace);
                bw.Write(Header.NameSpec);
            }

            Console.WriteLine($"Файл {CurrentFileName} создан.");

            string prsFullPath = Path.Combine(_currentDirectory, prsName);
            PRS prsFile = new PRS(prsFullPath);
            prsFile.Create();

            IsOpen = true;
            Console.WriteLine($"Файл {CurrentFileName} открыт для работы.");
        }

        /// <summary>
        /// Открывает существующий PRD-файл.
        /// </summary>
        public void Open()
        {
            if (!File.Exists(CurrentFileName))
            {
                throw new Exception($"Файла {CurrentFileName} не существует");
            }

            try
            {
                using (BinaryReader br = new BinaryReader(File.OpenRead(CurrentFileName)))
                {
                    Header.Signature = br.ReadBytes(2);
                    string signatureStr = Encoding.ASCII.GetString(Header.Signature);

                    if (signatureStr != "PS")
                    {
                        throw new Exception("Неверная сигнатура файла");
                    }

                    Header.RecordLen = br.ReadUInt16();
                    Header.p_FirstRecord = br.ReadInt32();
                    Header.p_FreeSpace = br.ReadInt32();
                    Header.NameSpec = br.ReadBytes(16);

                    if (NameSpec.Length < 16)
                    {
                        throw new Exception("Файл поврежден: неполный заголовок");
                    }
                }

                IsOpen = true;
                Console.WriteLine($"Файл {CurrentFileName} открыт");
            }
            catch (Exception ex)
            {
                throw new Exception($"Ошибка при открытии файла: {ex.Message}");
            }
        }

        /// <summary>
        /// Преобразует строковое представление типа в enum.
        /// </summary>
        private Type GetComponentType(string typeName)
        {
            if (typeName == "Изделие") return Type.PRODUCT;
            if (typeName == "Узел") return Type.ASSEMBLY;
            if (typeName == "Деталь") return Type.DETAIL;

            return Type.UNKNOWN;
        }

        /// <summary>
        /// Добавляет новый компонент в файл.
        /// </summary>
        public string Input(string argument)
        {
            if (!IsOpen)
            {
                throw new Exception("Файл не открыт");
            }

            string[] parts = argument
                .Replace("(", "")
                .Replace(")", "")
                .Replace(",", "")
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            string name = parts[0];
            string typeStr = parts[1];
            Type type = GetComponentType(typeStr);

            if (type == Type.UNKNOWN)
            {
                throw new Exception("Неизвестный тип компонента");
            }

            if (name.Length > Header.RecordLen)
            {
                throw new Exception("Превышена максимальная длина имени");
            }

            byte[] nameBytes = Encoding.UTF8.GetBytes(name);

            RecordPRD newRecord = new RecordPRD(0, -1, Header.p_FirstRecord, nameBytes);

            using (FileStream fs = new FileStream(CurrentFileName, FileMode.Open, FileAccess.ReadWrite))
            using (BinaryReader br = new BinaryReader(fs))
            using (BinaryWriter bw = new BinaryWriter(fs))
            {
                int currentOffset = Header.p_FirstRecord;

                // Проверка на уникальность имени компонента
                while ((currentOffset != -1) && (currentOffset < fs.Length))
                {
                    fs.Seek(currentOffset, SeekOrigin.Begin);

                    (RecordPRD read, string nameStr) = ReadRecord(br);

                    if ((nameStr == name) && (!read.IsDeleted))
                    {
                        throw new Exception($"Компонент с именем '{name}' уже существует!");
                    }

                    currentOffset = read.p_Next;
                }

                fs.Seek(4, SeekOrigin.Begin);
                int oldP_FirstRec = br.ReadInt32();

                fs.Seek(8, SeekOrigin.Begin);
                int oldP_FreeSpace = br.ReadInt32();

                fs.Seek(0, SeekOrigin.End);
                int newOffset = (int)fs.Position;

                bw.Write(newRecord.FlagDelete);
                bw.Write(newRecord.p_FirstComp);
                bw.Write(newRecord.p_Next);

                byte[] nameBuffer = new byte[Header.RecordLen];
                Array.Copy(nameBytes, nameBuffer, Math.Min(nameBytes.Length, nameBuffer.Length));

                bw.Write(nameBuffer);
                bw.Flush();

                // Обновление заголовка
                fs.Seek(4, SeekOrigin.Begin);
                bw.Write(newOffset);

                fs.Seek(8, SeekOrigin.Begin);
                int newFreeSpace = oldP_FreeSpace + 1 + 4 + 4 + Header.RecordLen;
                bw.Write(newFreeSpace);

                Header.p_FirstRecord = newOffset;
                Header.p_FreeSpace = newFreeSpace;
            }

            return $"Компонент '{name}' ({typeStr}) добавлен.";
        }

        /// <summary>
        /// Проверяет наличие ссылок на компонент в PRS.
        /// </summary>
        private void CheckRelate(int foundOffset, PRS filePRS)
        {
            using (FileStream prsStream = new(filePRS.CurrentFileName, FileMode.Open, FileAccess.ReadWrite))
            using (BinaryReader prsReader = new BinaryReader(prsStream))
            using (BinaryWriter prsWriter = new BinaryWriter(prsStream))
            {
                prsStream.Seek(0, SeekOrigin.Begin);

                filePRS.Header.p_FirstRecord = prsReader.ReadInt32();

                int currentOffset = filePRS.Header.p_FirstRecord;

                while (currentOffset != -1)
                {
                    prsStream.Seek(currentOffset, SeekOrigin.Begin);

                    filePRS.Record.FlagDelete = prsReader.ReadByte();
                    filePRS.Record.p_Product = prsReader.ReadInt32();
                    filePRS.Record.p_Detail = prsReader.ReadInt32();

                    if ((filePRS.Record.p_Product == foundOffset) || (filePRS.Record.p_Detail == foundOffset))
                    {
                        throw new Exception("На компонент имеются ссылки в спецификациях других компонент");
                    }

                    filePRS.Record.MultiOccurrence = prsReader.ReadUInt16();
                    filePRS.Record.p_Next = prsReader.ReadInt32();

                    currentOffset = filePRS.Record.p_Next;
                }
            }
        }

        /// <summary>
        /// Помечает компонент как удаленный.
        /// </summary>
        public string Delete(string name)
        {
            if (!IsOpen)
            {
                throw new Exception("Файл не открыт");
            }

            if (string.IsNullOrEmpty(name))
            {
                throw new Exception("Укажите имя компонента для удаления");
            }

            int foundOffset = -1;

            string prsFileName = Encoding.UTF8.GetString(Header.NameSpec).TrimEnd('\0');
            string prsFullPath = Path.Combine(_currentDirectory, prsFileName);
            PRS filePRS = new PRS(prsFullPath);

            using (FileStream fs = new FileStream(CurrentFileName, FileMode.Open, FileAccess.ReadWrite))
            using (BinaryReader br = new BinaryReader(fs))
            {
                int currentOffset = Header.p_FirstRecord;

                while ((currentOffset != -1) && (currentOffset < fs.Length))
                {
                    fs.Seek(currentOffset, SeekOrigin.Begin);

                    (RecordPRD read, string nameStr) = ReadRecord(br);

                    if ((nameStr == name) && (!read.IsDeleted))
                    {
                        foundOffset = currentOffset;
                        break;
                    }

                    currentOffset = read.p_Next;
                }
            }

            CheckRelate(foundOffset, filePRS);

            if (foundOffset == -1)
            {
                throw new Exception($"Компонент '{name}' не найден");
            }

            using (FileStream fs = new FileStream(CurrentFileName, FileMode.Open, FileAccess.ReadWrite))
            using (BinaryWriter bw = new BinaryWriter(fs))
            {
                fs.Seek(foundOffset, SeekOrigin.Begin);
                bw.Write((byte)0xFF);
            }

            return $"Компонент '{name}' помечен как удаленный.";
        }

        /// <summary>
        /// Восстанавливает компонент.
        /// </summary>
        public void Restore(string name)
        {
            if (!IsOpen)
            {
                throw new Exception("Файл не открыт");
            }

            if (string.IsNullOrEmpty(name))
            {
                throw new Exception("Укажите имя компонента для восстановления");
            }

            if (name == "*")
            {
                RestoreAll();
                return;
            }

            using (FileStream fs = new FileStream(CurrentFileName, FileMode.Open, FileAccess.ReadWrite))
            using (BinaryReader br = new BinaryReader(fs))
            {
                int currentOffset = Header.p_FirstRecord;
                int foundOffset = -1;

                while ((currentOffset != -1) && (currentOffset < fs.Length))
                {
                    fs.Seek(currentOffset, SeekOrigin.Begin);

                    (RecordPRD read, string nameStr) = ReadRecord(br);

                    if (nameStr == name)
                    {
                        if (!read.IsDeleted)
                        {
                            throw new Exception($"Компонент '{name}' не удален");
                        }

                        foundOffset = currentOffset;
                        break;
                    }

                    currentOffset = read.p_Next;
                }

                if (foundOffset == -1)
                {
                    throw new Exception($"Компонент '{name}' не найден");
                }

                using (BinaryWriter bw = new BinaryWriter(fs))
                {
                    fs.Seek(foundOffset, SeekOrigin.Begin);
                    bw.Write((byte)0x00);
                }

                Console.WriteLine($"Компонент '{name}' восстановлен.");
            }
        }

        /// <summary>
        /// Восстанавливает все удаленные компоненты.
        /// </summary>
        private void RestoreAll()
        {
            using (FileStream fs = new FileStream(CurrentFileName, FileMode.Open, FileAccess.ReadWrite))
            using (BinaryReader br = new BinaryReader(fs))
            using (BinaryWriter bw = new BinaryWriter(fs))
            {
                int currentOffset = Header.p_FirstRecord;
                int restoredCount = 0;

                while ((currentOffset != -1) && (currentOffset < fs.Length))
                {
                    fs.Seek(currentOffset, SeekOrigin.Begin);

                    (RecordPRD read, string nameStr) = ReadRecord(br);

                    if (read.IsDeleted)
                    {
                        fs.Seek(currentOffset, SeekOrigin.Begin);
                        bw.Write((byte)0x00);
                        restoredCount++;
                    }

                    currentOffset = read.p_Next;
                }

                Console.WriteLine($"Восстановлено компонентов: {restoredCount}");
            }
        }

        /// <summary>
        /// Удаляет помеченные записи и пересобирает файл.
        /// Обновляет смещения записей и синхронизирует PRS.
        /// </summary>
        public void Truncate()
        {
            string tempFile = Path.GetTempFileName();
            int newFirstRec = -1;
            int lastValidOffset = -1;
            int removedCount = 0;

            // Сохраняем маппинг старых offset -> новые для PRS
            Dictionary<int, int> offsetMapForPrs = new Dictionary<int, int>();

            try
            {
                using (FileStream sourceFs = new FileStream(CurrentFileName, FileMode.Open, FileAccess.Read))
                using (FileStream destFs = new FileStream(tempFile, FileMode.Create, FileAccess.Write))
                using (BinaryReader br = new BinaryReader(sourceFs))
                using (BinaryWriter bw = new BinaryWriter(destFs))
                {
                    sourceFs.Seek(0, SeekOrigin.Begin);

                    byte[] signature = br.ReadBytes(2);
                    ushort recordLen = br.ReadUInt16();
                    int oldP_FirstRec = br.ReadInt32();
                    int oldP_FreeSpace = br.ReadInt32();
                    byte[] nameSpec = br.ReadBytes(16);

                    bw.Write(signature);
                    bw.Write(recordLen);
                    bw.Write(-1);  // временный p_FirstRecord
                    bw.Write(0);
                    bw.Write(nameSpec);

                    int currentOffset = Header.p_FirstRecord;

                    // ЭТАП 1: Первый проход - запомнить маппинг offset'ов
                    List<(int oldOffset, long newOffset, RecordPRD record, string name)> liveRecords = new();

                    while ((currentOffset != -1) && (currentOffset < sourceFs.Length))
                    {
                        sourceFs.Seek(currentOffset, SeekOrigin.Begin);
                        (RecordPRD read, string nameStr) = ReadRecord(br);

                        if (!read.IsDeleted)
                        {
                            long recordStart = destFs.Position;
                            offsetMapForPrs[currentOffset] = (int)recordStart;

                            bw.Write(read.FlagDelete);
                            bw.Write(read.p_FirstComp);
                            bw.Write(0); // временный p_Next
                            bw.Write(read.Name);

                            liveRecords.Add((currentOffset, recordStart, read, nameStr));

                            if (newFirstRec == -1) newFirstRec = (int)recordStart;

                            lastValidOffset = (int)recordStart;
                        }
                        else
                        {
                            removedCount++;
                        }

                        currentOffset = read.p_Next;
                    }

                    // ЭТАП 2: Второй проход - обновить p_Next указатели
                    for (int i = 0; i < liveRecords.Count; i++)
                    {
                        long recordPos = liveRecords[i].newOffset;
                        int nextPointer;

                        if (i < (liveRecords.Count - 1))
                        {
                            nextPointer = (int)liveRecords[i + 1].newOffset;
                        }
                        else
                        {
                            nextPointer = -1;
                        }

                        destFs.Seek(recordPos + PRD_NEXT_OFFSET, SeekOrigin.Begin); // +5 = flag(1) + p_FirstComp(4)
                        bw.Write(nextPointer);
                    }

                    // ЭТАП 3: Обновить заголовок
                    destFs.Seek(4, SeekOrigin.Begin);
                    bw.Write(newFirstRec);
                    destFs.Seek(8, SeekOrigin.Begin);
                    bw.Write(0);
                }

                Header.p_FirstRecord = newFirstRec;
                Header.p_FreeSpace = 0;

                File.Delete(CurrentFileName);
                File.Move(tempFile, CurrentFileName);

                // ЭТАП 4: Синхронизировать PRS
                string prsFileName = Encoding.UTF8.GetString(Header.NameSpec).TrimEnd('\0');
                string prsFullPath = Path.Combine(_currentDirectory, prsFileName);

                if (File.Exists(prsFullPath))
                {
                    UpdatePrsAfterPrdTruncate(prsFullPath, offsetMapForPrs);
                }

                Console.WriteLine($"Файл сжат. Удалено записей: {removedCount}");
            }
            catch
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
                throw;
            }
        }

        /// <summary>
        /// Обновляет указатели в PRS-файле после сжатия PRD.
        /// </summary>
        private void UpdatePrsAfterPrdTruncate(string prsFileName, Dictionary<int, int> prdOffsetMap)
        {
            string tempFile = Path.GetTempFileName();

            try
            {
                using (FileStream sourceFs = new FileStream(prsFileName, FileMode.Open, FileAccess.Read))
                using (FileStream destFs = new FileStream(tempFile, FileMode.Create, FileAccess.Write))
                using (BinaryReader br = new BinaryReader(sourceFs))
                using (BinaryWriter bw = new BinaryWriter(destFs))
                {
                    sourceFs.Seek(0, SeekOrigin.Begin);
                    int oldFirstRecord = br.ReadInt32();
                    int oldFreeSpace = br.ReadInt32();

                    bw.Write(-1); // временный p_FirstRecord
                    bw.Write(0);

                    int currentOffset = oldFirstRecord;
                    int newFirstRecord = -1;
                    List<(int oldOffset, long newOffset, int p_Product, int p_Detail, ushort multiOcc)> liveRecords = new();

                    // ЭТАП 1: Первый проход
                    while ((currentOffset != -1) && (currentOffset < sourceFs.Length))
                    {
                        sourceFs.Seek(currentOffset, SeekOrigin.Begin);

                        byte flagDelete = br.ReadByte();
                        int p_Product = br.ReadInt32();
                        int p_Detail = br.ReadInt32();
                        ushort multiOccurrence = br.ReadUInt16();
                        int p_Next = br.ReadInt32();

                        if (flagDelete != 0xFF) // Живая запись
                        {
                            long recordPos = destFs.Position;

                            // Обновляем ссылки на PRD если они есть в маппинге
                            if (prdOffsetMap.ContainsKey(p_Product)) p_Product = prdOffsetMap[p_Product];

                            if (prdOffsetMap.ContainsKey(p_Detail)) p_Detail = prdOffsetMap[p_Detail];

                            bw.Write(flagDelete);
                            bw.Write(p_Product);
                            bw.Write(p_Detail);
                            bw.Write(multiOccurrence);
                            bw.Write(0); // временный p_Next

                            liveRecords.Add((currentOffset, recordPos, p_Product, p_Detail, multiOccurrence));

                            if (newFirstRecord == -1) newFirstRecord = (int)recordPos;
                        }

                        currentOffset = p_Next;
                    }

                    // ЭТАП 2: Второй проход - обновить p_Next
                    for (int i = 0; i < liveRecords.Count; i++)
                    {
                        long recordPos = liveRecords[i].newOffset;
                        int nextPointer;
                        if (i < (liveRecords.Count - 1))
                        {
                            nextPointer = (int)liveRecords[i + 1].newOffset;
                        }
                        else
                        {
                            nextPointer = -1;
                        }

                        destFs.Seek(recordPos + PRS_NEXT_OFFSET, SeekOrigin.Begin); // +11 = flag(1) + p_Product(4) + p_Detail(4) + multiOcc(2)
                        bw.Write(nextPointer);
                    }

                    // ЭТАП 3: Обновить заголовок
                    destFs.Seek(0, SeekOrigin.Begin);
                    bw.Write(newFirstRecord);
                    bw.Write(0);
                } // <-- ВСЕ ПОТОКИ ЗАКРЫТЫ ЗДЕСЬ!

                // Теперь файлы закрыты, можем их ме��ять
                File.Delete(prsFileName);
                File.Move(tempFile, prsFileName);
            }
            catch
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
                throw;
            }
        }

        /// <summary>
        /// Выводит на экран PRD состав компонента в виде дерева
        /// </summary>
        public void Print(string name)
        {
            if (name == "*")
            {
                PrintAll();
                return;
            }

            string prsFileName = Encoding.UTF8.GetString(Header.NameSpec).TrimEnd('\0');
            string prsFullPath = Path.Combine(_currentDirectory, prsFileName);
            PRS filePRS = new PRS(prsFullPath);

            using (FileStream prsStream = new(filePRS.CurrentFileName, FileMode.Open, FileAccess.Read))
            using (BinaryReader prsReader = new(prsStream))
            using (FileStream prdStream = new(CurrentFileName, FileMode.Open, FileAccess.Read))
            using (BinaryReader prdReader = new(prdStream))
            {
                int currentOffset = Header.p_FirstRecord;
                int firstComp = 0;

                while ((currentOffset != -1) && (currentOffset < prdStream.Length))
                {
                    prdStream.Seek(currentOffset, SeekOrigin.Begin);
                    (RecordPRD read, string nameStr) = ReadRecord(prdReader);
                    string type = read.IsDetail ? "Деталь" : read.IsAssembly ? "Узел/Изделие" : "Неизвестно";
                    firstComp = read.p_FirstComp;

                    if (nameStr == name)
                    {
                        if (type == "Деталь")
                        { 
                            throw new Exception($"Компонент '{name}' является деталью!");
                        }
                        break;
                    }
                    currentOffset = read.p_Next;
                }

                if (currentOffset == -1)
                { 
                    throw new Exception($"Компонент не найден!");
                }
                    

                Console.WriteLine();
                Console.WriteLine(name);

                // === Читаем ВСЕ связи из PRS и строим карту ===
                var childrenMap = BuildChildrenMap(prsStream, prdStream, prsReader, prdReader, filePRS);

                // === Выводим дерево ===
                if (childrenMap.ContainsKey(name))
                {
                    DrawTreeFromMap(childrenMap, name, "");
                }
                Console.WriteLine();
            }
        }

        /// <summary>
        /// Создает карту указателей PRS
        /// </summary>
        private Dictionary<string, List<(string childName, bool isAssembly, string childRef, ushort count)>>
        BuildChildrenMap(FileStream prsStream, FileStream prdStream, BinaryReader prsReader, BinaryReader prdReader, PRS filePRS)
        {
            var childrenMap = new Dictionary<string, List<(string, bool, string, ushort)>>();

            prsStream.Seek(0, SeekOrigin.Begin);
            int firstRecord = prsReader.ReadInt32();
            int freeSpace = prsReader.ReadInt32();

            int offset = firstRecord;

            while ((offset != -1) && (offset < prsStream.Length))
            {
                prsStream.Seek(offset, SeekOrigin.Begin);

                filePRS.Record.FlagDelete = prsReader.ReadByte();
                filePRS.Record.p_Product = prsReader.ReadInt32();
                filePRS.Record.p_Detail = prsReader.ReadInt32();
                filePRS.Record.MultiOccurrence = prsReader.ReadUInt16();
                filePRS.Record.p_Next = prsReader.ReadInt32();

                if (filePRS.Record.IsDeleted)
                {
                    offset = filePRS.Record.p_Next;
                    continue;
                }

                // Родитель
                prdStream.Seek(filePRS.Record.p_Product, SeekOrigin.Begin);
                (RecordPRD prodRec, string prodName) = ReadRecord(prdReader);

                // Ребенок
                prdStream.Seek(filePRS.Record.p_Detail, SeekOrigin.Begin);
                (RecordPRD detailRec, string detailName) = ReadRecord(prdReader);

                bool isAssembly = detailRec.IsAssembly;
                string detailRefStr = detailRec.p_FirstComp.ToString();

                // Если ключа нет — создаём список
                if (!childrenMap.ContainsKey(prodName))
                {
                    childrenMap[prodName] = new List<(string, bool, string, ushort)>();
                }

                // Добавляем ОДИН раз
                childrenMap[prodName].Add((detailName, isAssembly, detailRefStr, filePRS.Record.MultiOccurrence));

                offset = filePRS.Record.p_Next;
            }

            return childrenMap;
        }

        /// <summary>
        /// Рисует древовидную структуру
        /// </summary>
        private void DrawTreeFromMap(
            Dictionary<string, List<(string childName, bool isAssembly, string childRef, ushort count)>> childrenMap,
            string parentName, string prefix)
        {
            if (!childrenMap.ContainsKey(parentName))
            {
                return;
            }

            var children = childrenMap[parentName];

            for (int i = 0; i < children.Count; i++)
            {
                var (childName, isAssembly, _, count) = children[i];
                bool isLast = (i == children.Count - 1);

                Console.Write(prefix);
                Console.Write(isLast ? "└── " : "├── ");

                if (count > 1)
                {
                    Console.WriteLine($"{childName} (x{count})");
                }
                else
                { 
                    Console.WriteLine(childName);
                }
                  
                if (isAssembly)
                {
                    string newPrefix = prefix + (isLast ? "    " : "│   ");
                    DrawTreeFromMap(childrenMap, childName, newPrefix);
                }
            }
        }

        /// <summary>
        /// Выводит все компоненты в виде таблицы
        /// </summary>
        private void PrintAll()
        {
            try
            {
                using (FileStream fs = new FileStream(CurrentFileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (BinaryReader br = new BinaryReader(fs))
                {
                    if (Header.p_FirstRecord == -1)
                    {
                        Console.WriteLine("Записей нет.");
                        return;
                    }

                    int offset = Header.p_FirstRecord;

                    // Рисуем шапку таблицы
                    DrawTableHeader();

                    while ((offset != -1) && (offset < fs.Length))
                    {
                        fs.Seek(offset, SeekOrigin.Begin);

                        (RecordPRD read, string nameStr) = ReadRecord(br);

                        string type = read.IsDetail ? "Деталь" : read.IsAssembly ? "Узел/Изделие" : "Неизвестно";

                        if (!read.IsDeleted)
                        {
                            DrawTableRow(nameStr, type);
                        }

                        offset = read.p_Next;
                    }

                    // Рисуем нижнюю границу таблицы
                    DrawTableFooter();
                }
            }
            catch (Exception ex)
            {
                throw new Exception("Ошибка чтения: " + ex.Message);
            }
        }

        /// <summary>
        /// Рисует таблицу компонентов
        /// </summary>
        private void DrawTableHeader()
        {
            string topLine = "┌───────────────────────────────────┬───────────────────┐";
            string headerLine = "│ Наименование                      │ Тип               │";
            string separator = "├───────────────────────────────────┼───────────────────┤";

            Console.WriteLine(topLine);
            Console.WriteLine(headerLine);
            Console.WriteLine(separator);
        }

        private void DrawTableRow(string name, string type)
        {
            // Обрезаем слишком длинные строки
            if (name.Length > 33)
            { 
                name = name.Substring(0, 30) + "...";
            }
            if (type.Length > 17)
            { 
                type = type.Substring(0, 14) + "...";
            } 

            string row = $"│ {name,-33} │ {type,-17} │";
            Console.WriteLine(row);
        }

        private void DrawTableFooter()
        {
            string bottomLine = "└───────────────────────────────────┴───────────────────┘";
            Console.WriteLine(bottomLine);
        }

        /// <summary>
        /// Класс компонента для записи в список
        /// </summary>
        public class Component
        {
            public string Name { get; set; }
            public string Type { get; set; }
        }

        /// <summary>
        /// Записывает компоненты в список и возвращает его
        /// </summary>
        public List<Component> GetAllComponents()
        {
            var components = new List<Component>();

            using (FileStream fs = new FileStream(CurrentFileName, FileMode.Open, FileAccess.Read))
            using (BinaryReader br = new BinaryReader(fs))
            {
                int offset = Header.p_FirstRecord;

                while ((offset != -1) && (offset < fs.Length))
                {
                    fs.Seek(offset, SeekOrigin.Begin);

                    Record.FlagDelete = br.ReadByte();
                    Record.p_FirstComp = br.ReadInt32();
                    Record.p_Next = br.ReadInt32();
                    Record.Name = br.ReadBytes(Header.RecordLen);
                    string name = Encoding.UTF8.GetString(Record.Name).TrimEnd('\0');

                    if (Record.FlagDelete != 0xFF) // Только активные
                    {
                        string type = Record.IsDetail ? "Деталь" : "Узел/Изделие";
                        components.Add(new Component { Name = name, Type = type });
                    }

                    offset = Record.p_Next;
                }
            }

            return components;
        }
    }
}

