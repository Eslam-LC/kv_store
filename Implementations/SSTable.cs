using System.Text;
using kv_store.EnumsAndConstants;
using static kv_store.EnumsAndConstants.Constants;
using static kv_store.EnumsAndConstants.ErrorCode;
using static kv_store.EnumsAndConstants.MapExToEr;
using static kv_store.EnumsAndConstants.WAOperation;

namespace kv_store.Implementations
{
    public class SSTable(long entriesCount)
    {
        static readonly byte[] _magicVersion = [.. Encoding.UTF8.GetBytes("SST"), 1];
        const int FooterSize = -52;
        public string Path { get; set; } = null!;

        /*
        [magic/version: 4B]                          // header, fixed offset 0
        [record blocks: Key | ValueLen | Value]...   // your KVPairIO records, sequential
        [sparse index: Key | Offset]...              // one entry per N records, or one per block
        [BloomFilter bytes]
        [First key][Last key]
        [Footer, FIXED SIZE, written last]:
            [IndexOffset: 8B]  [IndexLength: 4B]
            [FilterOffset: 8B] [FilterLength: 4B]
            [BitSize: 8B] [HashCount: 4B]
            [First/Last Key Offset: 8B]
            [RecordCount: 4B]
            [Magic/Version: 4B]                      // repeated at the tail too — lets you validate reading backward too
        */

        SparseIndex SparseIndex_ { get; set; } = new();
        BloomFilter BloomFilter_ { get; set; } = new(entriesCount);
        string? FirstKey;
        string? LastKey;
        FooterTag Footer { get; set; } = new() { MagicVersion = _magicVersion };

        public static ErrorCode WriteTableToFile(
            string path,
            in ImmutableSkipList<string, byte[]?> keyValues,
            int entriesCount,
            out ImmutableSSTable? immutableSSTable
        )
        {
            immutableSSTable = null;
            try
            {
                using FileStream stream = new(path, FileMode.Create, FileAccess.Write);
                using BinaryWriter w = new(stream);

                SSTable sst = new(entriesCount);

                ErrorCode errorCode;
                w.Write(_magicVersion);

                int i = 0;
                foreach (var entry in keyValues)
                {
                    if (i % 10 == 0)
                    {
                        errorCode = sst.SparseIndex_.AddEntry(entry.Key, w.BaseStream.Position);
                        if (errorCode != None)
                            return errorCode;
                    }

                    errorCode = sst.BloomFilter_.Add(entry.Key);
                    if (errorCode != None)
                        return errorCode;

                    errorCode = WARecord.WriteFrame(
                        w,
                        entry.Value == null ? DELETE : PUT,
                        entry.Key,
                        entry.Value!
                    );
                    if (errorCode != None)
                        return errorCode;

                    if (i == 0)
                        sst.FirstKey = entry.Key;
                    if (i == entriesCount - 1)
                        sst.LastKey = entry.Key;
                    i++;
                }

                sst.Footer.RecordCount = i;

                if (
                    string.IsNullOrWhiteSpace(sst.FirstKey)
                    || string.IsNullOrWhiteSpace(sst.LastKey)
                )
                    return EntryIsEmpty;

                sst.Footer.IndexOffset = w.BaseStream.Position;
                errorCode = sst.SparseIndex_.WriteIndex(w);
                if (errorCode != None)
                    return errorCode;
                sst.Footer.IndexLength = (int)(w.BaseStream.Position - sst.Footer.IndexOffset);

                sst.Footer.FilterOffset = w.BaseStream.Position;
                var filterBytes = sst.BloomFilter_.GetBytes;
                if (filterBytes == null)
                    return UnexpectedFailure;
                w.Write(filterBytes);
                sst.Footer.FilterLength = (int)(w.BaseStream.Position - sst.Footer.FilterOffset);
                sst.Footer.BitSize = sst.BloomFilter_.BitSize;
                sst.Footer.HashCount = sst.BloomFilter_.HashCount;

                sst.Footer.FirstLastKeyOffset = w.BaseStream.Position;
                w.Write(sst.FirstKey);
                w.Write(sst.LastKey);

                errorCode = sst.Footer.WriteTag(w);
                if (errorCode != None)
                    return errorCode;

                stream.Flush(true);

                sst.Path = path;

                immutableSSTable = sst.GetImmutableSSTable();
            }
            catch (Exception ex)
            {
                return GetErrorCode(ex);
            }

            return None;
        }

        public ImmutableSSTable GetImmutableSSTable()
        {
            return new(
                Path,
                Footer,
                FirstKey!,
                LastKey!,
                SparseIndex_.GetEntries(),
                BloomFilter_.GetBytes!
            );
        }

        public static ErrorCode ReadFromFileToTable(
            string path,
            out ImmutableSSTable? immutableSSTable
        )
        {
            immutableSSTable = null;
            try
            {
                using FileStream stream = new(path, FileMode.Open, FileAccess.Read);
                using BinaryReader r = new(stream);

                if (!r.ReadBytes(4).SequenceEqual(_magicVersion))
                    return FileIsCorruptedOrVersionUnsupported;
                r.BaseStream.Seek(FooterSize, SeekOrigin.End);
                var Footer_ = new FooterTag() { MagicVersion = [] };
                var errCode = Footer_.ReadTag(r);
                if (errCode != None)
                    return errCode;

                if (!Footer_.MagicVersion.SequenceEqual(_magicVersion))
                    return FileIsCorruptedOrVersionUnsupported;

                immutableSSTable = new(path, Footer_);
            }
            catch (Exception ex)
            {
                return GetErrorCode(ex);
            }

            return None;
        }

        public record FooterTag
        {
            public long IndexOffset { get; set; }
            public int IndexLength { get; set; }
            public long FilterOffset { get; set; }
            public int FilterLength { get; set; }
            public long BitSize { get; set; }
            public int HashCount { get; set; }
            public long FirstLastKeyOffset { get; set; }
            public int RecordCount { get; set; }
            public required byte[] MagicVersion { get; set; }

            public ErrorCode WriteTag(BinaryWriter w)
            {
                try
                {
                    w.Write(IndexOffset);
                    w.Write(IndexLength);
                    w.Write(FilterOffset);
                    w.Write(FilterLength);
                    w.Write(BitSize);
                    w.Write(HashCount);
                    w.Write(FirstLastKeyOffset);
                    w.Write(RecordCount);
                    w.Write(MagicVersion!);
                }
                catch (Exception ex)
                {
                    return GetErrorCode(ex);
                }
                return None;
            }

            public ErrorCode ReadTag(BinaryReader r)
            {
                try
                {
                    IndexOffset = r.ReadInt64();
                    IndexLength = r.ReadInt32();
                    FilterOffset = r.ReadInt64();
                    FilterLength = r.ReadInt32();
                    BitSize = r.ReadInt64();
                    HashCount = r.ReadInt32();
                    FirstLastKeyOffset = r.ReadInt64();
                    RecordCount = r.ReadInt32();
                    MagicVersion = r.ReadBytes(4);
                }
                catch (Exception ex)
                {
                    return GetErrorCode(ex);
                }
                return None;
            }
        }
    }

    public class ImmutableSSTable : IDisposable
    {
        public string FileName;
        public readonly byte[] MagicVersion;
        public readonly long RecordCount;
        public readonly string FirstKey;
        public readonly string LastKey;

        public readonly ImmutableSkipList<string, long> SparseIndex;
        public readonly ImmutableBloomFilter BloomFilter;

        private FileStream? stream;
        private BinaryReader? reader;
        private readonly SSTable.FooterTag footerTag;

        public ImmutableSSTable(string fileName, SSTable.FooterTag footer)
        {
            FileName = fileName;
            MagicVersion = footer.MagicVersion;
            RecordCount = footer.RecordCount;

            using FileStream s = new(FileName, FileMode.Open, FileAccess.Read);
            using BinaryReader r = new(s);
            r.BaseStream.Seek(footer.FirstLastKeyOffset, SeekOrigin.Begin);
            FirstKey = r.ReadString();
            LastKey = r.ReadString();
            r.BaseStream.Seek(footer.IndexOffset, SeekOrigin.Begin);
            var errCode = Implementations.SparseIndex.ReadIndex(
                r,
                footer.IndexLength,
                out SparseIndex
            );
            if (errCode != None)
                throw new ArgumentException("Failed to read sparse index", nameof(footer));
            r.BaseStream.Seek(footer.FilterOffset, SeekOrigin.Begin);
            BloomFilter = new(r.ReadBytes(footer.FilterLength), footer.BitSize, footer.HashCount);
            footerTag = footer;
        }

        public ImmutableSSTable(
            string fileName,
            SSTable.FooterTag footer,
            string firstKey,
            string lastKey,
            ImmutableSkipList<string, long> sparseIndex,
            byte[] filterBytes
        )
        {
            FileName = fileName;
            MagicVersion = footer.MagicVersion;
            RecordCount = footer.RecordCount;
            FirstKey = firstKey;
            LastKey = lastKey;
            SparseIndex = sparseIndex;
            BloomFilter = new(filterBytes, footer.BitSize, footer.HashCount);
            footerTag = footer;
        }

        public ErrorCode TryReadEntry(string key, out byte[]? value)
        {
            value = null;
            ErrorCode errCode;
            if (
                key.CompareTo(FirstKey, StringComparison.Ordinal) < 0
                || key.CompareTo(LastKey, StringComparison.Ordinal) > 0
            )
                return KeyWasNotFound;

            errCode = BloomFilter.Contains(key, out bool MayExist);
            if (errCode != None)
                return errCode;

            if (!MayExist)
                return KeyWasNotFound;

            if (!SparseIndex.GetValueAtOrBefore(key, out long offset))
                return KeyWasNotFound;
            if (reader == null)
            {
                stream = new(FileName, FileMode.Open, FileAccess.Read);
                reader = new(stream);
            }

            reader.BaseStream.Seek(offset, SeekOrigin.Begin);
            errCode = WARecord.ReadFrame(reader, out var op, out string? fetchedKey, out value!);

            while (
                errCode == None
                && fetchedKey != null
                && key.CompareTo(fetchedKey, StringComparison.Ordinal) > 0
                && reader.BaseStream.Position < footerTag.IndexOffset
            )
            {
                errCode = WARecord.ReadFrame(reader, out op, out fetchedKey, out value!);
            }

            if (errCode != None)
                return errCode;

            if (key.CompareTo(fetchedKey, StringComparison.Ordinal) != 0)
                return KeyWasNotFound;

            if (op == DELETE)
                return KeyWasDeleted;

            return None;
        }

        public ErrorCode Scan(
            string startKey,
            string endKey,
            out IEnumerable<KeyValuePair<string, byte[]?>> pairs
        )
        {
            pairs = [];
            if (
                endKey.CompareTo(FirstKey, StringComparison.Ordinal) < 0
                || startKey.CompareTo(LastKey, StringComparison.Ordinal) > 0
            )
                return None;

            if (!SparseIndex.GetValueAtOrBefore(startKey, out long offset))
                return UnexpectedFailure; // should be unreachable

            if (reader == null)
            {
                stream = new(FileName, FileMode.Open, FileAccess.Read);
                reader = new(stream);
            }

            List<KeyValuePair<string, byte[]?>> keyValues = [];

            reader.BaseStream.Seek(offset, SeekOrigin.Begin);
            ErrorCode errCode = WARecord.ReadFrame(
                reader,
                out var op,
                out string? fetchedKey,
                out var value
            );

            while (
                errCode == None
                && fetchedKey != null
                && endKey.CompareTo(fetchedKey, StringComparison.Ordinal) > 0
                && reader.BaseStream.Position < footerTag.IndexOffset
            )
            {
                keyValues.Add(new(fetchedKey, op == DELETE ? Deleted : value));
                errCode = WARecord.ReadFrame(reader, out op, out fetchedKey, out value!);
            }

            pairs = keyValues;
            return None;
        }

        private bool _disposed = false;

        public void Dispose()
        {
            if (!_disposed)
            {
                reader?.Close();
                stream?.Close();
                _disposed = true;
            }
            GC.SuppressFinalize(this);
        }

        ~ImmutableSSTable()
        {
            if (!_disposed)
            {
                reader?.Close();
                stream?.Close();
                _disposed = true;
            }
        }
    }
}
